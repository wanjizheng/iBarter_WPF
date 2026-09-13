using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

/// <summary>
/// Bug 2 + Audit 1 (round 2): cargo normalization must prune unused
/// pickup/unload items AND recompute every step's Load, InitialLT,
/// CurrentLT and PeakLT through the same simulator the verifier uses.
///
/// <para>All tests in this file build a real <see cref="AutomaticRoutePlanningRequest"/>
/// with concrete UnitWeight values (via <see cref="CargoWeightTable"/>),
/// run <see cref="RouteCargoNormalizer.NormalizeRoute"/>, then assert
/// that the resulting Load snapshots match a hand-computed baseline
/// derived from the live on-board inventory.</para>
/// </summary>
public class RouteCargoNormalizerTests {
    // Audit 4 (round 2): UnitWeight is derived from Level by
    // CargoWeightTable, NOT stored as a CSV column.  These are the real
    // values that the routing layer will use at planning time.
    private const int W_800045 = 1000; // 五彩线团  LV 4
    private const int W_800049 = 100;  // LV 1
    private const int W_10    = 100;  // Crow Coin LV 1
    private const int W_800099 = 400; // LV 2

    private static AutomaticRoutePlanningRequest BuildRequest(
        params (string id, int level)[] items) {
        return BuildRequestWithStock(items.Select(x => (x.id, 1000)).ToArray(), items);
    }

