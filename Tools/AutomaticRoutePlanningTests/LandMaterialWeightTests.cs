using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public sealed class LandMaterialWeightTests {
    [Fact]
    public void Csv_values_override_legacy_weights_for_all_item_levels() {
        using var csv = new StringReader("\"Fig, fresh\",7018,0,300,0.12\nSlab,800022,2,1,401.25\nCoin,44,-1,1,0\n");
        var values = CargoWeightTable.ReadWeights(csv, new Dictionary<string, double> { ["7018"] = .1 });
        Assert.Equal(.12, values["7018"]);
        Assert.Equal(401.25, values["800022"]);
        Assert.Equal(0, values["44"]);
    }

    [Fact]
    public void Four_column_csv_uses_bundled_values_without_overwriting_explicit_zero() {
        using var csv = new StringReader("Brass Ingot,4066,0,100\nFig,7018,0,300,0.00\n");
        var values = CargoWeightTable.ReadWeights(csv,
            new Dictionary<string, double> { ["4066"] = .3, ["7018"] = .1 });
        Assert.Equal(.3, values["4066"]);
        Assert.Equal(0, values["7018"]);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("NaN")]
    [InlineData("text")]
    [InlineData("0.001")]
    public void Invalid_csv_weight_is_reported_instead_of_silently_ignored(string value) {
        using var csv = new StringReader($"Fig,7018,0,300,{value}\n");
        Assert.Throws<InvalidDataException>(() => CargoWeightTable.ReadWeights(csv));
    }

    private static readonly (string Id, int Level, int Quantity)[] Cargo = [
        ("7018", 0, 3000), ("5854", 0, 500), ("800022", 2, 10), ("800027", 2, 7)
    ];
    private static Dictionary<string, RouteItem> Items() => Cargo.ToDictionary(x => x.Id,
        x => new RouteItem(x.Id, x.Id, x.Level, CargoWeightTable.GetWeight(x.Id, x.Level)));

    [Theory]
    [InlineData("7018", .10)]
    [InlineData("5854", .10)]
    [InlineData("4066", .30)]
    [InlineData("4661", .50)]
    [InlineData("7921", .03)]
    public void Land_materials_use_their_actual_item_weight(string id, double weight) =>
        Assert.Equal(weight, CargoWeightTable.GetWeight(id, 0));

    private static AutomaticRoutePlanningRequest Ordinary(int capacity) => new(
        [], Items(), [new("Iliya", "Iliya", new(0, 0), Cargo.ToDictionary(x => x.Id, x => x.Quantity))],
        2411, capacity, new(100, 10), "weight-regression");

    [Theory]
    [InlineData(9561, true)]
    [InlineData(9560, false)]
    public void Ordinary_loading_counts_land_materials_at_the_capacity_boundary(int capacity, bool accepted) {
        var request = Ordinary(capacity);
        var result = RouteStateTransition.TryPickup(request, RouteSimulationState.CreateInitial(request), "Iliya",
            Cargo.Select(x => new RouteItemQuantity(x.Id, x.Quantity)).ToArray());
        Assert.Equal(accepted, result.Success);
        if (accepted) Assert.Equal(9561, result.Step!.Load.TotalWithExtraLT);
        else Assert.Equal("overweight", result.Diagnostic!.Code);
    }

    [Fact]
    public void Tagged_route_one_initial_loading_matches_the_game() {
        var request = new TaggedTransportRequest {
            ShipLimitLT = 26410, ShipOccupiedLT = 2411, Items = Items(),
            Points = new() { ["Iliya"] = new(0, 0) },
            Warehouses = new() { ["Iliya"] = Cargo.ToDictionary(x => x.Id, x => x.Quantity) },
            Settings = new() { StartIsland = "Iliya", Ports = [new() { IslandId = "Iliya", WarehouseId = "Iliya", Enabled = true }] }
        };
        var simulator = new TaggedTransportSimulator(request);
        var state = simulator.Initial();
        foreach (var item in Cargo)
            Assert.True(simulator.TryApply(state, new(TaggedActionKind.Transfer, "Iliya", "warehouse:Iliya", "ship", item.Id, item.Quantity), out state, out _));
        Assert.Equal(9561, simulator.Weight(state, "ship"));
    }

    [Fact]
    public void Manual_projection_preserves_sub_unit_weight() {
        var result = ManualCargoProjector.Project([
            new("a", "A", "7018", 3, CargoWeightTable.GetWeight("7018", 0), "out", 1, 100)
        ], 0);
        Assert.Equal(.3, result.InitialLT, 8);
        Assert.Equal(100, result.CurrentLT);
    }

    [Fact]
    public void Old_zero_weight_routes_have_a_different_fingerprint() {
        var request = Ordinary(26410);
        var legacy = new AutomaticRoutePlanningRequest(request.Tasks,
            request.Items.ToDictionary(x => x.Key, x => x.Value.Level == 0 ? x.Value with { UnitWeight = 0 } : x.Value),
            request.Warehouses, request.ExtraLT, request.TotalLT, request.Limits, request.ConfigurationVersion);
        Assert.NotEqual(RoutePlanFingerprint.Compute(request), RoutePlanFingerprint.Compute(legacy));
    }
    [Fact]
    public void Smart_solver_keeps_decimal_weight_in_integer_constraints() {
        var baseRequest = Ordinary(9561);
        var request = new AutomaticRoutePlanningRequest([
            new("row", "Island", new(1000, 0), "7018", 3000, "800022", 1)
        ], baseRequest.Items, baseRequest.Warehouses, baseRequest.ExtraLT, baseRequest.TotalLT,
            baseRequest.Limits, baseRequest.ConfigurationVersion);
        var build = typeof(ExtremeRouteSolverClient).GetMethod("BuildInput",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var dto = (ExtremeSolverInputDto)build.Invoke(null,
            [request, null, TimeSpan.FromSeconds(1), new ExtremeRouteResources(1, 512)])!;
        Assert.Equal(10, dto.Items.Single(x => x.ItemId == "7018").UnitWeight);
        Assert.Equal(715000, dto.CargoCapacityLT);
    }
}
