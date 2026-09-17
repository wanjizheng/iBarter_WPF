using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public sealed class WarehouseUnloadPlannerTests {
    [Fact]
    public void Outputs_are_split_by_the_warehouses_that_already_store_each_item() {
        var request = Request(
            w1: new Dictionary<string, int> { ["A"] = 1, ["B"] = 1, ["X"] = 3 },
            w2: new Dictionary<string, int> { ["Y"] = 2 });
        var state = ExecuteBothBarters(request);

        var instructions = WarehouseUnloadPlanner.BuildInstructions(request, state);

        Assert.Equal(2, instructions.Count);
        Assert.Equal([new RouteItemQuantity("X", 1)],
            instructions.Single(x => x.WarehouseId == "W1").Items);
        Assert.Equal([new RouteItemQuantity("Y", 1)],
            instructions.Single(x => x.WarehouseId == "W2").Items);
        Assert.True(instructions[^1].FinishRoute);
        Assert.False(instructions[0].FinishRoute);
    }

    [Fact]
    public void Weighted_output_with_no_existing_home_uses_nearest_warehouse() {
        var request = Request(
            w1: new Dictionary<string, int> { ["A"] = 1, ["B"] = 1 },
            w2: new Dictionary<string, int>());
        var state = ExecuteBothBarters(request);

        var instructions = WarehouseUnloadPlanner.BuildInstructions(request, state);

        var only = Assert.Single(instructions);
        Assert.Equal("W2", only.WarehouseId);
        Assert.Equal([new RouteItemQuantity("X", 1), new RouteItemQuantity("Y", 1)], only.Items);
        Assert.True(only.FinishRoute);
    }

    private static AutomaticRoutePlanningRequest Request(
        IReadOnlyDictionary<string, int> w1,
        IReadOnlyDictionary<string, int> w2) {
        var items = new Dictionary<string, RouteItem> {
            ["A"] = new("A", "A", 1, 100),
            ["B"] = new("B", "B", 1, 100),
            ["X"] = new("X", "X", 2, 100),
            ["Y"] = new("Y", "Y", 2, 100),
        };
        return new AutomaticRoutePlanningRequest(
            [
                new RouteBarterTask("x", "Trade1", new RoutePoint(40, 0), "A", 1, "X", 1),
                new RouteBarterTask("y", "Trade2", new RoutePoint(90, 0), "B", 1, "Y", 1),
            ],
            items,
            [
                new RouteWarehouse("W1", "W1", new RoutePoint(0, 0), w1),
                new RouteWarehouse("W2", "W2", new RoutePoint(100, 0), w2),
            ],
            0, 10_000, new RouteSearchLimits(1_000, 10), "unload-test");
    }

    private static RouteSimulationState ExecuteBothBarters(AutomaticRoutePlanningRequest request) {
        var state = RouteSimulationState.CreateInitial(request);
        var pickup = RouteStateTransition.TryPickup(request, state, "W1", [new("A", 1), new("B", 1)]);
        Assert.True(pickup.Success);
        var first = RouteStateTransition.TryBarter(request, pickup.State, 0);
        Assert.True(first.Success);
        var second = RouteStateTransition.TryBarter(request, first.State, 1);
        Assert.True(second.Success);
        return second.State;
    }
}
