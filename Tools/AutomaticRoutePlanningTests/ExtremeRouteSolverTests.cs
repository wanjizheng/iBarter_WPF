using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public sealed class ExtremeRouteSolverTests {
    [Fact]
    public void Cp_sat_worker_returns_a_replay_verified_inventory_and_capacity_safe_plan() {
        string solver = SolverPath();
        Assert.True(File.Exists(solver), $"Build the x64 solver first: {solver}");

        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["raw"] = new("raw", "Raw", 1, 100),
            ["mid"] = new("mid", "Mid", 2, 200),
            ["coin"] = new("coin", "Coin", 0, 0),
        };
        var request = new AutomaticRoutePlanningRequest(
            [
                new RouteBarterTask("producer", "A", new RoutePoint(100, 0), "raw", 2, "mid", 1),
                new RouteBarterTask("consumer", "B", new RoutePoint(200, 0), "mid", 1, "coin", 10),
            ],
            items,
            [new RouteWarehouse("W", "W", new RoutePoint(0, 0),
                new Dictionary<string, int>(StringComparer.Ordinal) { ["raw"] = 2 })],
            extraLT: 0,
            totalLT: 200,
            new RouteSearchLimits(10_000, 100),
            "extreme-test-v1");

        ExtremeRouteSolverRunResult result = ExtremeRouteSolverClient.Solve(
            request, incumbent: null, TimeSpan.FromSeconds(10), new ExtremeRouteResources(2, 1_024),
            TestContext.Current.CancellationToken, solver);

        Assert.Null(result.Failure);
        Assert.NotNull(result.Plan);
        Assert.True(RoutePlanVerifier.Verify(request, result.Plan!).Success);
        Assert.Equal(new[] { "producer", "consumer" },
            result.Plan!.Routes.SelectMany(route => route.Steps).OfType<BarterStep>().Select(step => step.RowId));
        Assert.All(result.Plan.Routes, route => Assert.True(route.PeakLT <= request.TotalLT));
    }

    [Fact]
    public void Cp_sat_worker_splits_routes_when_one_starting_load_would_be_overweight() {
        string solver = SolverPath();
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["raw"] = new("raw", "Raw", 1, 100),
            ["coin"] = new("coin", "Coin", 0, 0),
        };
        var request = new AutomaticRoutePlanningRequest(
            [
                new RouteBarterTask("a", "A", new RoutePoint(100, 0), "raw", 5, "coin", 1),
                new RouteBarterTask("b", "B", new RoutePoint(200, 0), "raw", 5, "coin", 1),
            ],
            items,
            [new RouteWarehouse("W", "W", new RoutePoint(0, 0),
                new Dictionary<string, int>(StringComparer.Ordinal) { ["raw"] = 10 })],
            extraLT: 0,
            totalLT: 500,
            new RouteSearchLimits(10_000, 100),
            "extreme-test-v2");

        ExtremeRouteSolverRunResult result = ExtremeRouteSolverClient.Solve(
            request, incumbent: null, TimeSpan.FromSeconds(10), new ExtremeRouteResources(2, 1_024),
            TestContext.Current.CancellationToken, solver);

        Assert.Null(result.Failure);
        Assert.NotNull(result.Plan);
        Assert.Equal(2, result.Plan!.Routes.Count);
        Assert.True(RoutePlanVerifier.Verify(request, result.Plan).Success);
    }

    [Fact]
    public void Cp_sat_candidate_is_normalized_before_redundant_cargo_can_reject_it() {
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["raw"] = new("raw", "Raw", 1, 100),
            ["coin"] = new("coin", "Coin", 0, 0),
        };
        var request = new AutomaticRoutePlanningRequest(
            [new RouteBarterTask("barter", "A", new RoutePoint(100, 0),
                "raw", 1, "coin", 10)],
            items,
            [new RouteWarehouse("Iliya", "Iliya", new RoutePoint(0, 0),
                new Dictionary<string, int>(StringComparer.Ordinal) { ["raw"] = 2 })],
            extraLT: 0,
            totalLT: 1_000,
            new RouteSearchLimits(10_000, 100),
            "extreme-redundant-cargo");

        var state = RouteSimulationState.CreateInitial(request);
        state = RouteStateTransition.TryPickup(
            request, state, "Iliya", [new RouteItemQuantity("raw", 2)]).State;
        state = RouteStateTransition.TryBarter(request, state, 0).State;
        state = RouteStateTransition.TryUnload(
            request, state, "Iliya", [new RouteItemQuantity("raw", 1)],
            finishRoute: true).State;
        var candidate = RoutePlanFactory.FromState(
            request, state, RoutePlanStatus.BestKnownWithinLimit, []);
        Assert.True(RoutePlanVerifier.TryDetectRouteRedundantCargoRoundTrip(
            request, candidate, out _));

        RoutePlan? accepted = ExtremeRouteSolverClient.PrepareCandidateForAcceptance(
            request, candidate, out string? failure);

        Assert.Null(failure);
        Assert.NotNull(accepted);
        var route = Assert.Single(accepted!.Routes);
        Assert.Equal(1, Assert.Single(route.Steps.OfType<WarehousePickupStep>())
            .Items.Single(item => item.ItemId == "raw").Quantity);
        Assert.Empty(Assert.Single(route.Steps.OfType<WarehouseUnloadStep>()).Items);
        Assert.False(RoutePlanVerifier.TryDetectRouteRedundantCargoRoundTrip(
            request, accepted, out _));
        Assert.True(RoutePlanVerifier.Verify(request, accepted).Success);
    }

    private static string SolverPath() {
        DirectoryInfo directory = new(AppContext.BaseDirectory);
        for (int i = 0; i < 5; i++) directory = directory.Parent!;
        return Path.Combine(directory.FullName, "Tools", "ExtremeRouteSolver", "bin", "Release",
            "net10.0", "win-x64", "iBarter.ExtremeRouteSolver.exe");
    }
}
