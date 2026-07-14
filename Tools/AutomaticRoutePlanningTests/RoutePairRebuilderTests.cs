using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public sealed class RoutePairRebuilderTests {
    [Fact]
    public void Pair_rebuild_merges_two_routes_when_the_combined_cargo_fits() {
        var items = Enumerable.Range(0, 4).ToDictionary(
            i => $"I{i}", i => new RouteItem($"I{i}", $"I{i}", 1, 100));
        items["REWARD"] = new RouteItem("REWARD", "Reward", -1, 0);
        var request = new AutomaticRoutePlanningRequest(
            Enumerable.Range(0, 4).Select(i => new RouteBarterTask(
                $"r{i}", $"P{i}", new RoutePoint(i + 1, 0), $"I{i}", 1, "REWARD", 1)).ToArray(),
            items,
            [new RouteWarehouse("W", "W", new RoutePoint(0, 0),
                Enumerable.Range(0, 4).ToDictionary(i => $"I{i}", _ => 1))],
            0, 400, new RouteSearchLimits(10_000, 100), "pair-rebuild");
        var state = RouteSimulationState.CreateInitial(request);
        state = ExecuteRoute(request, state, [0, 1]);
        state = ExecuteRoute(request, state, [2, 3]);
        Assert.Equal(2, state.FinishedRoutes.Count);

        var improved = RoutePairRebuilder.Improve(request, state, CancellationToken.None);
        var plan = RoutePlanFactory.FromState(request, improved, RoutePlanStatus.BestKnownWithinLimit, []);

        Assert.Single(plan.Routes);
        Assert.Equal(4, plan.Routes[0].Steps.OfType<BarterStep>().Count());
        Assert.True(RoutePlanVerifier.Verify(request, plan).Success);
    }

    private static RouteSimulationState ExecuteRoute(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        int[] taskIndexes) {
        var pickup = RouteStateTransition.TryPickup(request, state, "W",
            taskIndexes.Select(i => new RouteItemQuantity($"I{i}", 1)).ToArray());
        Assert.True(pickup.Success);
        state = pickup.State;
        foreach (int index in taskIndexes) {
            var barter = RouteStateTransition.TryBarter(request, state, index);
            Assert.True(barter.Success);
            state = barter.State;
        }
        var unload = WarehouseUnloadPlanner.TryCompleteRoute(request, state);
        Assert.True(unload.Success);
        return unload.State;
    }
}
