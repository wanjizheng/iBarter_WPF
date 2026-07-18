using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

/// <summary>
/// Audit round 5: the user-reported regression was that real
/// BarterStep islands lost their labels in AutomaticRoute mode
/// because the renderer silently dropped every label the first
/// time it ran (host size was 0) and the subsequent
/// <c>IslandsButtonRearrange</c> never re-invokes the renderer.
/// These tests pin the lifecycle + reconcile contract so a future
/// regression is caught at unit-test time, not at the WPF UI.
/// </summary>
public class RouteIslandLabelRendererTests {
    [Fact]
    public void Reconcile_HostSizeZero_ReturnsDeferred_AndKeepsExisting() {
        // Host not laid out yet (size 0) — the very first call after
        // MapControl.Loaded. Reconcile MUST return Deferred and must
        // NOT mark any existing label for removal: a defer must
        // preserve state so the next SizeChanged callback can retry.
        var planned = new[] { "Baremi", "Crow" };
        var existing = new HashSet<string>(StringComparer.Ordinal) { "Baremi", "Crow" };

        var outcome = RouteIslandLabelRenderer.Reconcile(
            planned, existing, hostWidth: 0, hostHeight: 0,
            out var toAdd, out var toRemove);

        Assert.Equal(RouteIslandLabelRenderer.RebuildOutcome.Deferred, outcome);
        Assert.Empty(toAdd);
        Assert.Empty(toRemove);
    }

    [Fact]
    public void Reconcile_HostBecomesValid_AfterDeferred_ProducesAllLabels() {
        // Audit round 5 fixture: the first call lands with size 0
        // (Deferred), the second call after layout passes produces
        // every planned label.
        var planned = new[] { "Iliya", "Baremi", "Crow", "Rickun",
            "Cox_Pirate", "Grandiha", "Midnight", "Sausan", "Sanctuary" };
        var existing = new HashSet<string>(StringComparer.Ordinal);

        var firstOutcome = RouteIslandLabelRenderer.Reconcile(
            planned, existing, hostWidth: 0, hostHeight: 0,
            out _, out _);
        Assert.Equal(RouteIslandLabelRenderer.RebuildOutcome.Deferred, firstOutcome);

        var secondOutcome = RouteIslandLabelRenderer.Reconcile(
            planned, existing, hostWidth: 1280, hostHeight: 720,
            out var toAdd, out var toRemove);

        Assert.Equal(RouteIslandLabelRenderer.RebuildOutcome.Built, secondOutcome);
        Assert.Equal(planned.Length, toAdd.Count);
        Assert.Empty(toRemove);
        // The 10-step regression fixture: every island in the
        // visible set must be created.
        foreach (var id in planned) Assert.Contains(id, toAdd);
    }

    [Fact]
    public void Reconcile_ExcludesWarehouseIslands_FromPlanSet() {
        // The audit's "no double label" rule: a warehouse island
        // gets its label from EnsureAutomaticWarehouseNodes, the
        // route-only label pass must skip it.
        var visible = new[] { "Iliya", "Baremi", "Crow" };
        var warehouseIds = new HashSet<string>(StringComparer.Ordinal) { "Iliya" };

        var planned = RouteIslandLabelRenderer.PlanLabels(visible, warehouseIds);

        Assert.DoesNotContain("Iliya", planned);
        Assert.Contains("Baremi", planned);
        Assert.Contains("Crow", planned);
    }

    [Fact]
    public void Reconcile_RouteSwitch_RemovesOldAndAddsNew() {
        // SelectedRoute=1 visible: {Baremi}; SelectedRoute=2 visible: {Crow}.
        // Existing set has Baremi. Switching to route 2 should drop
        // Baremi and add Crow.
        var existing = new HashSet<string>(StringComparer.Ordinal) { "Baremi" };
        var newPlanned = new[] { "Crow" };

        var outcome = RouteIslandLabelRenderer.Reconcile(
            newPlanned, existing, hostWidth: 1280, hostHeight: 720,
            out var toAdd, out var toRemove);

        Assert.Equal(RouteIslandLabelRenderer.RebuildOutcome.Built, outcome);
        Assert.Equal(new[] { "Baremi" }, toRemove);
        Assert.Equal(new[] { "Crow" }, toAdd);
    }

    [Fact]
    public void Reconcile_NoChange_NoOp() {
        // Idempotent: same input on the second call must report
        // Empty so the WPF layer does not redundantly remove and
        // re-add the same labels.
        var planned = new[] { "Baremi", "Crow" };
        var existing = new HashSet<string>(StringComparer.Ordinal) { "Baremi", "Crow" };

        var outcome = RouteIslandLabelRenderer.Reconcile(
            planned, existing, hostWidth: 1280, hostHeight: 720,
            out var toAdd, out var toRemove);

        Assert.Equal(RouteIslandLabelRenderer.RebuildOutcome.Empty, outcome);
        Assert.Empty(toAdd);
        Assert.Empty(toRemove);
    }

