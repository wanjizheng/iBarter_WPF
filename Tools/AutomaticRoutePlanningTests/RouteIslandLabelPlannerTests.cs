using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

/// <summary>
/// Audit round 4 (regression fix): the audit's user-reported
/// regression was that real BarterStep islands (Baremi, Crow, ...)
/// in AutomaticRoute mode lost their island name labels after the
/// c5188ac fix suppressed the Planner-barter render loop. The fix
/// extracts a pure decision helper <see cref="RouteIslandLabelPlanner"/>
/// so the island-id set is unit-testable; the WPF Label wiring lives
/// in <c>MapControl.xaml.cs</c>.
/// </summary>
public class RouteIslandLabelPlannerTests {
    private const int W_800045 = 1000;
    private const int W_800208 = 2000;
    private const int W_800241 = 2000;

    private static AutomaticRoutePlanningRequest BuildRequest(
        params (string id, int level)[] items) {
        var dict = new Dictionary<string, RouteItem>(StringComparer.Ordinal);
        var inventory = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (id, level) in items) {
            dict[id] = new RouteItem(id, id, level, CargoWeightTable.GetWeightForLevel(level));
            inventory[id] = 1000;
        }
        return new AutomaticRoutePlanningRequest(
            Array.Empty<RouteBarterTask>(),
            dict,
            [new RouteWarehouse("Iliya", "Iliya", new RoutePoint(0, 0), inventory)],
            extraLT: 0, totalLT: 100_000,
            new RouteSearchLimits(100_000, 1_000), "test-fp");
    }

    // Test 1: visible islands include every BarterStep island plus
    // every pickup/unload island.
    [Fact]
    public void Visible_Islands_IncludeEveryRouteStepIsland() {
        var route = new PlannedRoute(1, "Iliya", "Iliya", new RouteStep[] {
            new WarehousePickupStep("Iliya", "Iliya",
                [new RouteItemQuantity("800049", 1)], new RouteLoadSnapshot(0, 0, 0)),
            new BarterStep("Baremi:800049:10", "Baremi",
                new RouteItemQuantity("800049", 1), new RouteItemQuantity("10", 1),
                new RouteLoadSnapshot(0, 0, 0)),
            new BarterStep("Crow:10:11", "Crow",
                new RouteItemQuantity("10", 1), new RouteItemQuantity("11", 1),
                new RouteLoadSnapshot(0, 0, 0)),
            new WarehouseUnloadStep("Iliya", "Iliya",
                [new RouteItemQuantity("11", 1)], new RouteLoadSnapshot(0, 0, 0)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [route], null, [], "fp");

        var islands = RouteIslandLabelPlanner.VisibleIslandIds(plan, showAll: false, selectedRouteNumber: 1);

        Assert.Contains("Iliya", islands);
        Assert.Contains("Baremi", islands);
        Assert.Contains("Crow", islands);
    }

    // Test 2: even when the Planner-barter render loop is suppressed,
    // BarterStep islands still need labels — that is the whole point
    // of the fix.
    [Fact]
    public void Visible_Islands_Independent_OfPlannerBarterRendering() {
        var route = new PlannedRoute(1, "Iliya", "Iliya", new RouteStep[] {
            new WarehousePickupStep("Iliya", "Iliya",
                [new RouteItemQuantity("800049", 1)], new RouteLoadSnapshot(0, 0, 0)),
            new BarterStep("Baremi:800049:10", "Baremi",
                new RouteItemQuantity("800049", 1), new RouteItemQuantity("10", 1),
                new RouteLoadSnapshot(0, 0, 0)),
            new BarterStep("Crow:10:11", "Crow",
                new RouteItemQuantity("10", 1), new RouteItemQuantity("11", 1),
                new RouteLoadSnapshot(0, 0, 0)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [route], null, [], "fp");

        var islands = RouteIslandLabelPlanner.VisibleIslandIds(plan, showAll: false, selectedRouteNumber: 1);

        // Whether or not the Planner-barter loop is suppressed, the
        // audit expects these island ids in the render set.
        Assert.Contains("Baremi", islands);
        Assert.Contains("Crow", islands);
    }

    // Test 3: islands with no Planner barters AND no route steps
    // must NOT be in the visible island set.
    [Fact]
    public void Visible_Islands_Excludes_Untouched_Islands() {
        var route = new PlannedRoute(1, "Iliya", "Iliya", new RouteStep[] {
            new WarehousePickupStep("Iliya", "Iliya",
                [new RouteItemQuantity("800049", 1)], new RouteLoadSnapshot(0, 0, 0)),
            new BarterStep("Baremi:800049:10", "Baremi",
                new RouteItemQuantity("800049", 1), new RouteItemQuantity("10", 1),
                new RouteLoadSnapshot(0, 0, 0)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [route], null, [], "fp");

        var islands = RouteIslandLabelPlanner.VisibleIslandIds(plan, showAll: false, selectedRouteNumber: 1);

        Assert.DoesNotContain("Velia", islands);
        Assert.DoesNotContain("Epheria", islands);
    }

    // Test 4: warehouse islands are excluded from the
    // route-only label set so the planner doesn't stack a second
    // label on top of the warehouse's "装货"/"卸货" text.
    [Fact]
    public void ExcludeWarehouseIslands_RemovesIslandsWithWarehouseNode() {
        var ids = new[] { "Iliya", "Baremi", "Crow" };
        var warehouses = new HashSet<string>(StringComparer.Ordinal) { "Iliya" };

        var filtered = RouteIslandLabelPlanner.ExcludeWarehouseIslands(ids, warehouses);

        Assert.DoesNotContain("Iliya", filtered);
        Assert.Contains("Baremi", filtered);
        Assert.Contains("Crow", filtered);
    }

    // Test 5: BarterStep island labels come from the route step,
    // never from a Planner-barter FirstOrDefault lookup.
    [Fact]
    public void BarterStep_Island_Label_Source_IsRouteStepIsland() {
        // The route step's islandId is the canonical source.  Even
        // when the Planner grid has a different island listed for the
        // same RowId, the route step wins.
        var route = new PlannedRoute(1, "Iliya", "Iliya", new RouteStep[] {
            new WarehousePickupStep("Iliya", "Iliya",
                [new RouteItemQuantity("800049", 1)], new RouteLoadSnapshot(0, 0, 0)),
            new BarterStep("X:800049:10", "CanonicalBaremi",
                new RouteItemQuantity("800049", 1), new RouteItemQuantity("10", 1),
                new RouteLoadSnapshot(0, 0, 0)),
            new WarehouseUnloadStep("Iliya", "Iliya",
                [new RouteItemQuantity("10", 1)], new RouteLoadSnapshot(0, 0, 0)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [route], null, [], "fp");

        var islands = RouteIslandLabelPlanner.VisibleIslandIds(plan, showAll: false, selectedRouteNumber: 1);

        Assert.Contains("CanonicalBaremi", islands);
        // The audit forbids falling back to any Planner-side
        // FirstOrDefault on IslandId/IslandName; the planner helper
        // never inspects Planner barters.
    }

    // Test 6: when a single island appears in multiple route steps
    // (pickup + barter + unload on the same island), the visible set
    // still contains the island only once.
    [Fact]
    public void SameIsland_DeduplicatesAcrossSteps() {
        var route = new PlannedRoute(1, "Iliya", "Iliya", new RouteStep[] {
            new WarehousePickupStep("Iliya", "Iliya",
                [new RouteItemQuantity("800049", 1)], new RouteLoadSnapshot(0, 0, 0)),
            new BarterStep("X:800049:10", "Iliya",
                new RouteItemQuantity("800049", 1), new RouteItemQuantity("10", 1),
                new RouteLoadSnapshot(0, 0, 0)),
            new WarehouseUnloadStep("Iliya", "Iliya",
                [new RouteItemQuantity("10", 1)], new RouteLoadSnapshot(0, 0, 0)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [route], null, [], "fp");

        var islands = RouteIslandLabelPlanner.VisibleIslandIds(plan, showAll: false, selectedRouteNumber: 1);

        // Iliya appears three times in the route but the visible
        // island set dedupes to a single entry.
        Assert.Single(islands, x => x == "Iliya");
    }

    // Test 7: ShowAll mode returns the union of islands across all
    // routes, with duplicates removed.
    [Fact]
    public void ShowAll_DeduplicatesAcrossRoutes() {
        var route1 = new PlannedRoute(1, "Iliya", "Iliya", new RouteStep[] {
            new WarehousePickupStep("Iliya", "Iliya",
                [new RouteItemQuantity("800049", 1)], new RouteLoadSnapshot(0, 0, 0)),
            new BarterStep("X:800049:10", "Baremi",
                new RouteItemQuantity("800049", 1), new RouteItemQuantity("10", 1),
                new RouteLoadSnapshot(0, 0, 0)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);
        var route2 = new PlannedRoute(2, "Iliya", "Iliya", new RouteStep[] {
            new WarehousePickupStep("Iliya", "Iliya",
                [new RouteItemQuantity("800049", 1)], new RouteLoadSnapshot(0, 0, 0)),
            new BarterStep("Y:800049:10", "Baremi",
                new RouteItemQuantity("800049", 1), new RouteItemQuantity("10", 1),
                new RouteLoadSnapshot(0, 0, 0)),
            new BarterStep("Z:10:11", "Crow",
                new RouteItemQuantity("10", 1), new RouteItemQuantity("11", 1),
                new RouteLoadSnapshot(0, 0, 0)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);
        var plan = new RoutePlan(RoutePlanStatus.Optimal,
            [route1, route2], null, [], "fp");

        var islands = RouteIslandLabelPlanner.VisibleIslandIds(plan, showAll: true, selectedRouteNumber: null);

        // Baremi appears in both routes but should appear only once
        // in the deduped set.
        Assert.Single(islands, x => x == "Baremi");
        Assert.Single(islands, x => x == "Iliya");
        Assert.Single(islands, x => x == "Crow");
    }

    // Test 8: switching to a different SelectedRouteNumber swaps
    // the visible island set; islands exclusive to the previous
    // route are no longer in the set.
    [Fact]
    public void RouteSwitch_RemovesStaleIslands() {
        var route1 = new PlannedRoute(1, "Iliya", "Iliya", new RouteStep[] {
            new BarterStep("X:800049:10", "Baremi",
                new RouteItemQuantity("800049", 1), new RouteItemQuantity("10", 1),
                new RouteLoadSnapshot(0, 0, 0)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);
        var route2 = new PlannedRoute(2, "Iliya", "Iliya", new RouteStep[] {
            new BarterStep("Y:800049:10", "Crow",
                new RouteItemQuantity("800049", 1), new RouteItemQuantity("10", 1),
                new RouteLoadSnapshot(0, 0, 0)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);
        var plan = new RoutePlan(RoutePlanStatus.Optimal,
            [route1, route2], null, [], "fp");

        var route1Islands = RouteIslandLabelPlanner.VisibleIslandIds(plan, showAll: false, selectedRouteNumber: 1);
        var route2Islands = RouteIslandLabelPlanner.VisibleIslandIds(plan, showAll: false, selectedRouteNumber: 2);

        Assert.Contains("Baremi", route1Islands);
        Assert.DoesNotContain("Crow", route1Islands);
        Assert.Contains("Crow", route2Islands);
        Assert.DoesNotContain("Baremi", route2Islands);
    }

    // Test 9: a null plan returns an empty set without throwing —
    // this is the "exit AutomaticRoute mode" path.
    [Fact]
    public void NullPlan_ReturnsEmptySet() {
        var islands = RouteIslandLabelPlanner.VisibleIslandIds(
            plan: null, showAll: false, selectedRouteNumber: 1);
        Assert.Empty(islands);
    }

    // Test 10: ExcludeWarehouseIslands with no warehouses returns
    // every island unchanged.
    [Fact]
    public void ExcludeWarehouseIslands_NoWarehouses_ReturnsAll() {
        var ids = new[] { "Iliya", "Baremi", "Crow" };
        var warehouses = new HashSet<string>(StringComparer.Ordinal);

        var filtered = RouteIslandLabelPlanner.ExcludeWarehouseIslands(ids, warehouses);

        Assert.Equal(3, filtered.Count);
        Assert.Contains("Iliya", filtered);
        Assert.Contains("Baremi", filtered);
        Assert.Contains("Crow", filtered);
    }

    // Test 11: realistic 10-step fixture mirroring the user's
    // reported regression — the visible set must contain every island
    // the route plan touches.
    [Fact]
    public void RealisticTenStepFixture_AllIslandsVisible() {
        // Mirror the audit's example route: Iliya pickup, eight
        // barters on different islands, then Iliya unload.
        var route = new PlannedRoute(1, "Iliya", "Iliya", new RouteStep[] {
            new WarehousePickupStep("Iliya", "Iliya",
                [new RouteItemQuantity("800045", 1)], new RouteLoadSnapshot(0, 0, 0)),
            new BarterStep("B:800045:800208", "Baremi",
                new RouteItemQuantity("800045", 1), new RouteItemQuantity("800208", 1),
                new RouteLoadSnapshot(0, 0, 0)),
            new BarterStep("C:800208:11", "Crow",
                new RouteItemQuantity("800208", 1), new RouteItemQuantity("11", 1),
                new RouteLoadSnapshot(0, 0, 0)),
            new BarterStep("R:11:12", "Rickun",
                new RouteItemQuantity("11", 1), new RouteItemQuantity("12", 1),
                new RouteLoadSnapshot(0, 0, 0)),
            new BarterStep("CP:12:13", "Cox_Pirate",
                new RouteItemQuantity("12", 1), new RouteItemQuantity("13", 1),
                new RouteLoadSnapshot(0, 0, 0)),
            new BarterStep("G:13:800212", "Grandiha",
                new RouteItemQuantity("13", 1), new RouteItemQuantity("800212", 1),
                new RouteLoadSnapshot(0, 0, 0)),
            new BarterStep("M:800212:800216", "Midnight",
                new RouteItemQuantity("800212", 1), new RouteItemQuantity("800216", 1),
                new RouteLoadSnapshot(0, 0, 0)),
            new BarterStep("S:800216:800235", "Sausan",
                new RouteItemQuantity("800216", 1), new RouteItemQuantity("800235", 1),
                new RouteLoadSnapshot(0, 0, 0)),
            new BarterStep("San:800235:800237", "Sanctuary",
                new RouteItemQuantity("800235", 1), new RouteItemQuantity("800237", 1),
                new RouteLoadSnapshot(0, 0, 0)),
            new WarehouseUnloadStep("Iliya", "Iliya",
                [new RouteItemQuantity("800237", 1)], new RouteLoadSnapshot(0, 0, 0)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [route], null, [], "fp");

        var visible = RouteIslandLabelPlanner.VisibleIslandIds(plan, showAll: false, selectedRouteNumber: 1);

        // Iliya is the warehouse so the planner won't render a duplicate
        // label for it (EnsureAutomaticWarehouseNodes handles that);
        // here we test the pre-warehouse-filter visible set.
        Assert.Contains("Iliya", visible);
        Assert.Contains("Baremi", visible);
        Assert.Contains("Crow", visible);
        Assert.Contains("Rickun", visible);
        Assert.Contains("Cox_Pirate", visible);
        Assert.Contains("Grandiha", visible);
        Assert.Contains("Midnight", visible);
        Assert.Contains("Sausan", visible);
        Assert.Contains("Sanctuary", visible);
    }

    // Test 12: persistent restore does not affect island visibility —
    // the planner is stateless WRT save/reload, so the round-tripped
    // plan yields the same island set as before save.
    [Fact]
    public void PersistenceRoundTrip_PreservesVisibleIslands() {
        var route = new PlannedRoute(1, "Iliya", "Iliya", new RouteStep[] {
            new WarehousePickupStep("Iliya", "Iliya",
                [new RouteItemQuantity("800049", 1)], new RouteLoadSnapshot(0, 0, 0)),
            new BarterStep("X:800049:10", "Baremi",
                new RouteItemQuantity("800049", 1), new RouteItemQuantity("10", 1),
                new RouteLoadSnapshot(0, 0, 0)),
            new BarterStep("Y:10:11", "Crow",
                new RouteItemQuantity("10", 1), new RouteItemQuantity("11", 1),
                new RouteLoadSnapshot(0, 0, 0)),
            new WarehouseUnloadStep("Iliya", "Iliya",
                [new RouteItemQuantity("11", 1)], new RouteLoadSnapshot(0, 0, 0)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [route], null, [], "fp");

        // Round-trip through persistence.
        string tempPath = Path.Combine(Path.GetTempPath(),
            $"autoroute-island-{Guid.NewGuid():N}.json");
        try {
            RoutePlanPersistence.Save(tempPath, plan, 1, false);
            var loaded = RoutePlanPersistence.TryLoad(tempPath, "fp");
            Assert.Equal(RoutePlanLoadStatus.Loaded, loaded.Status);
            var reloadedPlan = loaded.Snapshot!.Plan;

            var before = RouteIslandLabelPlanner.VisibleIslandIds(plan, showAll: false, selectedRouteNumber: 1);
            var after = RouteIslandLabelPlanner.VisibleIslandIds(reloadedPlan, showAll: false, selectedRouteNumber: 1);

            Assert.Equal(before, after);
            Assert.Contains("Baremi", after);
            Assert.Contains("Crow", after);
        }
        finally {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}