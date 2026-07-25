using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public sealed class WarehouseBoundaryOptimizerTests {
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
}
