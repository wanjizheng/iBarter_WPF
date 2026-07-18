using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

/// <summary>
/// Audit round 6: per-step labels for the AutomaticRoute map.
/// Replaces the v1 island-id-only label set that lost identity
/// when two BarterSteps shared an island.  These tests pin:
///   * the exact text format for each step kind (no IslandId
///     fallback to Planner barters);
///   * the (RouteNumber, StepIndex) identity contract;
///   * same-island multi-step dedup rules (warehouse dedup,
///     barter NOT deduped);
///   * the reconcile + reposition split;
///   * the no-completion-authority guarantee.
/// </summary>
public class RouteStepLabelTests {
    [Fact]
    public void BarterStep_DisplayText_UsesConsumedAndProduced_NotIslandId() {
        // 800208 × 5 → 800241 × 1: a real gold-cactus → artisan
        // shell-necklace trade. The audit forbids falling back to
        // any Planner-barter lookup on IslandId; the text is
        // computed directly from the step's Consumed/Produced.
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [
            new PlannedRoute(1, "Iliya", "Iliya", [
                new WarehousePickupStep("Iliya", "Iliya",
                    [new RouteItemQuantity("800208", 5)],
                    new RouteLoadSnapshot(0, 0, 0)),
                new BarterStep("5:Iliya:800208:800241", "Iliya",
                    new RouteItemQuantity("800208", 5),
                    new RouteItemQuantity("800241", 1),
                    new RouteLoadSnapshot(0, 0, 0)),
            ], distance: 0, initialLT: 0, currentLT: 0, peakLT: 0),
        ], null, [], "fp");
        var labels = RouteStepLabelPlanner.PlanLabels(
            plan, showAll: false, selectedRouteNumber: 1,
            itemDisplayNames: new Dictionary<string, string>(StringComparer.Ordinal) {
                ["800208"] = "金色仙人掌花束",
                ["800241"] = "匠人的贝壳项链",
            });
        var barter = labels.Single(l => l.StepKind == RouteStepKind.Barter);
        Assert.Equal("金色仙人掌花束 × 5 → 匠人的贝壳项链 × 1", barter.DisplayText);
    }

    [Fact]
    public void Pickup_DisplayText_IsIslandDashPickup() {
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [
            new PlannedRoute(1, "Iliya", "Iliya", [
                new WarehousePickupStep("Iliya", "Iliya",
                    [new RouteItemQuantity("800208", 1)],
                    new RouteLoadSnapshot(0, 0, 0)),
            ], distance: 0, initialLT: 0, currentLT: 0, peakLT: 0),
        ], null, [], "fp");
        var labels = RouteStepLabelPlanner.PlanLabels(
            plan, showAll: false, selectedRouteNumber: 1,
            itemDisplayNames: new Dictionary<string, string>(StringComparer.Ordinal));
        var pickup = labels.Single(l => l.StepKind == RouteStepKind.Pickup);
        Assert.Equal("Iliya · 装货", pickup.DisplayText);
        Assert.True(pickup.IsWarehouseOperation);
        Assert.Null(pickup.BarterRowId);
    }

    [Fact]
    public void Unload_DisplayText_IsIslandDashUnload() {
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [
            new PlannedRoute(1, "Iliya", "Iliya", [
                new WarehouseUnloadStep("Iliya", "Iliya",
                    [new RouteItemQuantity("10", 1)],
                    new RouteLoadSnapshot(0, 0, 0)),
            ], distance: 0, initialLT: 0, currentLT: 0, peakLT: 0),
        ], null, [], "fp");
        var labels = RouteStepLabelPlanner.PlanLabels(
            plan, showAll: false, selectedRouteNumber: 1,
            itemDisplayNames: new Dictionary<string, String>());
        var unload = labels.Single(l => l.StepKind == RouteStepKind.Unload);
        Assert.Equal("Iliya · 卸货", unload.DisplayText);
        Assert.True(unload.IsWarehouseOperation);
    }

    [Fact]
    public void SameIsland_TwoBarterSteps_GetDistinctIdentities() {
        // Two barters on "Iliya" both with the same business keys
        // would have collided under the v1 HashSet<IslandId>. With
        // (RouteNumber, StepIndex) identity they remain distinct.
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [
            new PlannedRoute(1, "Iliya", "Iliya", [
                new BarterStep("0:Iliya:800208:800241", "Iliya",
                    new RouteItemQuantity("800208", 1),
                    new RouteItemQuantity("800241", 1),
                    new RouteLoadSnapshot(0, 0, 0)),
                new BarterStep("1:Iliya:800208:800241", "Iliya",
                    new RouteItemQuantity("800208", 1),
                    new RouteItemQuantity("800241", 1),
                    new RouteLoadSnapshot(0, 0, 0)),
            ], distance: 0, initialLT: 0, currentLT: 0, peakLT: 0),
        ], null, [], "fp");
        var labels = RouteStepLabelPlanner.PlanLabels(
            plan, showAll: false, selectedRouteNumber: 1,
            itemDisplayNames: new Dictionary<string, string>());
        Assert.Equal(2, labels.Count);
        Assert.NotEqual(labels[0].Identity, labels[1].Identity);
        Assert.Equal("r1s0", labels[0].Identity);
        Assert.Equal("r1s1", labels[1].Identity);
    }

    [Fact]
    public void SameIsland_PickupBarterUnload_ThreeIndependentLabels() {
        // Iliya with pickup + barter + unload gets three
        // independent labels (no dedup across kinds).
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [
            new PlannedRoute(1, "Iliya", "Iliya", [
                new WarehousePickupStep("Iliya", "Iliya",
                    [new RouteItemQuantity("800208", 1)],
                    new RouteLoadSnapshot(0, 0, 0)),
                new BarterStep("0:Iliya:800208:800241", "Iliya",
                    new RouteItemQuantity("800208", 1),
                    new RouteItemQuantity("800241", 1),
                    new RouteLoadSnapshot(0, 0, 0)),
                new WarehouseUnloadStep("Iliya", "Iliya",
                    [new RouteItemQuantity("800241", 1)],
                    new RouteLoadSnapshot(0, 0, 0)),
            ], distance: 0, initialLT: 0, currentLT: 0, peakLT: 0),
        ], null, [], "fp");
        var labels = RouteStepLabelPlanner.PlanLabels(
            plan, showAll: false, selectedRouteNumber: 1,
            itemDisplayNames: new Dictionary<string, string>());
        Assert.Equal(3, labels.Count);
        // Barter is NOT deduped even though pickup/unload share
        // the same island.
        Assert.Contains(labels, l => l.StepKind == RouteStepKind.Pickup);
        Assert.Contains(labels, l => l.StepKind == RouteStepKind.Barter);
        Assert.Contains(labels, l => l.StepKind == RouteStepKind.Unload);
    }

    [Fact]
    public void WarehouseDedup_CollapsesTwoWarehouses_PreservesBarter() {
        // Iliya with pickup + unload + a barter on a different
        // island. The pickup and unload on the same island dedup
        // to one warehouse label; the barter on the OTHER island
        // survives untouched.
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [
            new PlannedRoute(1, "Iliya", "Crow", [
                new WarehousePickupStep("Iliya", "Iliya",
                    [new RouteItemQuantity("800049", 1)],
                    new RouteLoadSnapshot(0, 0, 0)),
                new BarterStep("0:Crow:800049:10", "Crow",
                    new RouteItemQuantity("800049", 1),
                    new RouteItemQuantity("10", 1),
                    new RouteLoadSnapshot(0, 0, 0)),
                new WarehouseUnloadStep("Iliya", "Iliya",
                    [new RouteItemQuantity("10", 1)],
                    new RouteLoadSnapshot(0, 0, 0)),
            ], distance: 0, initialLT: 0, currentLT: 0, peakLT: 0),
        ], null, [], "fp");
        var labels = RouteStepLabelPlanner.PlanLabels(
            plan, showAll: false, selectedRouteNumber: 1,
            itemDisplayNames: new Dictionary<string, string>());
        var deduped = RouteStepLabelPlanner.StripWarehouseDuplicates(labels);
        // Two warehouses on Iliya → one survives.
        Assert.Equal(2, deduped.Count);
        Assert.Single(deduped, l => l.StepKind == RouteStepKind.Pickup
            || l.StepKind == RouteStepKind.Unload);
        // The barter on Crow is untouched.
        Assert.Single(deduped, l => l.StepKind == RouteStepKind.Barter);
    }

    [Fact]
    public void BarterLabel_DoesNotFallBackToPlannerBarters() {
        // The audit forbids any IslandId / IslandName /
        // FirstOrDefault lookup against App.myPVM.BarterCollection.
        // The planner is invoked with an empty item lookup
        // (mimicking the catalog being unavailable); the resulting
        // label must contain the raw ItemId, not "undefined" or
        // any Planner-barter's data.
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [
            new PlannedRoute(1, "Iliya", "Iliya", [
                new BarterStep("X:800049:10", "Crow",
                    new RouteItemQuantity("800049", 5),
                    new RouteItemQuantity("10", 7),
                    new RouteLoadSnapshot(0, 0, 0)),
            ], distance: 0, initialLT: 0, currentLT: 0, peakLT: 0),
        ], null, [], "fp");
        var labels = RouteStepLabelPlanner.PlanLabels(
            plan, showAll: false, selectedRouteNumber: 1,
            itemDisplayNames: new Dictionary<string, string>());
        var barter = labels.Single();
        Assert.Equal("800049 × 5 → 10 × 7", barter.DisplayText);
    }

    [Fact]
    public void Reconcile_DistinguishesByStepIndex_NotIslandId() {
        // The v1 reconcile used HashSet<string> over island ids.
        // The v6 reconcile must distinguish two labels on the
        // same island.
        var pickup = new RouteStepMapLabel(
            1, 0, RouteStepKind.Pickup, "Iliya", null,
            "Iliya · 装货", true);
        var barter = new RouteStepMapLabel(
            1, 1, RouteStepKind.Barter, "Iliya", "0:Iliya:800208:800241",
            "800208 × 1 → 800241 × 1", false);
        var unload = new RouteStepMapLabel(
            1, 2, RouteStepKind.Unload, "Iliya", null,
            "Iliya · 卸货", true);

        // Add all three, existing set empty.
        var outcome = RouteStepLabelRenderer.Reconcile(
            new[] { pickup, barter, unload },
            new HashSet<string>(StringComparer.Ordinal),
            hostWidth: 1280, hostHeight: 720,
            out var toAdd, out var toRemove);

        Assert.Equal(RouteStepLabelRenderer.RebuildOutcome.Built, outcome);
        Assert.Equal(3, toAdd.Count);
        Assert.Empty(toRemove);
        // No island-only collapse: each entry has its own identity.
        var distinctIds = toAdd.Select(l => l.Identity).ToHashSet();
        Assert.Equal(3, distinctIds.Count);
    }

    [Fact]
    public void Reconcile_IdentityMatch_EmptyOutcome_ForReposition() {
        // Identity-stable input → Empty outcome. The WPF layer
        // must NOT recreate wrappers; it should call
        // RepositionRouteStepLabels separately for geometry
        // updates.
        var pickup = new RouteStepMapLabel(
            1, 0, RouteStepKind.Pickup, "Iliya", null,
            "Iliya · 装货", true);
        var existing = new HashSet<string>(StringComparer.Ordinal) { pickup.Identity };
        var outcome = RouteStepLabelRenderer.Reconcile(
            new[] { pickup }, existing, 1280, 720,
            out var toAdd, out var toRemove);
        Assert.Equal(RouteStepLabelRenderer.RebuildOutcome.Empty, outcome);
        Assert.Empty(toAdd);
        Assert.Empty(toRemove);
    }

    [Fact]
    public void ComputeVerticalOffset_IsDeterministic() {
        // Same input → same output. The WPF layer relies on this
        // to avoid jittering labels on reconcile cycles.
        double a1 = RouteStepLabelRenderer.ComputeVerticalOffset(0, 720);
        double a2 = RouteStepLabelRenderer.ComputeVerticalOffset(0, 720);
        Assert.Equal(a1, a2);
        // Different occurrence → different offset (stacked).
        double b = RouteStepLabelRenderer.ComputeVerticalOffset(1, 720);
        Assert.NotEqual(a1, b);
        // Each successive label is monotonically lower.
        double c = RouteStepLabelRenderer.ComputeVerticalOffset(2, 720);
        Assert.True(b > a1);
        Assert.True(c > b);
    }

    [Fact]
    public void Reconcile_Deferred_ReturnsDeferred_AndPreservesExisting() {
        // Host size 0: first call after IslandsButtonInitialisation
        // (audit round 5). Reconcile returns Deferred; existing
        // labels are preserved untouched so the next SizeChanged
        // callback can retry.
        var pickup = new RouteStepMapLabel(
            1, 0, RouteStepKind.Pickup, "Iliya", null,
            "Iliya · 装货", true);
        var existing = new HashSet<string>(StringComparer.Ordinal) { pickup.Identity };
        var outcome = RouteStepLabelRenderer.Reconcile(
            new[] { pickup }, existing, hostWidth: 0, hostHeight: 0,
            out var toAdd, out var toRemove);
        Assert.Equal(RouteStepLabelRenderer.RebuildOutcome.Deferred, outcome);
        Assert.Empty(toAdd);
        Assert.Empty(toRemove);
    }

    [Fact]
    public void Reconcile_EmptyPlan_DropsEverything() {
        // Manual-mode exit / plan empty: every existing label is
        // removed. WPF layer's RemoveAllRouteStepLabels is the
        // companion pass.
        var existing = new HashSet<string>(StringComparer.Ordinal) {
            "r1s0", "r1s1", "r1s2",
        };
        var outcome = RouteStepLabelRenderer.Reconcile(
            Array.Empty<RouteStepMapLabel>(), existing, 1280, 720,
            out var toAdd, out var toRemove);
        Assert.Equal(RouteStepLabelRenderer.RebuildOutcome.Built, outcome);
        Assert.Empty(toAdd);
        Assert.Equal(3, toRemove.Count);
    }

    [Fact]
    public void RouteStepMapLabel_NeverAuthorizesCompletion() {
        // The label's Tag is the descriptor. The WPF layer binds
        // IsHitTestVisible=false on the wrapper so a double-click
        // cannot fire. The audit's claim is that the renderer
        // never produces anything that grants completion
        // authority. The descriptor's only field that COULD be
        // misinterpreted is BarterRowId — but that is just an
        // identifier, not a handler. There's no StepKind-aware
        // callback or AuthorizesCompletion flag on the descriptor.
        var label = new RouteStepMapLabel(
            1, 0, RouteStepKind.Barter, "Iliya", "0:Iliya:800208:800241",
            "800208 × 1 → 800241 × 1", false);
        // AuthorizesCompletion is the existing map-level gate;
        // it lives on RouteMapNode, not on RouteStepMapLabel.
        // The renderer's descriptor only carries identity + content.
        Assert.False(label.IsWarehouseOperation);
        // The wrapper's IsHitTestVisible=false in the WPF layer
        // is what blocks the click. The test contract here is
        // that the descriptor itself has no handler, no callback,
        // and no "can complete" flag.
        var props = typeof(RouteStepMapLabel).GetProperties();
        Assert.DoesNotContain(props, p => p.Name.Contains("Authorize")
            || p.Name.Contains("CanComplete")
            || p.Name.Contains("Handler"));
    }
}