    [Fact]
    public void Reconcile_EmptyPlan_DropsExistingLabels() {
        // Exiting AutomaticRoute mode (or route became empty):
        // every existing route-only label must be removed.
        var planned = Array.Empty<string>();
        var existing = new HashSet<string>(StringComparer.Ordinal) { "Baremi", "Crow" };

        var outcome = RouteIslandLabelRenderer.Reconcile(
            planned, existing, hostWidth: 1280, hostHeight: 720,
            out var toAdd, out var toRemove);

        Assert.Equal(RouteIslandLabelRenderer.RebuildOutcome.Built, outcome);
        Assert.Empty(toAdd);
        Assert.Equal(2, toRemove.Count);
        Assert.Contains("Baremi", toRemove);
        Assert.Contains("Crow", toRemove);
    }

    [Fact]
    public void Reconcile_OnlyWidthIsZero_ReturnsDeferred() {
        // Edge case: only one dimension is zero. The renderer
        // must still defer — both must be > 0 for the layout
        // calculations to make sense.
        var planned = new[] { "Baremi" };
        var existing = new HashSet<string>(StringComparer.Ordinal);

        var outcome = RouteIslandLabelRenderer.Reconcile(
            planned, existing, hostWidth: 1280, hostHeight: 0,
            out var toAdd, out var toRemove);

        Assert.Equal(RouteIslandLabelRenderer.RebuildOutcome.Deferred, outcome);
        Assert.Empty(toAdd);
        Assert.Empty(toRemove);
    }

    [Fact]
    public void Reconcile_RealisticTenStepFixture_AllNineIslands() {
        // Audit's example: Iliya pickup + 8 barters + Iliya unload.
        // 9 unique islands. Iliya is a warehouse and is filtered
        // out by the warehouse dedup pass.
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

        var visible = RouteIslandLabelPlanner.VisibleIslandIds(
            plan, showAll: false, selectedRouteNumber: 1);
        // 9 unique islands (Iliya appears 3 times, deduped to 1).
        Assert.Equal(9, visible.Count);

        // Warehouse dedup strips Iliya; the renderer must add the
        // remaining 8 islands.
        var warehouseIds = new HashSet<string>(StringComparer.Ordinal) { "Iliya" };
        var planned = RouteIslandLabelRenderer.PlanLabels(visible, warehouseIds);
        Assert.Equal(8, planned.Count);
        Assert.DoesNotContain("Iliya", planned);
        Assert.Contains("Baremi", planned);
        Assert.Contains("Crow", planned);
        Assert.Contains("Rickun", planned);
        Assert.Contains("Cox_Pirate", planned);
        Assert.Contains("Grandiha", planned);
        Assert.Contains("Midnight", planned);
        Assert.Contains("Sausan", planned);
        Assert.Contains("Sanctuary", planned);

        // After layout pass: every planned island is added to the
        // visual tree on the first call with a valid host size.
        var existing = new HashSet<string>(StringComparer.Ordinal);
        var outcome = RouteIslandLabelRenderer.Reconcile(
            planned, existing, hostWidth: 1280, hostHeight: 720,
            out var toAdd, out var toRemove);

        Assert.Equal(RouteIslandLabelRenderer.RebuildOutcome.Built, outcome);
        Assert.Equal(8, toAdd.Count);
        Assert.Empty(toRemove);
    }

    [Fact]
    public void Reconcile_IslandBaseLabels_NeverAuthorizeBarterCompletion() {
        // Audit round 3 contract: a route-only island base label
        // carries no BarterRowId, no CanMarkDone, no StepKind. It
        // is purely a display element. The renderer's decision
        // output (toAdd / toRemove) must not include any field
        // that could be interpreted as completion authority.
        var planned = new[] { "Baremi" };
        var existing = new HashSet<string>(StringComparer.Ordinal);

        var outcome = RouteIslandLabelRenderer.Reconcile(
            planned, existing, hostWidth: 1280, hostHeight: 720,
            out var toAdd, out var toRemove);

        // The renderer output is purely an id list. The WPF layer
        // is responsible for NOT binding completion handlers to
        // these wrappers (verified by RouteMapNodeTests).
        Assert.Equal(RouteIslandLabelRenderer.RebuildOutcome.Built, outcome);
        Assert.Equal(new[] { "Baremi" }, toAdd);
        Assert.Empty(toRemove);
        // No field, no StepKind, no CanMarkDone — they live in
        // RouteMapNode, not in the renderer. This test only
        // asserts the renderer's surface is data-only.
    }
}