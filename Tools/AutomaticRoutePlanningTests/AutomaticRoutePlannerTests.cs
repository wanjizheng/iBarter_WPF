using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public sealed class AutomaticRoutePlannerTests {
    [Fact]
    public void Complete_search_returns_verified_optimal_plan() {
        var request = RouteTestData.SingleTask();
        var plan = new AutomaticRoutePlanner().Plan(request, TestContext.Current.CancellationToken);

        Assert.Equal(RoutePlanStatus.Optimal, plan.Status);
        Assert.Single(plan.Routes);
        Assert.True(RoutePlanVerifier.Verify(request, plan).Success);
        Assert.Equal(1, plan.Objective?.RouteCount);
    }

    [Fact]
    public void Cross_warehouse_pickups_are_kept_in_one_route_when_feasible() {
        var request = TwoWarehouseRequest(new RouteSearchLimits(100_000, 100));
        var plan = new AutomaticRoutePlanner().Plan(request, TestContext.Current.CancellationToken);

        Assert.Equal(RoutePlanStatus.Optimal, plan.Status);
        var route = Assert.Single(plan.Routes);
        Assert.Equal(2, route.Steps.OfType<WarehousePickupStep>().Count());
        Assert.True(RoutePlanVerifier.Verify(request, plan).Success);
    }

    [Fact]
    public void Produced_inventory_can_be_unloaded_then_used_by_a_later_route() {
        var items = new Dictionary<string, RouteItem> {
            ["A"] = new("A", "A", 1, 100),
            ["B"] = new("B", "B", 1, 100),
            ["C"] = new("C", "C", 5, 1_000),
        };
        var request = new AutomaticRoutePlanningRequest(
            [
                new("producer", "P", new RoutePoint(1, 0), "A", 1, "B", 2),
                new("consumer", "C", new RoutePoint(2, 0), "B", 1, "C", 1),
            ], items,
            [new RouteWarehouse("W", "W", new RoutePoint(0, 0), new Dictionary<string, int> { ["A"] = 1 })],
            0, 1_050, new RouteSearchLimits(100_000, 100), "carry-over");

        var plan = new AutomaticRoutePlanner().Plan(request, TestContext.Current.CancellationToken);

        Assert.Equal(RoutePlanStatus.Optimal, plan.Status);
        Assert.Equal(2, plan.Routes.Count);
        Assert.Equal("producer", plan.Routes[0].Steps.OfType<BarterStep>().Single().RowId);
        Assert.Equal("consumer", plan.Routes[1].Steps.OfType<BarterStep>().Single().RowId);
        Assert.True(RoutePlanVerifier.Verify(request, plan).Success);
    }

    [Fact]
    public void Search_limit_never_claims_optimality_when_incumbent_exists() {
        var request = TwoWarehouseRequest(new RouteSearchLimits(1, 0));
        var plan = new AutomaticRoutePlanner().Plan(request, TestContext.Current.CancellationToken);
        Assert.Equal(RoutePlanStatus.BestKnownWithinLimit, plan.Status);
        Assert.NotEmpty(plan.Routes);
        Assert.True(RoutePlanVerifier.Verify(request, plan).Success);
    }

    [Fact]
    public void Impossible_indivisible_task_is_infeasible() {
        var request = RouteTestData.SingleTask(outputLevel: 7, outputQuantity: 1, totalLT: 1_500);
        var plan = new AutomaticRoutePlanner().Plan(request, TestContext.Current.CancellationToken);
        Assert.Equal(RoutePlanStatus.Infeasible, plan.Status);
        Assert.Empty(plan.Routes);
    }

    [Fact]
    public void Repeated_runs_are_deterministic() {
        var request = TwoWarehouseRequest(new RouteSearchLimits(100_000, 100));
        var planner = new AutomaticRoutePlanner();
        string expected = Signature(planner.Plan(request, TestContext.Current.CancellationToken));
        for (int i = 0; i < 20; i++)
            Assert.Equal(expected, Signature(planner.Plan(request, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public void Exact_search_matches_bruteforce_pair_grouping_distance() {
        var items = new Dictionary<string, RouteItem> {
            ["A"] = new("A", "A", 1, 100),
            ["B"] = new("B", "B", 1, 100),
            ["C"] = new("C", "C", 1, 100),
            ["D"] = new("D", "D", 1, 100),
            ["O"] = new("O", "O", 1, 0),
        };
        double[] positions = [1, 2, 100, 101];
        string[] ids = ["A", "B", "C", "D"];
        var request = new AutomaticRoutePlanningRequest(
            Enumerable.Range(0, 4).Select(i => new RouteBarterTask(
                $"r{ids[i]}", ids[i], new RoutePoint(positions[i], 0), ids[i], 1, "O", 1)).ToArray(),
            items,
            [new RouteWarehouse("W", "W", new RoutePoint(0, 0),
                ids.ToDictionary(x => x, _ => 1, StringComparer.Ordinal))],
            0, 200, new RouteSearchLimits(500_000, 100), "bruteforce-pairs");

        var plan = new AutomaticRoutePlanner().Plan(request, TestContext.Current.CancellationToken);
        double bruteForce = Permutations(Enumerable.Range(0, 4).ToArray())
            .Select(order => PairRouteDistance(order, positions)).Min();

        Assert.Equal(RoutePlanStatus.Optimal, plan.Status);
        Assert.Equal(2, plan.Routes.Count);
        Assert.Equal(bruteForce, plan.Objective!.Value.TotalDistance, 10);
        Assert.Equal(206, bruteForce, 10);
    }

    [Fact]
    public void Cancellation_returns_cancelled_status() {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var plan = new AutomaticRoutePlanner().Plan(RouteTestData.SingleTask(), cts.Token);
        Assert.Equal(RoutePlanStatus.Cancelled, plan.Status);
    }

    private static AutomaticRoutePlanningRequest TwoWarehouseRequest(RouteSearchLimits limits) {
        var items = new Dictionary<string, RouteItem> {
            ["L1"] = new("L1", "L1", 1, 100),
            ["L6"] = new("L6", "L6", 6, 2_000),
            ["O1"] = new("O1", "O1", 1, 100),
            ["O2"] = new("O2", "O2", 1, 100),
        };
        return new AutomaticRoutePlanningRequest(
            [
                new("r1", "A", new RoutePoint(2, 0), "L1", 1, "O1", 1),
                new("r2", "B", new RoutePoint(8, 0), "L6", 1, "O2", 1),
            ], items,
            [
                new RouteWarehouse("Iliya", "Iliya", new RoutePoint(0, 0), new Dictionary<string, int> { ["L1"] = 5 }),
                new RouteWarehouse("Velia", "Velia", new RoutePoint(5, 0), new Dictionary<string, int> { ["L6"] = 5 }),
            ], 0, 3_000, limits, "two-warehouse");
    }

    private static string Signature(RoutePlan plan) => string.Join("|", plan.Routes.SelectMany(route =>
        route.Steps.Select(step => step switch {
            WarehousePickupStep pickup => $"P:{pickup.WarehouseId}:{string.Join(',', pickup.Items)}",
            BarterStep barter => $"B:{barter.RowId}",
            WarehouseUnloadStep unload => $"U:{unload.WarehouseId}",
            _ => "?",
        })));

    private static double PairRouteDistance(int[] order, double[] positions) =>
        positions[order[0]] + Math.Abs(positions[order[0]] - positions[order[1]]) + positions[order[1]] +
        positions[order[2]] + Math.Abs(positions[order[2]] - positions[order[3]]) + positions[order[3]];

    private static IEnumerable<int[]> Permutations(int[] values) {
        if (values.Length == 0) {
            yield return [];
            yield break;
        }
        for (int i = 0; i < values.Length; i++) {
            int head = values[i];
            foreach (var tail in Permutations(values.Where((_, index) => index != i).ToArray()))
                yield return new[] { head }.Concat(tail).ToArray();
        }
    }
}
