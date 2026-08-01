using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

/// <summary>
/// Bug 1 + Bug 3 tests at the level of the <see cref="RouteProgressFilter"/>
/// + <see cref="RouteCargoNormalizer"/> pipeline that the unified
/// <c>ApplyBarterCompletionProgress</c> method uses internally.
///
/// The Bug 1 behavior is: ticking a Planner CK must NOT clear the in-memory
/// route plan. We can't exercise <see cref="AutomaticRouteCoordinator"/>
/// directly here because it pulls in WPF (App domain), but we *can* exercise
/// the pure functions it composes: completed-step filtering, cargo
/// normalization, and the round trip through persistence.
/// </summary>
public class RouteProgressPipelineTests {
    private static readonly RouteLoadSnapshot ZeroLoad = new(0, 0, 0);

    private static WarehousePickupStep Pickup(string warehouseId, params (string id, int qty)[] items) =>
        new(warehouseId, warehouseId,
            items.Select(i => new RouteItemQuantity(i.id, i.qty)).ToArray(),
            ZeroLoad);

    private static WarehouseUnloadStep Unload(string warehouseId, params (string id, int qty)[] items) =>
        new(warehouseId, warehouseId,
            items.Select(i => new RouteItemQuantity(i.id, i.qty)).ToArray(),
            ZeroLoad);

    private static BarterStep Barter(string rowId, string fromId, int fromQty, string toId, int toQty) =>
        new(rowId, "Baremi",
            new RouteItemQuantity(fromId, fromQty),
            new RouteItemQuantity(toId, toQty),
            ZeroLoad);

    private static PlannedRoute BuildRoute(int number, params RouteStep[] steps) =>
        new(number, "Iliya", "Iliya", steps, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);

    [Fact]
    public void ExcludeCompletedBarters_RemovesMatchedRowIds() {
        var steps = new RouteStep[] {
            Pickup("Iliya", ("800049", 1)),
            Barter("a", "800049", 1, "10", 1),
            Barter("b", "10", 1, "11", 1),
        };
        var completed = new HashSet<string>(StringComparer.Ordinal) { "a" };

        var filtered = RouteProgressFilter.ExcludeCompletedBarters(steps, completed);

        Assert.Equal(2, filtered.Count);
        Assert.IsType<WarehousePickupStep>(filtered[0]);
        var barter = Assert.IsType<BarterStep>(filtered[1]);
        Assert.Equal("b", barter.RowId);
    }

    [Fact]
    public void ExcludeCompletedBarters_PreservesPickupAndUnload() {
        var steps = new RouteStep[] {
            Pickup("Iliya", ("800049", 1)),
            Barter("a", "800049", 1, "10", 1),
            Unload("Iliya", ("10", 1)),
        };
        var completed = new HashSet<string>(StringComparer.Ordinal) { "a" };

        var filtered = RouteProgressFilter.ExcludeCompletedBarters(steps, completed);

        // ExcludeCompletedBarters keeps pickup/unload verbatim and only
        // drops BarterSteps whose RowId is in the completed set.
        Assert.Equal(2, filtered.Count);
        Assert.IsType<WarehousePickupStep>(filtered[0]);
        Assert.IsType<WarehouseUnloadStep>(filtered[1]);
    }

    [Fact]
    public void RemainingMapSteps_SkipsCompletedPrefix() {
        // After completing a and b, the visible map trace should start at
        // the next non-completed barter (c) plus any trailing unload.
        var steps = new RouteStep[] {
            Pickup("Iliya", ("800049", 5)),
            Barter("a", "800049", 1, "10", 1),
            Barter("b", "10", 1, "11", 1),
            Barter("c", "11", 1, "12", 1),
            Unload("Iliya", ("12", 1)),
        };
        var completed = new HashSet<string>(StringComparer.Ordinal) { "a", "b" };

        var remaining = RouteProgressFilter.RemainingMapSteps(steps, completed);

        Assert.Equal(2, remaining.Count);
        var barter = Assert.IsType<BarterStep>(remaining[0]);
        Assert.Equal("c", barter.RowId);
        Assert.IsType<WarehouseUnloadStep>(remaining[1]);
    }

    [Fact]
    public void RemainingMapSteps_FirstBarterNotCompleted_KeepsEverything() {
        var steps = new RouteStep[] {
            Pickup("Iliya", ("800049", 1)),
            Barter("a", "800049", 1, "10", 1),
            Barter("b", "10", 1, "11", 1),
            Unload("Iliya", ("11", 1)),
        };
        var completed = new HashSet<string>(StringComparer.Ordinal);

        var remaining = RouteProgressFilter.RemainingMapSteps(steps, completed);

        Assert.Equal(4, remaining.Count);
    }

