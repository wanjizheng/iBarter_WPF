using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public sealed class AutomaticRouteHeuristicTests {
    [Fact]
    public void Builds_one_route_with_pickups_from_iliya_and_velia() {
        var items = new Dictionary<string, RouteItem> {
            ["L1"] = new("L1", "L1", 1, 100),
            ["L6"] = new("L6", "L6", 6, 2_000),
            ["O1"] = new("O1", "O1", 1, 100),
            ["O2"] = new("O2", "O2", 1, 100),
        };
        var request = new AutomaticRoutePlanningRequest(
            [
                new("r1", "A", new RoutePoint(2, 0), "L1", 1, "O1", 1),
                new("r2", "B", new RoutePoint(8, 0), "L6", 1, "O2", 1),
            ], items,
            [
                new RouteWarehouse("Iliya", "Iliya", new RoutePoint(0, 0), new Dictionary<string, int> { ["L1"] = 5 }),
                new RouteWarehouse("Velia", "Velia", new RoutePoint(5, 0), new Dictionary<string, int> { ["L6"] = 5 }),
            ],
            0, 3_000, new RouteSearchLimits(100_000, 100), "two-warehouse");
        var preflight = AutomaticRoutePreflight.Validate(request);

        var incumbent = AutomaticRouteHeuristic.TryBuildIncumbent(request, preflight, CancellationToken.None);

        Assert.NotNull(incumbent);
        var route = Assert.Single(incumbent.Plan.Routes);
        Assert.Equal(2, route.Steps.OfType<WarehousePickupStep>().Count());
        Assert.All(route.Steps, step => Assert.True(step.Load.TotalWithExtraLT <= request.TotalLT));
        Assert.Equal("Iliya", route.StartWarehouseId);
        Assert.Equal("Velia", route.EndWarehouseId);
    }

    [Fact]
    public void Returns_a_complete_feasible_plan_when_capacity_requires_multiple_routes() {
        var items = Enumerable.Range(0, 5).ToDictionary(
            i => $"I{i}", i => new RouteItem($"I{i}", $"I{i}", 1, 100), StringComparer.Ordinal);
        var tasks = Enumerable.Range(0, 4).Select(i =>
            new RouteBarterTask($"r{i}", $"P{i}", new RoutePoint(i < 2 ? i + 1 : i + 20, 0),
                $"I{i}", 1, "I4", 1)).ToArray();
        var request = new AutomaticRoutePlanningRequest(
            tasks, items,
            [new RouteWarehouse("W", "W", new RoutePoint(0, 0),
                Enumerable.Range(0, 4).ToDictionary(i => $"I{i}", _ => 1))],
            0, 250, new RouteSearchLimits(100_000, 100), "groups");

        var result = AutomaticRouteHeuristic.TryBuildIncumbent(
            request, AutomaticRoutePreflight.Validate(request), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(4, result.FinalState.FinishedRoutes.SelectMany(x => x.Steps).OfType<BarterStep>().Count());
        Assert.True(result.Plan.Routes.Count >= 2);
        Assert.All(result.Plan.Routes.SelectMany(x => x.Steps),
            step => Assert.True(step.Load.TotalWithExtraLT <= request.TotalLT));
    }

    [Fact]
    public void Local_moves_improve_a_nearest_neighbor_incumbent() {
        var items = Enumerable.Range(0, 4).ToDictionary(
            i => $"I{i}", i => new RouteItem($"I{i}", $"I{i}", 1, 100), StringComparer.Ordinal);
        items["O"] = new RouteItem("O", "O", 1, 0);
        var tasks = new[] {
            new RouteBarterTask("A", "A", new RoutePoint(2, 0), "I0", 1, "O", 1),
            new RouteBarterTask("B", "B", new RoutePoint(3, 0), "I1", 1, "O", 1),
            new RouteBarterTask("C", "C", new RoutePoint(0, 2), "I2", 1, "O", 1),
            new RouteBarterTask("D", "D", new RoutePoint(0, 3), "I3", 1, "O", 1),
        };
        var stock = Enumerable.Range(0, 4).ToDictionary(i => $"I{i}", _ => 1, StringComparer.Ordinal);

        AutomaticRoutePlanningRequest Build(int moves) => new(
            tasks, items, [new RouteWarehouse("W", "W", new RoutePoint(0, 0), stock)],
            0, 1_000, new RouteSearchLimits(100_000, moves), "local-moves");
        var greedyRequest = Build(0);
        var improvedRequest = Build(100);

        var greedy = AutomaticRouteHeuristic.TryBuildIncumbent(
            greedyRequest, AutomaticRoutePreflight.Validate(greedyRequest), CancellationToken.None);
        var improved = AutomaticRouteHeuristic.TryBuildIncumbent(
            improvedRequest, AutomaticRoutePreflight.Validate(improvedRequest), CancellationToken.None);

        Assert.NotNull(greedy);
        Assert.NotNull(improved);
        Assert.True(improved.Plan.Objective!.Value.TotalDistance < greedy.Plan.Objective!.Value.TotalDistance);
        Assert.True(RoutePlanVerifier.Verify(improvedRequest, improved.Plan).Success);
    }
}