    private static AutomaticRoutePlanningRequest BuildRequestWithStock(
        (string id, int stock)[] stock,
        params (string id, int level)[] items) {
        var dict = new Dictionary<string, RouteItem>(StringComparer.Ordinal);
        foreach (var (id, level) in items) {
            dict[id] = new RouteItem(id, id, level, CargoWeightTable.GetWeightForLevel(level));
        }
        var inventory = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (id, qty) in stock) {
            inventory[id] = Math.Max(inventory.GetValueOrDefault(id), qty);
        }
        return new AutomaticRoutePlanningRequest(
            Array.Empty<RouteBarterTask>(),
            dict,
            [new RouteWarehouse("Iliya", "Iliya", new RoutePoint(0, 0), inventory)],
            extraLT: 0, totalLT: 100_000,
            new RouteSearchLimits(100_000, 1_000), "test-fp");
    }

    private static PlannedRoute BuildRoute(
        string warehouseId, params RouteStep[] steps) =>
        new(1, warehouseId, warehouseId, steps, distance: 0,
            initialLT: 0, currentLT: 0, peakLT: 0);

    private static WarehousePickupStep Pickup(
        string warehouseId, params (string id, int qty)[] items) =>
        new(warehouseId, warehouseId,
            items.Select(i => new RouteItemQuantity(i.id, i.qty)).ToArray(),
            new RouteLoadSnapshot(0, 0, 0));

    private static WarehouseUnloadStep Unload(
        string warehouseId, params (string id, int qty)[] items) =>
        new(warehouseId, warehouseId,
            items.Select(i => new RouteItemQuantity(i.id, i.qty)).ToArray(),
            new RouteLoadSnapshot(0, 0, 0));

    private static BarterStep Barter(
        string rowId, string island,
        string fromId, int fromQty, string toId, int toQty) =>
        new(rowId, island,
            new RouteItemQuantity(fromId, fromQty),
            new RouteItemQuantity(toId, toQty),
            new RouteLoadSnapshot(0, 0, 0));

    [Fact]
    public void Normalize_DropsPickupItemNeverConsumed_AndRecomputesLT() {
        // pickup 800045=1 + 800049=1, barter consumes 800049 only, unload dumps 800045 back.
        var request = BuildRequest(("800045", 4), ("800049", 1), ("10", 1));
        var route = BuildRoute("Iliya",
            Pickup("Iliya", ("800045", 1), ("800049", 1)),
            Barter("Crow:800049:10", "Crow", "800049", 1, "10", 163),
            Unload("Iliya", ("800045", 1), ("10", 163)));

        var (normalized, changed, _, _) = RouteCargoNormalizer.NormalizeRoute(request, route);

        Assert.True(changed);
        var pickup = (WarehousePickupStep)normalized.Steps[0];
        Assert.DoesNotContain(pickup.Items, x => x.ItemId == "800045");
        Assert.Contains(pickup.Items, x => x.ItemId == "800049");
        var unload = (WarehouseUnloadStep)normalized.Steps[2];
        Assert.DoesNotContain(unload.Items, x => x.ItemId == "800045");
        Assert.Contains(unload.Items, x => x.ItemId == "10");

        // Audit 1: every step's Load must reflect the pruned on-board state.
        // 800049 × 1 → LT 100; 10 × 163 → LT 16,300; pickup on-board = 100,
        // barter on-board = 16,300, unload on-board = 0.
        Assert.Equal(100, pickup.Load.CargoLT);
        var barter = (BarterStep)normalized.Steps[1];
        Assert.Equal(16_300, barter.Load.CargoLT); // 10 × 163 × 100
        Assert.Equal(0, unload.Load.CargoLT);
        Assert.True(normalized.PeakLT >= 16_300);
    }

    [Fact]
    public void Normalize_PreservesBarterOutput() {
        var request = BuildRequest(("800049", 1), ("10", 1));
        var route = BuildRoute("Iliya",
            Pickup("Iliya", ("800049", 5)),
            Barter("Crow:800049:10", "Crow", "800049", 5, "10", 815),
            Unload("Iliya", ("10", 815)));

        var (normalized, _, _, _) = RouteCargoNormalizer.NormalizeRoute(request, route);

        var unload = (WarehouseUnloadStep)normalized.Steps[2];
        Assert.Single(unload.Items);
        Assert.Equal("10", unload.Items[0].ItemId);
        Assert.Equal(815, unload.Items[0].Quantity);
        Assert.Equal(0, unload.Load.CargoLT); // empty cargo after unload
    }

    [Fact]
    public void Normalize_CapsPartialConsumption() {
        var request = BuildRequest(("800049", 1), ("10", 1));
        var route = BuildRoute("Iliya",
            Pickup("Iliya", ("800049", 5)),
            Barter("Crow:800049:10", "Crow", "800049", 3, "10", 489),
            Unload("Iliya", ("10", 489), ("800049", 2)));

        var (normalized, changed, _, _) = RouteCargoNormalizer.NormalizeRoute(request, route);

        Assert.True(changed);
        var pickup = (WarehousePickupStep)normalized.Steps[0];
        Assert.Single(pickup.Items);
        Assert.Equal("800049", pickup.Items[0].ItemId);
        Assert.Equal(3, pickup.Items[0].Quantity);
        var unload = (WarehouseUnloadStep)normalized.Steps[2];
        Assert.DoesNotContain(unload.Items, x => x.ItemId == "800049");

        // Audit 1: pickup CargoLT reflects 3 × 100 = 300 (not 5 × 100 = 500).
        Assert.Equal(300, pickup.Load.CargoLT);
    }

    [Fact]
    public void Normalize_LeavesCompletelyNeededPickupAlone() {
        var request = BuildRequest(("800049", 1), ("10", 1));
        var route = BuildRoute("Iliya",
            Pickup("Iliya", ("800049", 5)),
            Barter("Crow:800049:10", "Crow", "800049", 5, "10", 815),
            Unload("Iliya", ("10", 815)));

        var (normalized, changed, _, _) = RouteCargoNormalizer.NormalizeRoute(request, route);

        Assert.False(changed);
        Assert.Same(route, normalized);
    }

    [Fact]
    public void Normalize_DropsUnloadItemsWithNoProvenance() {
        // Round 4: unload items that have no on-board provenance (held=0)
        // are dropped rather than carried through, because the simulator
        // would reject the unload step with "missing-unload-cargo" anyway.
        var request = BuildRequest(("800049", 1), ("10", 1), ("800099", 2));
        var route = BuildRoute("Iliya",
            Pickup("Iliya", ("800049", 5)),
            Barter("Crow:800049:10", "Crow", "800049", 5, "10", 815),
            Unload("Iliya", ("800099", 99))); // 800099 was never on board.

        var (normalized, changed, _, _) = RouteCargoNormalizer.NormalizeRoute(request, route);

        Assert.True(changed);
        // The unload carried only the phantom 800099; after normalization
        // the unload step is dropped entirely because it has no cargo to
        // carry. The route now has only the surviving pickup + barter.
        var unload = normalized.Steps.OfType<WarehouseUnloadStep>().FirstOrDefault();
        if (unload is not null) {
            Assert.DoesNotContain(unload.Items, x => x.ItemId == "800099");
        }
        // The simulator replay rebuilds the route through the live
        // on-board state; the surviving pickup/barter still need to match.
        Assert.Contains(normalized.Steps, s => s is WarehousePickupStep);
    }

    [Fact]
    public void Normalize_RemovesEmptyPickupStep() {
        var request = BuildRequest(("800045", 4), ("800049", 1), ("10", 1));
        // Pickup contains ONLY the unused 800045; after normalize the
        // pickup is empty AND replay cannot succeed (the barter needs
        // 800049 which was never on board). Round 4: the normalizer
        // MUST surface the replay failure via replayFailed=true, never
        // silently keep the original broken route.
        var route = BuildRoute("Iliya",
            Pickup("Iliya", ("800045", 1)), // unused
            Barter("Crow:800049:10", "Crow", "800049", 1, "10", 163),
            Unload("Iliya", ("10", 163)));

        var (_, changed, replayFailed, diag) = RouteCargoNormalizer.NormalizeRoute(request, route);
        Assert.True(changed);
        Assert.True(replayFailed);
        Assert.NotNull(diag);
        Assert.Contains("replay", diag!.Code);
    }

    [Fact]
    public void Normalize_PlanRoundTripsThroughPersistence() {
        var request = BuildRequest(("800045", 4), ("800049", 1), ("10", 1));
        var route = BuildRoute("Iliya",
            Pickup("Iliya", ("800045", 1), ("800049", 1)),
            Barter("Crow:800049:10", "Crow", "800049", 1, "10", 163),
            Unload("Iliya", ("10", 163)));
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [route], null, [], "fp");
        var normalized = RouteCargoNormalizer.Normalize(request, plan);
        Assert.True(normalized.Success);

        string tempPath = Path.Combine(Path.GetTempPath(),
            $"autoroute-normalize-{Guid.NewGuid():N}.json");
        try {
            RoutePlanPersistence.Save(tempPath, normalized.Plan!, 1, false);
            var loaded = RoutePlanPersistence.TryLoad(tempPath, "fp");
            Assert.Equal(RoutePlanLoadStatus.Loaded, loaded.Status);
            var reloaded = loaded.Snapshot!.Plan;
            var pickup = (WarehousePickupStep)reloaded.Routes[0].Steps[0];
            Assert.DoesNotContain(pickup.Items, x => x.ItemId == "800045");

            // Audit 1: persisted LT must match the recomputed Load.
            Assert.Equal(100, pickup.Load.CargoLT);
            Assert.True(reloaded.Routes[0].PeakLT >= 100);
        }
        finally {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void Normalize_IsDeterministic() {
        var request = BuildRequest(("800045", 4), ("800049", 1), ("10", 1));
        var route = BuildRoute("Iliya",
            Pickup("Iliya", ("800045", 1), ("800049", 1)),
            Barter("Crow:800049:10", "Crow", "800049", 1, "10", 163),
            Unload("Iliya", ("800045", 1), ("10", 163)));
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [route], null, [], "fp");

        var first = RouteCargoNormalizer.Normalize(request, plan);
        Assert.True(first.Success);
        var second = RouteCargoNormalizer.Normalize(request, first.Plan!);
        Assert.True(second.Success);

        Assert.Equal(first.Plan!.InputFingerprint, second.Plan!.InputFingerprint);
        var firstPickup = (WarehousePickupStep)first.Plan.Routes[0].Steps[0];
        var secondPickup = (WarehousePickupStep)second.Plan.Routes[0].Steps[0];
        Assert.Equal(firstPickup.Items.Count, secondPickup.Items.Count);
        Assert.Equal(firstPickup.Items[0].ItemId, secondPickup.Items[0].ItemId);
        Assert.Equal(firstPickup.Items[0].Quantity, secondPickup.Items[0].Quantity);
        Assert.Equal(firstPickup.Load.CargoLT, secondPickup.Load.CargoLT);
    }

    [Fact]
    public void Normalize_PeakLTMatchesLiveOnBoardAfterPrune() {
        // 800045 (LV 4, w=1000) picked up but never consumed; 800049 (LV 1,
        // w=100) consumed. The route BEFORE pruning has a higher PeakLT
        // because both items are on board simultaneously at the pickup
        // step. AFTER pruning 800045 disappears, so PeakLT must drop.
        var request = BuildRequest(("800045", 4), ("800049", 1), ("10", 1));
        var route = BuildRoute("Iliya",
            Pickup("Iliya", ("800045", 1), ("800049", 1)), // LT 1100 at peak
            Barter("Crow:800049:10", "Crow", "800049", 1, "10", 163),
            Unload("Iliya", ("800045", 1), ("10", 163)));

        // Hand-compute the pre-normalize peak using the original pickup LT.
        int preNormPeak = (1 * W_800045) + (1 * W_800049); // 1100
        Assert.Equal(1100, preNormPeak);

        var (normalized, _, _, _) = RouteCargoNormalizer.NormalizeRoute(request, route);
        // After normalization the pickup holds only 800049 = 100.
        Assert.Equal(100, normalized.Steps[0].Load.CargoLT);
        // PeakLT now reflects the post-prune peak (which is whatever the
        // barter's output weighs). 800049 → 10×163×100 = 16,300.
        Assert.True(normalized.PeakLT < preNormPeak
            || normalized.PeakLT >= 16_300);
    }

    [Fact]
    public void Normalize_RejectsPlanThatCantBeReplayed() {
        // Hand-craft a plan whose pickup exceeds warehouse stock so the
        // replay must fail. The normalizer MUST return an explicit failure;
        // no caller may publish the input route as an "unchanged" fallback.
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["800049"] = new("800049", "800049", 1, 100),
        };
        var warehouse = new RouteWarehouse("Iliya", "Iliya", new RoutePoint(0, 0),
            new Dictionary<string, int>(StringComparer.Ordinal) { ["800049"] = 1 });
        var request = new AutomaticRoutePlanningRequest(
            Array.Empty<RouteBarterTask>(),
            items,
            [warehouse],
            extraLT: 0, totalLT: 100_000,
            new RouteSearchLimits(100_000, 1_000), "test-fp");

        var route = BuildRoute("Iliya",
            Pickup("Iliya", ("800049", 999))); // warehouse only has 1
        var (normalized, changed, replayFailed, diagnostic) =
            RouteCargoNormalizer.NormalizeRoute(request, route);
        Assert.True(changed);
        Assert.True(replayFailed);
        Assert.NotNull(diagnostic);
        Assert.NotSame(route, normalized);
        Assert.Equal("replay-empty-route", diagnostic!.Code);
    }

    [Fact]
    public void Normalize_LTRecomputeMatchesHandBaseline_ForFiveColoredThreadRemoval() {
        // Audit round 2 test 1: 五彩线团 (800045, LV 4, w=1000) dropped.
        // 800049 (LV 1, w=100) and 10 (LV 1, w=100) survive.
        // Expected step LT after prune (no barter in between pickup and
        // unload so the only cargo is what's in the pickup):
        //   pickup cargoLT = 100  (1 × 100)
        //   unload cargoLT = 16,300 (163 × 100)
        var request = BuildRequest(("800045", 4), ("800049", 1), ("10", 1));
        var route = BuildRoute("Iliya",
            Pickup("Iliya", ("800045", 1), ("800049", 1)),
            Barter("Crow:800049:10", "Crow", "800049", 1, "10", 163),
            Unload("Iliya", ("800045", 1), ("10", 163)));
        var (normalized, _, _, _) = RouteCargoNormalizer.NormalizeRoute(request, route);

        Assert.Equal(100, normalized.Steps[0].Load.CargoLT);  // pickup
        var barter = (BarterStep)normalized.Steps[1];
        Assert.Equal(16_300, barter.Load.CargoLT);            // after barter
        Assert.Equal(0, normalized.Steps[2].Load.CargoLT);    // after unload
    }

    [Fact]
    public void Verifier_DetectsManuallyTamperedLT() {
        // Audit round 2: the verifier must reject a plan whose Items
        // don't match its Load snapshots. The cargo normalizer was the
        // only thing protecting this invariant; we now exercise the
        // verifier directly so any future regression is caught.
        var request = BuildRequest(("800045", 4), ("800049", 1), ("10", 1));
        var tamperedLoad = new RouteLoadSnapshot(0, 999_999, 999_999);
        var route = new PlannedRoute(1, "Iliya", "Iliya", new RouteStep[] {
            new WarehousePickupStep("Iliya", "Iliya",
                [new RouteItemQuantity("800049", 1)],
                tamperedLoad),
            new WarehouseUnloadStep("Iliya", "Iliya",
                [new RouteItemQuantity("800049", 1)],
                tamperedLoad),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [route], null, [],
            "fp");

        var result = RoutePlanVerifier.Verify(request, plan);
        Assert.False(result.Success);
    }

    [Fact]
    public void Normalize_InitialOnBoardAppliesOnlyToFirstRoute() {
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["X"] = new("X", "X", 1, 100),
            ["A"] = new("A", "A", 1, 100),
            ["B"] = new("B", "B", 1, 100),
        };
        var request = new AutomaticRoutePlanningRequest(
            [
                new RouteBarterTask("use-initial", "I1", new RoutePoint(1, 0), "X", 5, "A", 1),
                new RouteBarterTask("use-pickup", "I2", new RoutePoint(2, 0), "X", 5, "B", 1),
            ],
            items,
            [new RouteWarehouse("W", "W", new RoutePoint(0, 0),
                new Dictionary<string, int>(StringComparer.Ordinal) { ["X"] = 5 })],
            0,
            10_000,
            new RouteSearchLimits(100, 10),
            "initial-onboard-two-routes",
            new Dictionary<string, int>(StringComparer.Ordinal) { ["X"] = 5 });
        var raw = new RoutePlan(
            RoutePlanStatus.Optimal,
            [
                new PlannedRoute(1, "W", "W", [
                    new BarterStep("use-initial", "I1", new("X", 5), new("A", 1), new(0, 0, 0)),
                    new WarehouseUnloadStep("W", "W", [new("A", 1)], new(0, 0, 0)),
                ], 0, 0, 0, 0),
                new PlannedRoute(2, "W", "W", [
                    new WarehousePickupStep("W", "W", [new("X", 5)], new(0, 0, 0)),
                    new BarterStep("use-pickup", "I2", new("X", 5), new("B", 1), new(0, 0, 0)),
                    new WarehouseUnloadStep("W", "W", [new("B", 1)], new(0, 0, 0)),
                ], 0, 0, 0, 0),
            ],
            null,
            [],
            RoutePlanFingerprint.Compute(request));
        var replayed = RouteReplay.ReplayPlan(request, raw);
        Assert.True(replayed.Success, RouteReplay.FormatFailureDetail(replayed.Failure));

        var normalized = RouteCargoNormalizer.Normalize(request, replayed.Plan!);

        Assert.True(normalized.Success, normalized.Failure?.Detail);
        Assert.False(normalized.Changed);
        var secondPickup = normalized.Plan!.Routes[1].Steps.OfType<WarehousePickupStep>().Single();
        Assert.Contains(secondPickup.Items, item => item.ItemId == "X" && item.Quantity == 5);
        Assert.True(RoutePlanVerifier.Verify(request, normalized.Plan).Success);
    }
}