    [Fact]
    public void RemainingMapSteps_LastBarterCompleted_RemovesUnloadShell() {
        var steps = new RouteStep[] {
            Pickup("Iliya", ("800049", 1)),
            MakeBarterAt("only", "SameIsland", "800049", 1, "10", 1),
            new WarehouseUnloadStep(
                "Iliya", "SameIsland", [new RouteItemQuantity("10", 1)], ZeroLoad),
        };

        var remaining = RouteProgressFilter.RemainingMapSteps(
            steps,
            new HashSet<string>(StringComparer.Ordinal) { "only" });

        Assert.Empty(remaining);
    }

    [Fact]
    public void RemainingRoutes_RemovesCompletedRoute_WithoutRenumberingOthers() {
        var routes = new[] {
            BuildRoute(1, Barter("route-1", "800049", 1, "10", 1)),
            BuildRoute(2, Barter("route-2", "800049", 1, "11", 1)),
            BuildRoute(3, Barter("route-3", "800049", 1, "12", 1)),
        };

        var remaining = RouteProgressFilter.RemainingRoutes(
            routes,
            new HashSet<string>(StringComparer.Ordinal) { "route-2" });

        Assert.Equal([1, 3], remaining.Select(route => route.Number));
    }

    [Fact]
    public void FindVisibleBarterRowForSegment_OnlyRemainingBarters() {
        // 'a' is at Baremi; 'b' is at a separate island Baremi2.
        // When 'a' is completed, the visible map trace is [b, unload] and
        // 'b' is the only remaining barter. Its focus segment is the one
        // between 'b's island and the next step's island.
        var steps = new RouteStep[] {
            Pickup("Iliya", ("800049", 1)),
            MakeBarterAt("a", "Baremi", "800049", 1, "10", 1),
            MakeBarterAt("b", "Baremi2", "10", 1, "11", 1),
            Unload("Epheria", ("11", 1)),
        };
        var completed = new HashSet<string>(StringComparer.Ordinal) { "a" };

        // Visibility check: 'a' is excluded from the visible map trace.
        var visible = RouteProgressFilter.RemainingMapSteps(steps, completed);
        Assert.Equal(2, visible.Count);
        Assert.IsType<BarterStep>(visible[0]);
        Assert.IsType<WarehouseUnloadStep>(visible[1]);

        // 'b' is the only remaining barter; the segment between b's island
        // and the unload island (Epheria) must be findable.
        var segment = RouteProgressFilter.FindVisibleBarterSegment(
            steps, completed, "b");
        Assert.NotNull(segment);
        Assert.Equal("Baremi2", segment!.FromIslandId);
        Assert.Equal("Epheria", segment.ToIslandId);

        var rowId = RouteProgressFilter.FindVisibleBarterRowForSegment(
            steps, completed, "Baremi2", "Epheria");
        Assert.Equal("b", rowId);
    }

    [Fact]
    public void FindVisibleBarterRowForSegment_NoMatch_ReturnsNull() {
        var steps = new RouteStep[] {
            Pickup("Iliya", ("800049", 1)),
            MakeBarterAt("a", "Baremi", "800049", 1, "10", 1),
        };
        var completed = new HashSet<string>(StringComparer.Ordinal);

        // Asking for a segment that doesn't exist returns null rather than
        // silently matching the closest barter — this is the contract the
        // map focus code relies on.
        var rowId = RouteProgressFilter.FindVisibleBarterRowForSegment(
            steps, completed, "Nowhere", "Nowhere2");

        Assert.Null(rowId);
    }

    private static BarterStep MakeBarterAt(string rowId, string islandId,
        string fromId, int fromQty, string toId, int toQty) =>
        new(rowId, islandId,
            new RouteItemQuantity(fromId, fromQty),
            new RouteItemQuantity(toId, toQty),
            new RouteLoadSnapshot(0, 0, 0));

    [Fact]
    public void Pipeline_RemoveCompletedBarter_PreservesOtherBartersAndRoutes() {
        // Multi-route scenario: completing barter 'a' must leave 'b', 'c',
        // 'd' alone and keep all three routes.
        var route1 = BuildRoute(1,
            Pickup("Iliya", ("800049", 2)),
            Barter("a", "800049", 1, "10", 1),
            Barter("b", "800049", 1, "10", 1),
            Unload("Iliya", ("10", 2)));
        var route2 = BuildRoute(2,
            Pickup("Velia", ("800049", 1)),
            Barter("c", "800049", 1, "10", 1),
            Unload("Velia", ("10", 1)));
        var route3 = BuildRoute(3,
            Pickup("Epheria", ("800049", 1)),
            Barter("d", "800049", 1, "10", 1),
            Unload("Epheria", ("10", 1)));
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [route1, route2, route3],
            null, [], "fp");

