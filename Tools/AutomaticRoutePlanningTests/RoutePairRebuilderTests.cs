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

    [Fact]
    public void Pair_rebuild_repartitions_two_full_routes_to_group_distant_stops() {
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["I0"] = new("I0", "I0", 1, 100),
            ["I1"] = new("I1", "I1", 1, 100),
            ["I2"] = new("I2", "I2", 1, 100),
            ["I3"] = new("I3", "I3", 1, 100),
            ["REWARD"] = new("REWARD", "Reward", -1, 0),
        };
        var request = new AutomaticRoutePlanningRequest(
            [
                new("near-1", "Near1", new RoutePoint(1, 0), "I0", 1, "REWARD", 1),
                new("near-2", "Near2", new RoutePoint(2, 0), "I1", 1, "REWARD", 1),
                new("far-1", "Far1", new RoutePoint(100, 0), "I2", 1, "REWARD", 1),
                new("far-2", "Far2", new RoutePoint(101, 0), "I3", 1, "REWARD", 1),
            ],
            items,
            [new RouteWarehouse("W", "W", new RoutePoint(0, 0),
                new Dictionary<string, int> { ["I0"] = 1, ["I1"] = 1, ["I2"] = 1, ["I3"] = 1 })],
            0, 200, new RouteSearchLimits(10_000, 100), "pair-repartition");
        var state = RouteSimulationState.CreateInitial(request);
        state = ExecuteRoute(request, state, [0, 2]);
        state = ExecuteRoute(request, state, [1, 3]);
        double crossedDistance = state.TotalDistance;

        var improved = RoutePairRebuilder.Improve(request, state, CancellationToken.None);
        var plan = RoutePlanFactory.FromState(request, improved,
            RoutePlanStatus.BestKnownWithinLimit, []);

        Assert.Equal(2, plan.Routes.Count);
        Assert.True(plan.Objective!.Value.TotalDistance < crossedDistance * 0.7,
            $"before={crossedDistance}, after={plan.Objective.Value.TotalDistance}");
        Assert.Contains(plan.Routes, route => route.Steps.OfType<BarterStep>()
            .Select(step => step.RowId).ToHashSet(StringComparer.Ordinal)
            .SetEquals(["far-1", "far-2"]));
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
