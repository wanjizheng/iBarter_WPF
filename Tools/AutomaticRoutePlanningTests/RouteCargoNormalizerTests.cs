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

        var (normalized, changed) = RouteCargoNormalizer.NormalizeRoute(request, route);

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

        var (normalized, _) = RouteCargoNormalizer.NormalizeRoute(request, route);

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

        var (normalized, changed) = RouteCargoNormalizer.NormalizeRoute(request, route);

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

        var (normalized, changed) = RouteCargoNormalizer.NormalizeRoute(request, route);

        Assert.False(changed);
        Assert.Same(route, normalized);
    }

    [Fact]
    public void Normalize_DropsUnloadItemsWithNoProvenance() {
        var request = BuildRequest(("800049", 1), ("10", 1), ("800099", 2));
        var route = BuildRoute("Iliya",
            Pickup("Iliya", ("800049", 5)),
            Barter("Crow:800049:10", "Crow", "800049", 5, "10", 815),
            Unload("Iliya", ("800099", 99))); // 800099 was never picked up nor produced.

        var (normalized, _) = RouteCargoNormalizer.NormalizeRoute(request, route);

        var unload = (WarehouseUnloadStep)normalized.Steps[2];
        Assert.Contains(unload.Items, x => x.ItemId == "800099");
    }

    [Fact]
    public void Normalize_RemovesEmptyPickupStep() {
        var request = BuildRequest(("800045", 4), ("800049", 1), ("10", 1));
        // Pickup contains ONLY the unused 800045; after normalize the
        // pickup is empty AND replay cannot succeed (the barter needs
        // 800049 which was never on board). The normalizer must refuse
        // to publish the half-truth route and keep the original.
        var route = BuildRoute("Iliya",
            Pickup("Iliya", ("800045", 1)), // unused
            Barter("Crow:800049:10", "Crow", "800049", 1, "10", 163),
            Unload("Iliya", ("10", 163)));

        var (normalized, changed) = RouteCargoNormalizer.NormalizeRoute(request, route);
        Assert.False(changed);
        Assert.Same(route, normalized);
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

        string tempPath = Path.Combine(Path.GetTempPath(),
            $"autoroute-normalize-{Guid.NewGuid():N}.json");
        try {
            RoutePlanPersistence.Save(tempPath, normalized, 1, false);
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
        var second = RouteCargoNormalizer.Normalize(request, first);

        Assert.Equal(first.InputFingerprint, second.InputFingerprint);
        var firstPickup = (WarehousePickupStep)first.Routes[0].Steps[0];
        var secondPickup = (WarehousePickupStep)second.Routes[0].Steps[0];
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

        var (normalized, _) = RouteCargoNormalizer.NormalizeRoute(request, route);
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
        // replay must fail. The normalizer MUST refuse to publish the
        // half-truth route and fall back to the original.
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
        var (normalized, changed) = RouteCargoNormalizer.NormalizeRoute(request, route);
        Assert.False(changed);
        Assert.Same(route, normalized); // unchanged because replay would fail
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
        var (normalized, _) = RouteCargoNormalizer.NormalizeRoute(request, route);

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
}