        var completed = new HashSet<string>(StringComparer.Ordinal) { "a" };

        var newRoutes = new List<PlannedRoute>(plan.Routes.Count);
        foreach (var route in plan.Routes) {
            var remaining = RouteProgressFilter.ExcludeCompletedBarters(
                route.Steps, completed).ToArray();
            if (remaining.Length == 0) continue;
            if (!remaining.OfType<BarterStep>().Any()) continue;
            newRoutes.Add(new PlannedRoute(
                route.Number, route.StartWarehouseId, route.EndWarehouseId,
                remaining, route.Distance, route.InitialLT, route.CurrentLT, route.PeakLT));
        }

        // Bug 1 assertion: completing 'a' must NOT clear all routes.
        Assert.Equal(3, newRoutes.Count);
        // All other barters are intact.
        var allBarters = newRoutes.SelectMany(r => r.Steps.OfType<BarterStep>()).Select(s => s.RowId).ToArray();
        Assert.Contains("b", allBarters);
        Assert.Contains("c", allBarters);
        Assert.Contains("d", allBarters);
        Assert.DoesNotContain("a", allBarters);
    }

    [Fact]
    public void Pipeline_CompletingLastBarterInRoute_RemovesThatRouteOnly() {
        var route1 = BuildRoute(1,
            Pickup("Iliya", ("800049", 1)),
            Barter("a", "800049", 1, "10", 1),
            Unload("Iliya", ("10", 1)));
        var route2 = BuildRoute(2,
            Pickup("Velia", ("800049", 1)),
            Barter("b", "800049", 1, "10", 1),
            Unload("Velia", ("10", 1)));
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [route1, route2], null, [], "fp");

        var completed = new HashSet<string>(StringComparer.Ordinal) { "a" };

        var newRoutes = new List<PlannedRoute>();
        foreach (var route in plan.Routes) {
            var remaining = RouteProgressFilter.ExcludeCompletedBarters(
                route.Steps, completed).ToArray();
            if (remaining.Length == 0) continue;
            if (!remaining.OfType<BarterStep>().Any()) continue;
            newRoutes.Add(new PlannedRoute(
                route.Number, route.StartWarehouseId, route.EndWarehouseId,
                remaining, route.Distance, route.InitialLT, route.CurrentLT, route.PeakLT));
        }

        Assert.Single(newRoutes);
        Assert.Equal(2, newRoutes[0].Number);
    }

    [Fact]
    public void Pipeline_AllBartersCompleted_PlanIsEmpty() {
        var route1 = BuildRoute(1,
            Pickup("Iliya", ("800049", 1)),
            Barter("a", "800049", 1, "10", 1),
            Unload("Iliya", ("10", 1)));
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [route1], null, [], "fp");

        var completed = new HashSet<string>(StringComparer.Ordinal) { "a" };

        var newRoutes = new List<PlannedRoute>();
        foreach (var route in plan.Routes) {
            var remaining = RouteProgressFilter.ExcludeCompletedBarters(
                route.Steps, completed).ToArray();
            if (remaining.Length == 0) continue;
            if (!remaining.OfType<BarterStep>().Any()) continue;
            newRoutes.Add(new PlannedRoute(
                route.Number, route.StartWarehouseId, route.EndWarehouseId,
                remaining, route.Distance, route.InitialLT, route.CurrentLT, route.PeakLT));
        }

        // All routes consumed by completion → empty newRoutes. The pipeline
        // must NOT clear the saved snapshot in this case; it must keep
        // publishing an empty visible trace but preserve the historical
        // record on disk for restart.
        Assert.Empty(newRoutes);
    }

    [Fact]
    public void Pipeline_CargoNormalizationAlsoRuns_AfterProgressFilter() {
        // After pruning a completed barter, the original cargo demand was
        // over-supplied.  Pruning should also drop the now-unused pickup.
        var route = BuildRoute(1,
            Pickup("Iliya", ("800045", 1), ("800049", 1)),
            Barter("a", "800049", 1, "10", 1), // 800045 stays unused
            Unload("Iliya", ("800045", 1), ("10", 1)));
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [route], null, [], "fp");
        var request = TestRequestFactory.BuildFor(route);

        var normalized = RouteCargoNormalizer.Normalize(request, plan);
        Assert.True(normalized.Success);
        var pickup = (WarehousePickupStep)normalized.Plan!.Routes[0].Steps[0];
        Assert.DoesNotContain(pickup.Items, x => x.ItemId == "800045");
    }

    [Fact]
    public void Pipeline_ToggleCompletionTwiceIsIdempotent() {
        // Toggling a barter in and out of the completed set must produce
        // the same final plan as never toggling it. Because the legacy
        // incremental `displayedParley -= completedRow.Parley` patch would
        // drift under repeated toggles, this test guards the pure-derivation
        // contract that the unified pipeline relies on.
        var route = BuildRoute(1,
            Pickup("Iliya", ("800049", 1)),
            MakeBarterAt("a", "Baremi", "800049", 1, "10", 1),
            MakeBarterAt("b", "Baremi2", "10", 1, "11", 1),
            Unload("Epheria", ("11", 1)));
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [route], null, [], "fp");
        var request = TestRequestFactory.BuildFor(route);

        var first = ApplyPipeline(request, plan, new HashSet<string>(StringComparer.Ordinal));
        var toggled = ApplyPipeline(request, plan,
            new HashSet<string>(StringComparer.Ordinal) { "a" });
        var restored = ApplyPipeline(request, plan,
            new HashSet<string>(StringComparer.Ordinal));

        Assert.Equal(first.Routes.Count, restored.Routes.Count);
        var firstSteps = first.Routes[0].Steps.Select(s => s.GetType().Name).ToArray();
        var restoredSteps = restored.Routes[0].Steps.Select(s => s.GetType().Name).ToArray();
        Assert.Equal(firstSteps, restoredSteps);
        // Toggling must not change the visible step shape beyond removing
        // the completed barter.
        Assert.Equal(4, first.Routes[0].Steps.Count);
        Assert.Equal(3, toggled.Routes[0].Steps.Count);
    }

    [Fact]
    public void CompletionProgress_UsesPublishedWarehouseStock_WhenLiveSnapshotIsEmpty() {
        // Map completion needs the live task set (r1 is now done), but a
        // transient Storage UI refresh can report every warehouse item as
        // zero. Replaying the already published plan against that transient
        // stock must not reject the completion.
        var publishedRequest = RouteTestData.TwoItemRequest(false);
        var plan = new AutomaticRoutePlanner().Plan(
            publishedRequest, TestContext.Current.CancellationToken);
        Assert.Equal(RoutePlanStatus.Optimal, plan.Status);

        var emptyStockRequest = new AutomaticRoutePlanningRequest(
            publishedRequest.Tasks.Where(task => task.RowId != "r1").ToArray(),
            publishedRequest.Items,
            publishedRequest.Warehouses.Select(warehouse => new RouteWarehouse(
                warehouse.WarehouseId,
                warehouse.IslandId,
                warehouse.Point,
                warehouse.Inventory.Keys.ToDictionary(itemId => itemId, _ => 0,
                    StringComparer.Ordinal))).ToArray(),
            publishedRequest.ExtraLT,
            publishedRequest.TotalLT,
            publishedRequest.Limits,
            publishedRequest.ConfigurationVersion);
        var completed = new HashSet<string>(StringComparer.Ordinal) { "r1" };

        var transientFailure = RoutePlanPublication.PreparePlanForPublication(
            emptyStockRequest, plan, RoutePlanPublicationSource.CompletionProgress, completed);
        Assert.False(transientFailure.Success);
        Assert.Equal("insufficient-stock", transientFailure.Failure?.Code);

        var progressRequest = RouteProgressRequestBuilder.MergePublishedExecutionState(
            publishedRequest, emptyStockRequest);
        var prepared = RoutePlanPublication.PreparePlanForPublication(
            progressRequest, plan, RoutePlanPublicationSource.CompletionProgress, completed);

        Assert.True(prepared.Success,
            $"{prepared.Failure?.Code}: {prepared.Failure?.Detail}");
    }

    [Fact]
    public void CompletionProgress_CarriesCompletedRouteUnloadIntoNextRouteStock() {
        // Route 1 creates MID and unloads it into Velia. Route 2 then picks MID
        // up from Velia. Once route 1 is fully completed it disappears from the
        // visible projection, but its unload must remain part of route 2's
        // starting warehouse state.
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["SEED"] = new("SEED", "Seed", 1, 100),
            ["MID"] = new("MID", "Intermediate", 2, 100),
            ["OUT"] = new("OUT", "Output", 3, 100),
        };
        var originalRequest = new AutomaticRoutePlanningRequest(
            [
                new RouteBarterTask("r1", "A", new RoutePoint(10, 0),
                    "SEED", 10, "MID", 10),
                new RouteBarterTask("r2", "B", new RoutePoint(20, 0),
                    "MID", 10, "OUT", 10),
            ],
            items,
            [new RouteWarehouse("Velia", "Velia", new RoutePoint(0, 0),
                new Dictionary<string, int>(StringComparer.Ordinal) {
                    ["SEED"] = 10,
                    ["MID"] = 0,
                    ["OUT"] = 0,
                })],
            0,
            30_000,
            new RouteSearchLimits(100_000, 1_000),
            "completed-route-warehouse-handoff");
        var skeleton = new RoutePlan(
            RoutePlanStatus.Optimal,
            [
                BuildRoute(1,
                    Pickup("Velia", ("SEED", 10)),
                    Barter("r1", "SEED", 10, "MID", 10),
                    Unload("Velia", ("MID", 10))),
                BuildRoute(2,
                    Pickup("Velia", ("MID", 10)),
                    Barter("r2", "MID", 10, "OUT", 10),
                    Unload("Velia", ("OUT", 10))),
            ],
            null,
            [],
            RoutePlanFingerprint.Compute(originalRequest));
        var replayed = RouteReplay.ReplayPlan(originalRequest, skeleton);
        Assert.True(replayed.Success, replayed.Failure?.FailureDetail);

        var progressRequest = new AutomaticRoutePlanningRequest(
            originalRequest.Tasks.Where(task => task.RowId == "r2").ToArray(),
            originalRequest.Items,
            originalRequest.Warehouses,
            originalRequest.ExtraLT,
            originalRequest.TotalLT,
            originalRequest.Limits,
            originalRequest.ConfigurationVersion);
        var prepared = RoutePlanPublication.PreparePlanForPublication(
            progressRequest,
            replayed.Plan!,
            RoutePlanPublicationSource.CompletionProgress,
            new HashSet<string>(StringComparer.Ordinal) { "r1" });

        Assert.True(prepared.Success,
            $"{prepared.Failure?.Code}: {prepared.Failure?.Detail}");
        var remainingRoute = Assert.Single(prepared.Plan!.Routes);
        var pickup = Assert.IsType<WarehousePickupStep>(remainingRoute.Steps[0]);
        Assert.Contains(pickup.Items,
            item => item.ItemId == "MID" && item.Quantity == 10);
    }

    [Fact]
    public void CompletionProgress_AppliesCorrelatedCleanupWithoutBreakingWarehouseHandoff() {
        // Removing either obsolete A or B alone still leaves route 1
        // overweight after ACTIVE produces D. Both completed-prefix inputs
        // must be removed as one coherent route cleanup. Route 2's X looks
        // route-locally redundant, but it is deliberately unloaded into W2
        // for route 3 and therefore must survive conservative normalization.
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["A"] = new("A", "A", 1, 40),
            ["B"] = new("B", "B", 1, 40),
            ["C"] = new("C", "C", 1, 20),
            ["D"] = new("D", "D", 1, 70),
            ["ZA"] = new("ZA", "ZA", 0, 0),
            ["ZB"] = new("ZB", "ZB", 0, 0),
            ["X"] = new("X", "X", 1, 10),
            ["E"] = new("E", "E", 1, 10),
            ["F"] = new("F", "F", 1, 10),
            ["G"] = new("G", "G", 1, 10),
        };
        RouteBarterTask[] allTasks = [
            new("c1", "C1", new RoutePoint(0, 0), "A", 1, "ZA", 1),
            new("c2", "C2", new RoutePoint(0, 0), "B", 1, "ZB", 1),
            new("active", "ACTIVE", new RoutePoint(0, 0), "C", 1, "D", 1),
            new("active2", "ACTIVE2", new RoutePoint(0, 0), "E", 1, "F", 1),
            new("active3", "ACTIVE3", new RoutePoint(0, 0), "X", 1, "G", 1),
        ];
        RouteWarehouse[] warehouses = [
            new("W1", "W1", new RoutePoint(0, 0),
                new Dictionary<string, int>(StringComparer.Ordinal) {
                    ["A"] = 1,
                    ["B"] = 1,
                    ["C"] = 1,
                    ["X"] = 1,
                    ["E"] = 1,
                }),
            new("W2", "W2", new RoutePoint(0, 0),
                new Dictionary<string, int>(StringComparer.Ordinal) {
                    ["X"] = 0,
                }),
        ];
        var originalRequest = new AutomaticRoutePlanningRequest(
            allTasks,
            items,
            warehouses,
            0,
            100,
            new RouteSearchLimits(100_000, 1_000),
            "correlated-progress-cleanup");
        var skeleton = new RoutePlan(
            RoutePlanStatus.Optimal,
            [
                new PlannedRoute(
                    1, "W1", "W1",
                    [
                        Pickup("W1", ("A", 1), ("B", 1), ("C", 1)),
                        new BarterStep("c1", "C1", new("A", 1), new("ZA", 1), ZeroLoad),
                        new BarterStep("c2", "C2", new("B", 1), new("ZB", 1), ZeroLoad),
                        new BarterStep("active", "ACTIVE", new("C", 1), new("D", 1), ZeroLoad),
                        Unload("W1", ("ZA", 1), ("ZB", 1), ("D", 1)),
                    ],
                    0, 0, 0, 0),
                new PlannedRoute(
                    2, "W1", "W2",
                    [
                        Pickup("W1", ("X", 1), ("E", 1)),
                        new BarterStep("active2", "ACTIVE2", new("E", 1), new("F", 1), ZeroLoad),
                        Unload("W2", ("X", 1), ("F", 1)),
                    ],
                    0, 0, 0, 0),
                new PlannedRoute(
                    3, "W2", "W2",
                    [
                        Pickup("W2", ("X", 1)),
                        new BarterStep("active3", "ACTIVE3", new("X", 1), new("G", 1), ZeroLoad),
                        Unload("W2", ("G", 1)),
                    ],
                    0, 0, 0, 0),
            ],
            null,
            [],
            RoutePlanFingerprint.Compute(originalRequest));
        RoutePlanReplayResult originalReplay =
            RouteReplay.ReplayPlan(originalRequest, skeleton);
        Assert.True(originalReplay.Success,
            RouteReplay.FormatFailureDetail(originalReplay.Failure));
        var progressRequest = new AutomaticRoutePlanningRequest(
            allTasks.Where(task => task.RowId is not ("c1" or "c2")).ToArray(),
            items,
            warehouses,
            0,
            100,
            originalRequest.Limits,
            originalRequest.ConfigurationVersion);

        RoutePlanPublicationResult prepared =
            RoutePlanPublication.PreparePlanForPublication(
                progressRequest,
                originalReplay.Plan!,
                RoutePlanPublicationSource.CompletionProgress,
                new HashSet<string>(StringComparer.Ordinal) { "c1", "c2" });

        Assert.True(prepared.Success,
            $"{prepared.Failure?.Code}: {prepared.Failure?.Detail}");
        WarehousePickupStep firstPickup =
            Assert.IsType<WarehousePickupStep>(prepared.Plan!.Routes[0].Steps[0]);
        Assert.Equal(["C"], firstPickup.Items.Select(item => item.ItemId).ToArray());
        WarehouseUnloadStep handoff =
            Assert.IsType<WarehouseUnloadStep>(prepared.Plan.Routes[1].Steps[^1]);
        Assert.Contains(handoff.Items, item => item.ItemId == "X");
        WarehousePickupStep downstreamPickup =
            Assert.IsType<WarehousePickupStep>(prepared.Plan.Routes[2].Steps[0]);
        Assert.Contains(downstreamPickup.Items, item => item.ItemId == "X");
    }

    [Fact]
    public void CompletionProgress_PreservesHandoffCarriedByPartiallyCompletedRoute() {
        // The completed prefix leaves A and B as obsolete pickup cargo. Both
        // must be removed together before ACTIVE fits, while X must remain on
        // the same route because it is ferried from W1 to W2 for route 2.
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["A"] = new("A", "A", 1, 30),
            ["B"] = new("B", "B", 1, 30),
            ["C"] = new("C", "C", 1, 10),
            ["X"] = new("X", "X", 1, 30),
            ["D"] = new("D", "D", 2, 60),
            ["ZA"] = new("ZA", "ZA", 0, 0),
            ["ZB"] = new("ZB", "ZB", 0, 0),
            ["G"] = new("G", "G", 2, 30),
        };
        RouteBarterTask[] allTasks = [
            new("c1", "C1", new RoutePoint(1, 0), "A", 1, "ZA", 1),
            new("c2", "C2", new RoutePoint(2, 0), "B", 1, "ZB", 1),
            new("active", "ACTIVE", new RoutePoint(3, 0), "C", 1, "D", 1),
            new("next", "NEXT", new RoutePoint(4, 0), "X", 1, "G", 1),
        ];
        RouteWarehouse[] warehouses = [
            new("W1", "W1", new RoutePoint(0, 0),
                new Dictionary<string, int>(StringComparer.Ordinal) {
                    ["A"] = 1,
                    ["B"] = 1,
                    ["C"] = 1,
                    ["X"] = 1,
                }),
            new("W2", "W2", new RoutePoint(5, 0),
                new Dictionary<string, int>(StringComparer.Ordinal) {
                    ["X"] = 0,
                }),
        ];
        var originalRequest = new AutomaticRoutePlanningRequest(
            allTasks,
            items,
            warehouses,
            0,
            100,
            new RouteSearchLimits(100_000, 1_000),
            "partial-route-handoff");
        var skeleton = new RoutePlan(
            RoutePlanStatus.Optimal,
            [
                new PlannedRoute(
                    1, "W1", "W2",
                    [
                        Pickup("W1", ("A", 1), ("B", 1), ("C", 1), ("X", 1)),
                        Barter("c1", "A", 1, "ZA", 1),
                        Barter("c2", "B", 1, "ZB", 1),
                        Barter("active", "C", 1, "D", 1),
                        Unload("W2", ("X", 1), ("D", 1)),
                    ],
                    0, 0, 0, 0),
                new PlannedRoute(
                    2, "W2", "W2",
                    [
                        Pickup("W2", ("X", 1)),
                        Barter("next", "X", 1, "G", 1),
                        Unload("W2", ("G", 1)),
                    ],
                    0, 0, 0, 0),
            ],
            null,
            [],
            RoutePlanFingerprint.Compute(originalRequest));
        RoutePlanReplayResult originalReplay =
            RouteReplay.ReplayPlan(originalRequest, skeleton);
        Assert.True(originalReplay.Success,
            RouteReplay.FormatFailureDetail(originalReplay.Failure));
        var progressRequest = new AutomaticRoutePlanningRequest(
            allTasks.Where(task => task.RowId is not ("c1" or "c2")).ToArray(),
            items,
            warehouses,
            0,
            100,
            originalRequest.Limits,
            originalRequest.ConfigurationVersion);

        RoutePlanPublicationResult prepared =
            RoutePlanPublication.PreparePlanForPublication(
                progressRequest,
                originalReplay.Plan!,
                RoutePlanPublicationSource.CompletionProgress,
                new HashSet<string>(StringComparer.Ordinal) { "c1", "c2" });

        Assert.True(prepared.Success,
            $"{prepared.Failure?.Code}: {prepared.Failure?.Detail}");
        WarehousePickupStep firstPickup =
            Assert.IsType<WarehousePickupStep>(prepared.Plan!.Routes[0].Steps[0]);
        Assert.DoesNotContain(firstPickup.Items, item => item.ItemId is "A" or "B");
        Assert.Contains(firstPickup.Items, item => item.ItemId == "X");
        WarehouseUnloadStep handoff =
            Assert.IsType<WarehouseUnloadStep>(prepared.Plan.Routes[0].Steps[^1]);
        Assert.Contains(handoff.Items, item => item.ItemId == "X");
        WarehousePickupStep downstreamPickup =
            Assert.IsType<WarehousePickupStep>(prepared.Plan.Routes[1].Steps[0]);
        Assert.Contains(downstreamPickup.Items, item => item.ItemId == "X");
    }

    [Fact]
    public void CompletionProgress_ReplaysImmediateUnloadBeforeNextWarehousePickup() {
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["SYRUP"] = new("SYRUP", "Syrup", 6, 2_000),
            ["TELESCOPE"] = new("TELESCOPE", "Telescope", 7, 2_000),
            ["NEXT-IN"] = new("NEXT-IN", "Next input", 3, 900),
            ["NEXT-OUT"] = new("NEXT-OUT", "Next output", 4, 1_000),
        };
        var originalRequest = new AutomaticRoutePlanningRequest(
            [
                new RouteBarterTask("finish", "Iliya", new RoutePoint(10, 0),
                    "SYRUP", 5, "TELESCOPE", 5),
                new RouteBarterTask("next", "Tigris", new RoutePoint(20, 0),
                    "NEXT-IN", 10, "NEXT-OUT", 20),
            ],
            items,
            [
                new RouteWarehouse("Velia", "Velia", new RoutePoint(0, 0),
                    new Dictionary<string, int>(StringComparer.Ordinal) {
                        ["SYRUP"] = 5,
                    }),
                new RouteWarehouse("Iliya", "Iliya", new RoutePoint(10, 0),
                    new Dictionary<string, int>(StringComparer.Ordinal) {
                        ["NEXT-IN"] = 10,
                    }),
            ],
            0,
            30_000,
            new RouteSearchLimits(100_000, 1_000),
            "progress-immediate-unload");
        var skeleton = new RoutePlan(
            RoutePlanStatus.Optimal,
            [
                new PlannedRoute(
                    1,
                    "Velia",
                    "Iliya",
                    [
                        Pickup("Velia", ("SYRUP", 5)),
                        MakeBarterAt("finish", "Iliya",
                            "SYRUP", 5, "TELESCOPE", 5),
                        Unload("Iliya", ("TELESCOPE", 5)),
                        Pickup("Iliya", ("NEXT-IN", 10)),
                        MakeBarterAt("next", "Tigris",
                            "NEXT-IN", 10, "NEXT-OUT", 20),
                        Unload("Iliya", ("NEXT-OUT", 20)),
                    ],
                    0,
                    0,
                    0,
                    0),
            ],
            null,
            [],
            RoutePlanFingerprint.Compute(originalRequest));
        RoutePlanReplayResult replayed =
            RouteReplay.ReplayPlan(originalRequest, skeleton);
        Assert.True(replayed.Success,
            RouteReplay.FormatFailureDetail(replayed.Failure));
        var progressRequest = new AutomaticRoutePlanningRequest(
            originalRequest.Tasks.Where(task => task.RowId == "next").ToArray(),
            originalRequest.Items,
            originalRequest.Warehouses,
            originalRequest.ExtraLT,
            originalRequest.TotalLT,
            originalRequest.Limits,
            originalRequest.ConfigurationVersion);

        RoutePlanPublicationResult prepared =
            RoutePlanPublication.PreparePlanForPublication(
                progressRequest,
                replayed.Plan!,
                RoutePlanPublicationSource.CompletionProgress,
                new HashSet<string>(StringComparer.Ordinal) { "finish" });

        Assert.True(prepared.Success,
            $"{prepared.Failure?.Code}: {prepared.Failure?.Detail}");
        PlannedRoute remaining = Assert.Single(prepared.Plan!.Routes);
        WarehouseUnloadStep first =
            Assert.IsType<WarehouseUnloadStep>(remaining.Steps[0]);
        Assert.Contains(first.Items,
            item => item.ItemId == "TELESCOPE" && item.Quantity == 5);
        Assert.IsType<WarehousePickupStep>(remaining.Steps[1]);
        Assert.Equal(
            "next",
            Assert.IsType<BarterStep>(remaining.Steps[2]).RowId);
    }

    private static RoutePlan ApplyPipeline(
        AutomaticRoutePlanningRequest request, RoutePlan plan, HashSet<string> completed) {
        var newRoutes = new List<PlannedRoute>();
        foreach (var route in plan.Routes) {
            var remaining = RouteProgressFilter.ExcludeCompletedBarters(
                route.Steps, completed).ToArray();
            if (remaining.Length == 0) continue;
            if (!remaining.OfType<BarterStep>().Any()) continue;
            newRoutes.Add(new PlannedRoute(
                route.Number, route.StartWarehouseId, route.EndWarehouseId,
                remaining, route.Distance, route.InitialLT, route.CurrentLT, route.PeakLT));
        }
        var reconciled = new RoutePlan(plan.Status, newRoutes, plan.Objective,
            plan.Diagnostics, plan.InputFingerprint);
        var norm = RouteCargoNormalizer.Normalize(request, reconciled);
        return norm.Success ? norm.Plan! : reconciled;
    }
}

