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

    [Fact]
    public void Heuristic_finishes_a_route_with_item_aware_multi_warehouse_unloads() {
        var items = new Dictionary<string, RouteItem> {
            ["A"] = new("A", "A", 1, 100), ["B"] = new("B", "B", 1, 100),
            ["X"] = new("X", "X", 2, 100), ["Y"] = new("Y", "Y", 2, 100),
        };
        var request = new AutomaticRoutePlanningRequest(
            [
                new("x", "T1", new RoutePoint(20, 0), "A", 1, "X", 1),
                new("y", "T2", new RoutePoint(30, 0), "B", 1, "Y", 1),
            ], items,
            [
                new RouteWarehouse("W1", "W1", new RoutePoint(0, 0),
                    new Dictionary<string, int> { ["A"] = 1, ["B"] = 1, ["X"] = 2 }),
                new RouteWarehouse("W2", "W2", new RoutePoint(40, 0),
                    new Dictionary<string, int> { ["Y"] = 2 }),
            ],
            0, 10_000, new RouteSearchLimits(10_000, 10), "split-unload");

        var result = AutomaticRouteHeuristic.TryBuildIncumbent(
            request, AutomaticRoutePreflight.Validate(request), CancellationToken.None);

        Assert.NotNull(result);
        var unloads = Assert.Single(result.Plan.Routes).Steps.OfType<WarehouseUnloadStep>().ToArray();
        Assert.Equal(2, unloads.Length);
        Assert.Equal([new RouteItemQuantity("X", 1)], unloads.Single(x => x.WarehouseId == "W1").Items);
        Assert.Equal([new RouteItemQuantity("Y", 1)], unloads.Single(x => x.WarehouseId == "W2").Items);
        Assert.True(RoutePlanVerifier.Verify(request, result.Plan).Success);
    }

    [Fact]
    public void Confirmed_right_region_five_task_load_can_run_as_one_route_when_crow_tasks_free_weight() {
        var items = new Dictionary<string, RouteItem> {
            ["C1"] = new("C1", "C1", 4, 1_000), ["C2"] = new("C2", "C2", 4, 1_000),
            ["C3"] = new("C3", "C3", 4, 1_000), ["U1"] = new("U1", "U1", 5, 1_000),
            ["U2"] = new("U2", "U2", 5, 1_000), ["O1"] = new("O1", "O1", 5, 1_000),
            ["O2"] = new("O2", "O2", 5, 1_000), ["10"] = new("10", "Crow Coin", -1, 0),
        };
        var request = new AutomaticRoutePlanningRequest(
            [
                new("crow-halmad", "Halmad", new RoutePoint(558_999, 333_684), "C1", 3, "10", 100),
                new("crow-kashuma", "Kashuma", new RoutePoint(589_314, 372_650), "C2", 3, "10", 100),
                new("crow-derko", "Derko", new RoutePoint(843_205, 415_735), "C3", 3, "10", 100),
                new("upgrade-hakoven", "Hakoven", new RoutePoint(1_252_450, 547_567), "U1", 2, "O1", 10),
                new("upgrade-arehaza", "Arehaza", new RoutePoint(1_267_170, 177_948), "U2", 2, "O2", 10),
            ],
            items,
            [new RouteWarehouse("Iliya", "Iliya", new RoutePoint(360_000, 520_000),
                new Dictionary<string, int> {
                    ["C1"] = 3, ["C2"] = 3, ["C3"] = 3, ["U1"] = 2, ["U2"] = 2,
                })],
            2_411, 24_110, new RouteSearchLimits(100_000, 500), "right-five");

        var result = AutomaticRouteHeuristic.TryBuildIncumbent(
            request, AutomaticRoutePreflight.Validate(request), CancellationToken.None);

        Assert.NotNull(result);
        var route = Assert.Single(result.Plan.Routes);
        Assert.Equal(5, route.Steps.OfType<BarterStep>().Count());
        Assert.All(route.Steps, step => Assert.True(step.Load.TotalWithExtraLT <= request.TotalLT));
        Assert.True(RoutePlanVerifier.Verify(request, result.Plan).Success);
    }

    [Fact]
    public void Dependency_aware_route_reordering_inserts_arehaza_between_hakoven_and_lema() {
        var items = new Dictionary<string, RouteItem> {
            ["HakovenInput"] = new("HakovenInput", "HakovenInput", 5, 1_000),
            ["ArehazaInput"] = new("ArehazaInput", "ArehazaInput", 5, 1_000),
            ["HakovenOutput"] = new("HakovenOutput", "HakovenOutput", 5, 1_000),
            ["LemaOutput"] = new("LemaOutput", "LemaOutput", 5, 1_000),
            ["ArehazaOutput"] = new("ArehazaOutput", "ArehazaOutput", 5, 1_000),
            ["IliyaOutput"] = new("IliyaOutput", "IliyaOutput", 5, 1_000),
        };
        var request = new AutomaticRoutePlanningRequest(
            [
                new("hakoven", "Hakoven", new RoutePoint(1_252_450, 547_567),
                    "HakovenInput", 5, "HakovenOutput", 5),
                new("lema", "Lema", new RoutePoint(417_920, 735_338),
                    "HakovenOutput", 5, "LemaOutput", 5),
                new("arehaza", "Arehaza", new RoutePoint(1_267_170, 177_948),
                    "ArehazaInput", 5, "ArehazaOutput", 5),
                new("iliya", "Iliya", new RoutePoint(360_000, 520_000),
                    "ArehazaOutput", 5, "IliyaOutput", 5),
            ],
            items,
            [new RouteWarehouse("Iliya", "Iliya", new RoutePoint(360_000, 520_000),
                new Dictionary<string, int> {
                    ["HakovenInput"] = 5,
                    ["ArehazaInput"] = 5,
                })],
            2_411, 24_110, new RouteSearchLimits(100_000, 500), "right-region-order");
        var state = RouteSimulationState.CreateInitial(request);

        state = RouteStateTransition.TryPickup(request, state, "Iliya", [
            new RouteItemQuantity("HakovenInput", 5),
            new RouteItemQuantity("ArehazaInput", 5),
        ]).State;
        foreach (string rowId in new[] { "hakoven", "lema", "arehaza", "iliya" }) {
            int index = request.Tasks.ToList().FindIndex(task => task.RowId == rowId);
            var transition = RouteStateTransition.TryBarter(request, state, index);
            Assert.True(transition.Success, transition.Diagnostic?.Code);
            state = transition.State;
        }
        state = WarehouseUnloadPlanner.TryCompleteRoute(request, state).State;

        var improved = IntraRouteOrderOptimizer.Improve(request, state, CancellationToken.None);

        var rowIds = Assert.Single(improved.FinishedRoutes).Steps
            .OfType<BarterStep>().Select(step => step.RowId).ToArray();
        Assert.Equal(["hakoven", "arehaza", "lema", "iliya"], rowIds);
        Assert.True(improved.TotalDistance < state.TotalDistance * 0.7);
        var plan = RoutePlanFactory.FromState(
            request, improved, RoutePlanStatus.BestKnownWithinLimit, []);
        Assert.True(RoutePlanVerifier.Verify(request, plan).Success);
    }

    [Fact]
    public void Bounded_beam_search_finds_and_verifies_a_plan_above_the_exact_search_limit() {
        const int taskCount = 13;
        var items = Enumerable.Range(0, taskCount).ToDictionary(
            i => $"I{i}", i => new RouteItem($"I{i}", $"I{i}", 4, 100), StringComparer.Ordinal);
        items["Reward"] = new RouteItem("Reward", "Reward", 5, 0);
        var tasks = Enumerable.Range(0, taskCount).Select(i =>
            new RouteBarterTask($"r{i:D2}", $"P{i:D2}", new RoutePoint(i * 10 + 10, i % 3),
                $"I{i}", 1, "Reward", 1)).ToArray();
        var stock = Enumerable.Range(0, taskCount).ToDictionary(
            i => $"I{i}", _ => 1, StringComparer.Ordinal);
        var request = new AutomaticRoutePlanningRequest(
            tasks, items,
            [new RouteWarehouse("W", "W", new RoutePoint(0, 0), stock)],
            0, 500, new RouteSearchLimits(20_000, 100), "beam-large");

        var incumbent = AutomaticRouteBeamSearch.TryBuildIncumbent(
            request, AutomaticRoutePreflight.Validate(request), CancellationToken.None);

        Assert.NotNull(incumbent);
        Assert.Equal(taskCount, incumbent.Plan.Routes
            .SelectMany(route => route.Steps).OfType<BarterStep>().Count());
        Assert.True(RoutePlanVerifier.Verify(request, incumbent.Plan).Success);
    }

    [Fact]
    public void Large_planner_uses_existing_intermediate_stock_before_replenishing_it_later() {
        const int chainLength = 11;
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["CHAIN0"] = new("CHAIN0", "Chain 0", 1, 100),
            ["PRODUCER_INPUT"] = new("PRODUCER_INPUT", "Producer input", 1, 100),
            ["INTERMEDIATE"] = new("INTERMEDIATE", "Intermediate", 6, 100),
            ["AFTER_CONSUMER"] = new("AFTER_CONSUMER", "After consumer", 6, 100),
            ["REWARD"] = new("REWARD", "Reward", -1, 0),
        };
        for (int i = 1; i < chainLength; i++)
            items[$"CHAIN{i}"] = new($"CHAIN{i}", $"Chain {i}", 1, 100);

        var tasks = new List<RouteBarterTask>();
        for (int i = 0; i < chainLength; i++) {
            string output = i == chainLength - 1 ? "REWARD" : $"CHAIN{i + 1}";
            tasks.Add(new RouteBarterTask(
                $"chain-{i:D2}", $"CHAIN_ISLAND_{i:D2}", new RoutePoint(0, 100),
                $"CHAIN{i}", 1, output, 1));
        }
        tasks.Add(new RouteBarterTask(
            "producer", "PRODUCER", new RoutePoint(1_000, 0),
            "PRODUCER_INPUT", 1, "INTERMEDIATE", 1));
        tasks.Add(new RouteBarterTask(
            "consumer", "SANCTUARY", new RoutePoint(0, 100),
            "INTERMEDIATE", 1, "AFTER_CONSUMER", 1));
        tasks.Add(new RouteBarterTask(
            "after-consumer", "AFTER_CONSUMER_ISLAND", new RoutePoint(1_000, 100),
            "AFTER_CONSUMER", 1, "REWARD", 1));

        var request = new AutomaticRoutePlanningRequest(
            tasks,
            items,
            [new RouteWarehouse("Velia", "Velia", new RoutePoint(0, 0),
                new Dictionary<string, int>(StringComparer.Ordinal) {
                    ["CHAIN0"] = 1,
                    ["PRODUCER_INPUT"] = 1,
                    ["INTERMEDIATE"] = 1,
                })],
            0,
            10_000,
            new RouteSearchLimits(100_000, 2_000),
            "inventory-first-large");

        var planned = new AutomaticRoutePlanner().Plan(request, TestContext.Current.CancellationToken);
        var withoutStoredIntermediate = new AutomaticRoutePlanningRequest(
            tasks,
            items,
            [new RouteWarehouse("Velia", "Velia", new RoutePoint(0, 0),
                new Dictionary<string, int>(StringComparer.Ordinal) {
                    ["CHAIN0"] = 1,
                    ["PRODUCER_INPUT"] = 1,
                })],
            0,
            10_000,
            new RouteSearchLimits(100_000, 2_000),
            "inventory-first-large-no-stock");
        var noStockPlan = new AutomaticRoutePlanner().Plan(
            withoutStoredIntermediate, TestContext.Current.CancellationToken);

        Assert.NotNull(planned.Objective);
        Assert.NotNull(noStockPlan.Objective);
        Assert.True(planned.Objective.Value.TotalDistance < noStockPlan.Objective.Value.TotalDistance,
            $"stored={planned.Objective.Value.TotalDistance}, no-stock={noStockPlan.Objective.Value.TotalDistance}, status={planned.Status}");
        var route = Assert.Single(planned.Routes);
        var order = route.Steps.OfType<BarterStep>().Select(step => step.RowId).ToArray();
        Assert.True(Array.IndexOf(order, "consumer") < Array.IndexOf(order, "producer"));
        var finalUnload = route.Steps.OfType<WarehouseUnloadStep>().Last();
        Assert.Contains(finalUnload.Items,
            item => item.ItemId == "INTERMEDIATE" && item.Quantity == 1);
        Assert.True(RoutePlanVerifier.Verify(request, planned).Success);
    }

    [Fact]
    public void Long_route_relocation_moves_a_late_passed_island_into_the_nearby_sequence() {
        const int straightTaskCount = 18;
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["REWARD"] = new("REWARD", "Reward", -1, 0),
        };
        var stock = new Dictionary<string, int>(StringComparer.Ordinal);
        var tasks = new List<RouteBarterTask>();
        for (int i = 0; i < straightTaskCount; i++) {
            string id = $"INPUT{i:D2}";
            items[id] = new RouteItem(id, id, 1, 10);
            stock[id] = 1;
            tasks.Add(new RouteBarterTask(
                $"straight-{i:D2}", $"STRAIGHT_{i:D2}", new RoutePoint(i + 1, 0),
                id, 1, "REWARD", 1));
        }
        items["MISSED_INPUT"] = new("MISSED_INPUT", "Missed input", 1, 10);
        stock["MISSED_INPUT"] = 1;
        items["ANCHOR_INPUT"] = new("ANCHOR_INPUT", "Anchor input", 1, 10);
        stock["ANCHOR_INPUT"] = 1;
        tasks.Add(new RouteBarterTask(
            "anchor", "ANCHOR", new RoutePoint(18, 100),
            "ANCHOR_INPUT", 1, "REWARD", 1));
        tasks.Add(new RouteBarterTask(
            "missed", "MISSED", new RoutePoint(5, 1),
            "MISSED_INPUT", 1, "REWARD", 1));

        var request = new AutomaticRoutePlanningRequest(
            tasks,
            items,
            [new RouteWarehouse("W", "W", new RoutePoint(0, 0), stock)],
            0,
            10_000,
            new RouteSearchLimits(100_000, 2_000),
            "long-relocation");
        var state = RouteSimulationState.CreateInitial(request);
        var pickup = RouteStateTransition.TryPickup(
            request,
            state,
            "W",
            stock.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new RouteItemQuantity(pair.Key, pair.Value)).ToArray());
        Assert.True(pickup.Success);
        state = pickup.State;
        for (int i = 0; i < tasks.Count; i++) {
            var barter = RouteStateTransition.TryBarter(request, state, i);
            Assert.True(barter.Success, barter.Diagnostic?.Code);
            state = barter.State;
        }
        state = WarehouseUnloadPlanner.TryCompleteRoute(request, state).State;

        var improved = IntraRouteOrderOptimizer.Improve(
            request, state, TestContext.Current.CancellationToken);

        var improvedRoute = Assert.Single(improved.FinishedRoutes);
        var order = improvedRoute.Steps.OfType<BarterStep>().Select(step => step.RowId).ToArray();
        Assert.True(Array.IndexOf(order, "missed") < Array.IndexOf(order, "anchor"),
            string.Join(",", order));
        Assert.True(improved.TotalDistance < state.TotalDistance);
        var plan = RoutePlanFactory.FromState(
            request, improved, RoutePlanStatus.BestKnownWithinLimit, []);
        Assert.True(RoutePlanVerifier.Verify(request, plan).Success);
    }
}
