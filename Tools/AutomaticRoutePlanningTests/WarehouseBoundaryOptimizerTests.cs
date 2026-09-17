using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public sealed class WarehouseBoundaryOptimizerTests {
    [Fact]
    public void Fresh_publication_reuses_first_visit_capacity_to_merge_later_route() {
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["S"] = new("S", "Supreme Coconut Syrup", 6, 1_600),
            ["F"] = new("F", "Balenosian Sailor Telescope", 7, 1_600),
            ["X"] = new("X", "Route 1 Input", 4, 1_100),
            ["Y"] = new("Y", "Route 1 Output", 5, 550),
            ["Q"] = new("Q", "Route 2 Input", 4, 1_400),
            ["R"] = new("R", "Route 2 Output", 5, 700),
        };
        RouteWarehouse[] warehouses = [
            new(
                "Velia",
                "Velia",
                new RoutePoint(-100, 0),
                new Dictionary<string, int>(StringComparer.Ordinal) {
                    ["S"] = 5,
                }),
            new(
                "Iliya",
                "Iliya",
                new RoutePoint(0, 0),
                new Dictionary<string, int>(StringComparer.Ordinal) {
                    ["X"] = 10,
                    ["Q"] = 10,
                }),
        ];
        RouteBarterTask[] tasks = [
            new("finish-s", "Iliya", new RoutePoint(0, 0), "S", 5, "F", 5),
            new("x-y", "Tigris", new RoutePoint(100, 0), "X", 10, "Y", 20),
            new("q-r", "Almai", new RoutePoint(200, 0), "Q", 10, "R", 20),
        ];
        var request = new AutomaticRoutePlanningRequest(
            tasks,
            items,
            warehouses,
            0,
            30_000,
            new RouteSearchLimits(100_000, 1_000),
            "warehouse-first-visit-merge-v1");
        RouteSimulationState state = RouteSimulationState.CreateInitial(request);
        RouteTransitionResult veliaPickup = RouteStateTransition.TryPickup(
            request, state, "Velia", [new("S", 5)]);
        Assert.True(veliaPickup.Success, veliaPickup.Diagnostic?.Code);
        RouteTransitionResult firstIliyaPickup = RouteStateTransition.TryPickup(
            request, veliaPickup.State, "Iliya", [new("X", 10)]);
        Assert.True(firstIliyaPickup.Success, firstIliyaPickup.Diagnostic?.Code);
        RouteTransitionResult xBarter =
            RouteStateTransition.TryBarter(request, firstIliyaPickup.State, 1);
        Assert.True(xBarter.Success, xBarter.Diagnostic?.Code);
        RouteTransitionResult terminal =
            RouteStateTransition.TryBarter(request, xBarter.State, 0);
        Assert.True(terminal.Success, terminal.Diagnostic?.Code);
        RouteTransitionResult firstUnload =
            RouteStateTransition.TryUnload(request, terminal.State, "Iliya");
        Assert.True(firstUnload.Success, firstUnload.Diagnostic?.Code);
        RouteTransitionResult secondIliyaPickup = RouteStateTransition.TryPickup(
            request, firstUnload.State, "Iliya", [new("Q", 10)]);
        Assert.True(secondIliyaPickup.Success, secondIliyaPickup.Diagnostic?.Code);
        RouteTransitionResult qBarter =
            RouteStateTransition.TryBarter(request, secondIliyaPickup.State, 2);
        Assert.True(qBarter.Success, qBarter.Diagnostic?.Code);
        RouteTransitionResult secondUnload =
            RouteStateTransition.TryUnload(request, qBarter.State, "Iliya");
        Assert.True(secondUnload.Success, secondUnload.Diagnostic?.Code);
        RoutePlan original = RoutePlanFactory.FromState(
            request,
            secondUnload.State,
            RoutePlanStatus.BestKnownWithinLimit,
            []);
        Assert.Equal(2, original.Routes.Count);

        RoutePlanPublicationResult published =
            RoutePlanPublication.PreparePlanForPublication(
                request,
                original,
                RoutePlanPublicationSource.FreshGeneration);

        Assert.True(
            published.Success,
            published.Failure is null
                ? "publication failed without diagnostic"
                : $"{published.Failure.Code}: {published.Failure.Detail}");
        RoutePlan optimized = Assert.IsType<RoutePlan>(published.Plan);
        PlannedRoute route = Assert.Single(optimized.Routes);
        Assert.Equal(
            [
                "pickup:Velia",
                "barter:finish-s",
                "unload:Iliya:F=5",
                "pickup:Iliya",
                "barter:x-y",
                "barter:q-r",
                "unload:Iliya:R=20,Y=20",
            ],
            route.Steps.Select(DescribeStep));
        Assert.Equal(25_000, route.PeakLT);
        Assert.True(
            route.Distance < original.Objective?.TotalDistance);
        Assert.True(RoutePlanVerifier.Verify(request, optimized).Success);
    }

    [Fact]
    public void Fresh_publication_finishes_terminal_barter_at_first_warehouse_visit() {
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["S"] = new("S", "Supreme Coconut Syrup", 6, 1_600),
            ["F"] = new("F", "Balenosian Sailor Telescope", 7, 1_600),
            ["X"] = new("X", "Unrelated Input", 4, 1_100),
            ["Y"] = new("Y", "Unrelated Output", 5, 1_100),
        };
        RouteWarehouse[] warehouses = [
            new(
                "Velia",
                "Velia",
                new RoutePoint(-100, 0),
                new Dictionary<string, int>(StringComparer.Ordinal) {
                    ["S"] = 5,
                }),
            new(
                "Iliya",
                "Iliya",
                new RoutePoint(0, 0),
                new Dictionary<string, int>(StringComparer.Ordinal) {
                    ["X"] = 10,
                }),
        ];
        RouteBarterTask[] tasks = [
            new("finish-s", "Iliya", new RoutePoint(0, 0), "S", 5, "F", 5),
            new("x-y", "Tigris", new RoutePoint(100, 0), "X", 10, "Y", 20),
        ];
        var request = new AutomaticRoutePlanningRequest(
            tasks,
            items,
            warehouses,
            0,
            30_000,
            new RouteSearchLimits(100_000, 1_000),
            "warehouse-first-visit-v1");
        RouteSimulationState state = RouteSimulationState.CreateInitial(request);
        RouteTransitionResult veliaPickup = RouteStateTransition.TryPickup(
            request, state, "Velia", [new("S", 5)]);
        Assert.True(veliaPickup.Success, veliaPickup.Diagnostic?.Code);
        RouteTransitionResult iliyaPickup = RouteStateTransition.TryPickup(
            request, veliaPickup.State, "Iliya", [new("X", 10)]);
        Assert.True(iliyaPickup.Success, iliyaPickup.Diagnostic?.Code);
        RouteTransitionResult unrelated =
            RouteStateTransition.TryBarter(request, iliyaPickup.State, 1);
        Assert.True(unrelated.Success, unrelated.Diagnostic?.Code);
        RouteTransitionResult terminal =
            RouteStateTransition.TryBarter(request, unrelated.State, 0);
        Assert.True(terminal.Success, terminal.Diagnostic?.Code);
        RouteTransitionResult unload =
            RouteStateTransition.TryUnload(request, terminal.State, "Iliya");
        Assert.True(unload.Success, unload.Diagnostic?.Code);
        RoutePlan original = RoutePlanFactory.FromState(
            request,
            unload.State,
            RoutePlanStatus.BestKnownWithinLimit,
            []);
        Assert.Equal(30_000, original.Routes[0].PeakLT);

        RoutePlanPublicationResult published =
            RoutePlanPublication.PreparePlanForPublication(
                request,
                original,
                RoutePlanPublicationSource.FreshGeneration);

        Assert.True(
            published.Success,
            published.Failure is null
                ? "publication failed without diagnostic"
                : $"{published.Failure.Code}: {published.Failure.Detail}");
        RoutePlan optimized = Assert.IsType<RoutePlan>(published.Plan);
        PlannedRoute route = Assert.Single(optimized.Routes);
        Assert.Equal(22_000, route.PeakLT);
        Assert.Equal(original.Routes[0].Distance, route.Distance);
        Assert.Equal(
            [
                "pickup:Velia",
                "barter:finish-s",
                "unload:Iliya:F=5",
                "pickup:Iliya",
                "barter:x-y",
                "unload:Iliya:Y=20",
            ],
            route.Steps.Select(DescribeStep));
        Assert.True(RoutePlanVerifier.Verify(request, optimized).Success);

        RoutePlanPublicationResult repeated =
            RoutePlanPublication.PreparePlanForPublication(
                request,
                optimized,
                RoutePlanPublicationSource.FreshGeneration);
        Assert.True(repeated.Success);
        Assert.False(repeated.Changed);
        Assert.Equal(
            optimized.Objective,
            Assert.IsType<RoutePlan>(repeated.Plan).Objective);
    }

    [Fact]
    public void Fresh_publication_unloads_terminal_reward_before_unrelated_later_barter() {
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["S"] = new("S", "Supreme Coconut Syrup", 6, 1_600),
            ["F"] = new("F", "Final Level 7", 7, 1_600),
            ["X"] = new("X", "Unrelated Input", 4, 1_100),
            ["Y"] = new("Y", "Unrelated Output", 5, 1_100),
        };
        var warehouse = new RouteWarehouse(
            "W",
            "Iliya",
            new RoutePoint(0, 0),
            new Dictionary<string, int>(StringComparer.Ordinal) {
                ["S"] = 5,
                ["X"] = 10,
            });
        RouteBarterTask[] tasks = [
            new("finish-s", "Iliya", new RoutePoint(0, 0), "S", 5, "F", 5),
            new("x-y", "Tigris", new RoutePoint(100, 0), "X", 10, "Y", 20),
        ];
        var request = new AutomaticRoutePlanningRequest(
            tasks,
            items,
            [warehouse],
            0,
            30_000,
            new RouteSearchLimits(100_000, 1_000),
            "warehouse-terminal-unload-v1");
        RouteSimulationState state = RouteSimulationState.CreateInitial(request);
        state = ExecuteRoute(
            request,
            state,
            [new("S", 5), new("X", 10)],
            [0, 1]);
        RoutePlan original = RoutePlanFactory.FromState(
            request,
            state,
            RoutePlanStatus.BestKnownWithinLimit,
            []);
        Assert.Equal(30_000, original.Routes[0].PeakLT);

        RoutePlanPublicationResult published =
            RoutePlanPublication.PreparePlanForPublication(
                request,
                original,
                RoutePlanPublicationSource.FreshGeneration);

        Assert.True(
            published.Success,
            published.Failure is null
                ? "publication failed without diagnostic"
                : $"{published.Failure.Code}: {published.Failure.Detail}");
        RoutePlan optimized = Assert.IsType<RoutePlan>(published.Plan);
        PlannedRoute route = Assert.Single(optimized.Routes);
        Assert.Equal(22_000, route.PeakLT);
        WarehouseUnloadStep[] unloads =
            route.Steps.OfType<WarehouseUnloadStep>().ToArray();
        Assert.Equal(2, unloads.Length);
        Assert.Equal(
            [new RouteItemQuantity("F", 5)],
            unloads[0].Items);
        Assert.DoesNotContain(
            unloads[1].Items,
            item => item.ItemId == "F");
        Assert.Equal(
            ["finish-s", "x-y"],
            route.Steps.OfType<BarterStep>().Select(step => step.RowId));
        Assert.True(RoutePlanVerifier.Verify(request, optimized).Success);

        RoutePlanPublicationResult repeated =
            RoutePlanPublication.PreparePlanForPublication(
                request,
                optimized,
                RoutePlanPublicationSource.FreshGeneration);
        Assert.True(repeated.Success);
        Assert.False(repeated.Changed);
        Assert.Equal(
            optimized.Objective,
            Assert.IsType<RoutePlan>(repeated.Plan).Objective);
    }

    [Fact]
    public void Fresh_publication_finishes_same_warehouse_exchange_before_unload_and_reuses_freed_capacity() {
        var request = CreateRequest();
        RouteSimulationState state = RouteSimulationState.CreateInitial(request);

        state = ExecuteRoute(
            request,
            state,
            [new("A", 5), new("B", 10)],
            [0, 1]);
        state = ExecuteRoute(
            request,
            state,
            [new("S", 5), new("X", 10)],
            [2, 3]);
        state = ExecuteRoute(
            request,
            state,
            [new("Q", 10)],
            [4]);

        RoutePlan original = RoutePlanFactory.FromState(
            request,
            state,
            RoutePlanStatus.BestKnownWithinLimit,
            []);
        Assert.Equal(3, original.Routes.Count);
        Assert.Equal(19_000, original.Routes[1].InitialLT);

        RoutePlanPublicationResult published =
            RoutePlanPublication.PreparePlanForPublication(
                request,
                original,
                RoutePlanPublicationSource.FreshGeneration);

        Assert.True(
            published.Success,
            published.Failure is null
                ? "publication failed without diagnostic"
                : $"{published.Failure.Code}: {published.Failure.Detail}");
        RoutePlan optimized = Assert.IsType<RoutePlan>(published.Plan);
        Assert.Equal(2, optimized.Routes.Count);

        PlannedRoute finishingRoute = Assert.Single(
            optimized.Routes, route => route.Steps
                .OfType<BarterStep>()
                .Any(step => step.RowId == "finish-s"));
        Assert.Equal(
            ["produce-s", "produce-k", "finish-s"],
            finishingRoute.Steps.OfType<BarterStep>().Select(step => step.RowId));
        Assert.Contains(
            finishingRoute.Steps.OfType<WarehouseUnloadStep>().Single().Items,
            item => item.ItemId == "F" && item.Quantity == 5);
        Assert.DoesNotContain(
            optimized.Routes.SkipWhile(route => route.Number != finishingRoute.Number)
                .Skip(1)
                .SelectMany(route => route.Steps)
                .OfType<WarehousePickupStep>()
                .SelectMany(step => step.Items),
            item => item.ItemId == "S");

        PlannedRoute combinedRoute = Assert.Single(
            optimized.Routes, route => route.Number != finishingRoute.Number);
        Assert.Equal(
            new HashSet<string>(["x-y", "q-r"], StringComparer.Ordinal),
            combinedRoute.Steps
                .OfType<BarterStep>()
                .Select(step => step.RowId)
                .ToHashSet(StringComparer.Ordinal));
        WarehousePickupStep combinedPickup =
            Assert.Single(combinedRoute.Steps.OfType<WarehousePickupStep>());
        Assert.Equal(
            new HashSet<RouteItemQuantity>([new("Q", 10), new("X", 10)]),
            combinedPickup.Items.ToHashSet());
        Assert.Equal(18_000, combinedRoute.InitialLT);
        Assert.Equal(25_000, combinedRoute.PeakLT);
        Assert.True(RoutePlanVerifier.Verify(request, optimized).Success);

        RoutePlanPublicationResult repeated =
            RoutePlanPublication.PreparePlanForPublication(
                request,
                optimized,
                RoutePlanPublicationSource.FreshGeneration);
        Assert.True(repeated.Success);
        Assert.False(repeated.Changed);
        Assert.Equal(
            optimized.Objective,
            Assert.IsType<RoutePlan>(repeated.Plan).Objective);
    }

    private static AutomaticRoutePlanningRequest CreateRequest() {
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["A"] = new("A", "Producer A", 6, 2_000),
            ["B"] = new("B", "Producer B", 4, 1_000),
            ["S"] = new("S", "Syrup", 6, 2_000),
            ["K"] = new("K", "Keys", 4, 1_000),
            ["F"] = new("F", "Final Level 7", 7, 2_000),
            ["X"] = new("X", "Route 2 Input", 3, 900),
            ["Y"] = new("Y", "Route 2 Output", 4, 1_000),
            ["Q"] = new("Q", "Route 3 Input", 3, 900),
            ["R"] = new("R", "Route 3 Output", 4, 1_000),
        };
        var warehouse = new RouteWarehouse(
            "W",
            "W_ISLAND",
            new RoutePoint(0, 0),
            new Dictionary<string, int>(StringComparer.Ordinal) {
                ["A"] = 5,
                ["B"] = 10,
                ["X"] = 10,
                ["Q"] = 10,
            });
        RouteBarterTask[] tasks = [
            new("produce-s", "AREHAZA", new RoutePoint(100, 0), "A", 5, "S", 5),
            new("produce-k", "DATON", new RoutePoint(200, 0), "B", 10, "K", 20),
            new("finish-s", "W_ISLAND", new RoutePoint(0, 0), "S", 5, "F", 5),
            new("x-y", "X_ISLAND", new RoutePoint(20, 0), "X", 10, "Y", 15),
            new("q-r", "Q_ISLAND", new RoutePoint(30, 0), "Q", 10, "R", 10),
        ];
        return new AutomaticRoutePlanningRequest(
            tasks,
            items,
            [warehouse],
            0,
            30_000,
            new RouteSearchLimits(100_000, 1_000),
            "warehouse-boundary-v1");
    }

    private static RouteSimulationState ExecuteRoute(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        IReadOnlyList<RouteItemQuantity> pickupItems,
        IReadOnlyList<int> taskIndexes) {
        RouteTransitionResult pickup =
            RouteStateTransition.TryPickup(request, state, "W", pickupItems);
        Assert.True(pickup.Success, pickup.Diagnostic?.Code);
        state = pickup.State;
        foreach (int taskIndex in taskIndexes) {
            RouteTransitionResult barter =
                RouteStateTransition.TryBarter(request, state, taskIndex);
            Assert.True(barter.Success, barter.Diagnostic?.Code);
            state = barter.State;
        }

        RouteTransitionResult unload =
            RouteStateTransition.TryUnload(request, state, "W");
        Assert.True(unload.Success, unload.Diagnostic?.Code);
        return unload.State;
    }

    private static string DescribeStep(RouteStep step) => step switch {
        WarehousePickupStep pickup => $"pickup:{pickup.WarehouseId}",
        BarterStep barter => $"barter:{barter.RowId}",
        WarehouseUnloadStep unload =>
            $"unload:{unload.WarehouseId}:" +
            string.Join(",", unload.Items.Select(item =>
                $"{item.ItemId}={item.Quantity}")),
        _ => step.GetType().Name,
    };
}