internal static class TestRequestFactory {
    public static AutomaticRoutePlanningRequest BuildFor(PlannedRoute route) {
        var itemIds = new HashSet<string>(StringComparer.Ordinal);
        var warehouseStocks = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var step in route.Steps) {
            switch (step) {
                case WarehousePickupStep pickup:
                    foreach (var item in pickup.Items)
                        warehouseStocks[item.ItemId] = Math.Max(
                            warehouseStocks.GetValueOrDefault(item.ItemId), item.Quantity);
                    break;
                case WarehouseUnloadStep unload:
                    foreach (var item in unload.Items)
                        warehouseStocks.TryAdd(item.ItemId, 0);
                    break;
                case BarterStep barter:
                    itemIds.Add(barter.Consumed.ItemId);
                    itemIds.Add(barter.Produced.ItemId);
                    warehouseStocks.TryAdd(barter.Consumed.ItemId, 0);
                    warehouseStocks.TryAdd(barter.Produced.ItemId, 0);
                    break;
            }
        }
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal);
        foreach (var id in itemIds) {
            items[id] = new RouteItem(id, id, 1, CargoWeightTable.GetWeightForLevel(1));
        }
        return new AutomaticRoutePlanningRequest(
            Array.Empty<RouteBarterTask>(),
            items,
            [new RouteWarehouse("Iliya", "Iliya", new RoutePoint(0, 0), warehouseStocks)],
            extraLT: 0, totalLT: 1_000_000,
            new RouteSearchLimits(100_000, 1_000), "test-fp");
    }
}
