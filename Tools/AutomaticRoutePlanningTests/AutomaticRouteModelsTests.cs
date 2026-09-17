using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public sealed class AutomaticRouteModelsTests {
    [Theory]
    [InlineData(1, 100)]
    [InlineData(2, 400)]
    [InlineData(3, 900)]
    [InlineData(4, 1000)]
    [InlineData(5, 1000)]
    [InlineData(6, 2000)]
    [InlineData(7, 2000)]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(8, 0)]
    public void Weight_table_matches_existing_ship_cargo_rules(int level, int expected) =>
        Assert.Equal(expected, CargoWeightTable.GetWeightForLevel(level));

    [Fact]
    public void Route_point_rejects_non_finite_coordinates() {
        Assert.True(new RoutePoint(1, 2).IsFinite);
        Assert.False(new RoutePoint(double.NaN, 2).IsFinite);
        Assert.False(new RoutePoint(1, double.PositiveInfinity).IsFinite);
    }

    [Fact]
    public void Split_task_identity_round_trips_to_the_planner_row() {
        string plannerRowId = "br-a490ae5f24824cc6814a0d0be3bef792";
        string first = RouteTaskIdentity.CreateSegmentId(plannerRowId, 0, 2);
        string second = RouteTaskIdentity.CreateSegmentId(plannerRowId, 1, 2);

        Assert.NotEqual(first, second);
        Assert.Equal(plannerRowId, RouteTaskIdentity.PlannerRowId(first));
        Assert.Equal(plannerRowId, RouteTaskIdentity.PlannerRowId(second));
        Assert.Equal(plannerRowId, RouteTaskIdentity.CreateSegmentId(plannerRowId, 0, 1));
        Assert.True(RouteTaskIdentity.IsSegmentId(first));
        Assert.False(RouteTaskIdentity.IsSegmentId(plannerRowId));
        Assert.True(RouteTaskIdentity.IsCompleted(
            first, new HashSet<string>([first], StringComparer.Ordinal)));
        Assert.False(RouteTaskIdentity.IsCompleted(
            second, new HashSet<string>([first], StringComparer.Ordinal)));
        Assert.True(RouteTaskIdentity.IsCompleted(
            second, new HashSet<string>([plannerRowId], StringComparer.Ordinal)));
        Assert.False(RouteTaskIdentity.AreAllTasksCompleted(
            plannerRowId,
            [first, second],
            new HashSet<string>([first], StringComparer.Ordinal)));
        Assert.True(RouteTaskIdentity.AreAllTasksCompleted(
            plannerRowId,
            [first, second],
            new HashSet<string>([first, second], StringComparer.Ordinal)));

        var step = new BarterStep(
            first,
            "Ostra",
            new RouteItemQuantity("IN", 2),
            new RouteItemQuantity("OUT", 6),
            new RouteLoadSnapshot(0, 0, 0));
        Assert.True(RouteTaskIdentity.TryGetExchangeCount(step, 1, 3, out int count));
        Assert.Equal(2, count);
        Assert.False(RouteTaskIdentity.TryGetExchangeCount(step, 2, 2, out _));

        var partial = RouteTaskIdentity.DecideCompletion(10, 2);
        Assert.True(partial.IsValid);
        Assert.False(partial.CompletesPlannerRow);
        Assert.Equal(8, partial.RemainingExchangeQuantity);
        var whole = RouteTaskIdentity.DecideCompletion(8, 8);
        Assert.True(whole.IsValid);
        Assert.True(whole.CompletesPlannerRow);
        Assert.Equal(8, whole.RemainingExchangeQuantity);
        Assert.False(RouteTaskIdentity.DecideCompletion(2, 8).IsValid);
    }

    [Fact]
    public void Objective_is_strictly_lexicographic() {
        var baseline = new RoutePlanObjective(2, 100, 3, 1_000, "b");

        Assert.True(new RoutePlanObjective(99, 99, 99, 99_999, "z").CompareTo(baseline) < 0);
        Assert.True(new RoutePlanObjective(1, 101, 0, 0, "a").CompareTo(baseline) > 0);
        Assert.True(new RoutePlanObjective(1, 100, 99, 99_999, "z").CompareTo(baseline) < 0);
        Assert.True(new RoutePlanObjective(2, 99, 99, 99_999, "z").CompareTo(baseline) < 0);
        Assert.True(new RoutePlanObjective(2, 100, 2, 99_999, "z").CompareTo(baseline) < 0);
        Assert.True(new RoutePlanObjective(2, 100, 3, 999, "z").CompareTo(baseline) < 0);
        Assert.True(new RoutePlanObjective(2, 100, 3, 1_000, "a").CompareTo(baseline) < 0);
        Assert.Equal(0, baseline.CompareTo(baseline));
    }

    [Fact]
    public void Request_and_warehouse_defensively_copy_input_collections() {
        var inventory = new Dictionary<string, int>(StringComparer.Ordinal) { ["IN"] = 5 };
        var warehouse = new RouteWarehouse("W", "W_ISLAND", new RoutePoint(0, 0), inventory);
        var tasks = new List<RouteBarterTask> {
            new("r1", "A", new RoutePoint(1, 0), "IN", 1, "OUT", 1),
        };
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["IN"] = new("IN", "Input", 1, 100),
            ["OUT"] = new("OUT", "Output", 2, 400),
        };
        var request = new AutomaticRoutePlanningRequest(
            tasks, items, [warehouse], 0, 1_000,
            new RouteSearchLimits(100, 10), "v1");

        inventory["IN"] = 99;
        tasks.Clear();
        items.Clear();

        Assert.Equal(5, warehouse.Inventory["IN"]);
        Assert.Single(request.Tasks);
        Assert.Equal(2, request.Items.Count);
    }

    [Fact]
    public void Step_collections_cannot_be_changed_through_constructor_inputs() {
        var pickupItems = new List<RouteItemQuantity> { new("IN", 2) };
        var unloadItems = new List<RouteItemQuantity> { new("OUT", 3) };
        var steps = new List<RouteStep> {
            new WarehousePickupStep("W", "W_ISLAND", pickupItems, new RouteLoadSnapshot(200, 200, 200)),
            new WarehouseUnloadStep("W", "W_ISLAND", unloadItems, new RouteLoadSnapshot(0, 0, 1_200)),
        };
        var route = new PlannedRoute(1, "W", "W", steps, 20, 200, 0, 1_200);

        pickupItems.Clear();
        unloadItems.Clear();
        steps.Clear();

        Assert.Equal(2, route.Steps.Count);
        Assert.Single(((WarehousePickupStep)route.Steps[0]).Items);
        Assert.Single(((WarehouseUnloadStep)route.Steps[1]).Items);
    }

    [Fact]
    public void Defensively_copied_collections_have_no_public_setter() {
        var collectionProperties = new[] {
            typeof(RouteWarehouse).GetProperty(nameof(RouteWarehouse.Inventory))!,
            typeof(WarehousePickupStep).GetProperty(nameof(WarehousePickupStep.Items))!,
            typeof(WarehouseUnloadStep).GetProperty(nameof(WarehouseUnloadStep.Items))!,
        };

        Assert.All(collectionProperties, property => Assert.Null(property.SetMethod));
    }
}
