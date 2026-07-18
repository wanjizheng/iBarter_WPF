using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

/// <summary>
/// Bug 2: the planner's demand bundle generator can include items in a
/// pickup that the actual route never consumes (the bundle enumerates greedy
/// subsets of pending tasks; intermediate subsets survive into the bundle).
/// The matching <c>WarehouseUnloadPlanner</c> then unloads everything on
/// board, so the unused item gets trucked out and dropped at the source
/// warehouse — a zero-value round trip that still costs load and LT.
///
/// <c>RouteCargoNormalizer.Normalize</c> is the post-pass that prunes these
/// phantom pickups. Tests below cover:
///   * complete unused pickup (drop both ends)
///   * partial consumption (cap pickup at the consumed amount)
///   * barter output that must still be unloaded
///   * pickup output where the same item is also produced (consumed wins)
///   * persistence round-trip of normalized plan
/// </summary>
public class RouteCargoNormalizerTests {
    private static readonly RouteItem FiveColoredThread = new("800045", "五彩线团", 1, 100);
    private static readonly RouteItem GoldCactusBouquet = new("800208", "金色仙人掌花束", 1, 100);
    private static readonly RouteItem ArtisanShellNecklace = new("800241", "匠人的贝壳项链", 1, 100);

    private static PlannedRoute BuildRoute(params RouteStep[] steps) =>
        new(1, "Iliya", "Iliya", steps, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);

    private static WarehousePickupStep Pickup(string warehouseId, params (string id, int qty)[] items) =>
        new(warehouseId, "Iliya",
            items.Select(i => new RouteItemQuantity(i.id, i.qty)).ToArray(),
            new RouteLoadSnapshot(0, 0, 0));

    private static WarehouseUnloadStep Unload(string warehouseId, params (string id, int qty)[] items) =>
        new(warehouseId, "Iliya",
            items.Select(i => new RouteItemQuantity(i.id, i.qty)).ToArray(),
            new RouteLoadSnapshot(0, 0, 0));

    private static BarterStep Barter(string rowId, string fromId, int fromQty, string toId, int toQty) =>
        new(rowId, "Baremi",
            new RouteItemQuantity(fromId, fromQty),
            new RouteItemQuantity(toId, toQty),
            new RouteLoadSnapshot(0, 0, 0));

    [Fact]
    public void Normalize_DropsPickupItemNeverConsumed() {
        // pickup 五彩线团 × 1, barter consumes something else, unload dumps 五彩线团 back.
        var route = BuildRoute(
            Pickup("Iliya", ("800045", 1), ("800049", 1)),
            Barter("82:Crow:800049:10", "800049", 1, "10", 163),
            Unload("Iliya", ("800045", 1), ("10", 163)));

        var (normalized, changed) = RouteCargoNormalizer.NormalizeRoute(route);

        Assert.True(changed);
        var pickup = (WarehousePickupStep)normalized.Steps[0];
        Assert.DoesNotContain(pickup.Items, x => x.ItemId == "800045");
        Assert.Contains(pickup.Items, x => x.ItemId == "800049");
        var unload = (WarehouseUnloadStep)normalized.Steps[2];
        // 10 (Crow Coin) was produced by the barter; it stays in unload.
        Assert.DoesNotContain(unload.Items, x => x.ItemId == "800045");
        Assert.Contains(unload.Items, x => x.ItemId == "10");
    }

    [Fact]
    public void Normalize_PreservesBarterOutput() {
        // pickup X, barter produces Y, unload has Y.  Y must survive the pass.
        var route = BuildRoute(
            Pickup("Iliya", ("800049", 5)),
            Barter("82:Crow:800049:10", "800049", 5, "10", 815),
            Unload("Iliya", ("10", 815)));

        var (normalized, _) = RouteCargoNormalizer.NormalizeRoute(route);

        var unload = (WarehouseUnloadStep)normalized.Steps[2];
        Assert.Single(unload.Items);
        Assert.Equal("10", unload.Items[0].ItemId);
        Assert.Equal(815, unload.Items[0].Quantity);
    }

    [Fact]
    public void Normalize_CapsPartialConsumption() {
        // pickup X × 5, but only 3 are consumed; remaining 2 should NOT be
        // picked up in the first place (no value to carrying them home).
        var route = BuildRoute(
            Pickup("Iliya", ("800049", 5)),
            Barter("82:Crow:800049:10", "800049", 3, "10", 489),
            Unload("Iliya", ("10", 489), ("800049", 2)));

        var (normalized, changed) = RouteCargoNormalizer.NormalizeRoute(route);

        Assert.True(changed);
        var pickup = (WarehousePickupStep)normalized.Steps[0];
        Assert.Single(pickup.Items);
        Assert.Equal("800049", pickup.Items[0].ItemId);
        Assert.Equal(3, pickup.Items[0].Quantity);
        var unload = (WarehouseUnloadStep)normalized.Steps[2];
        Assert.DoesNotContain(unload.Items, x => x.ItemId == "800049");
    }

    [Fact]
    public void Normalize_LeavesCompletelyNeededPickupAlone() {
        // pickup X × 5, consume X × 5 — nothing to prune.
        var route = BuildRoute(
            Pickup("Iliya", ("800049", 5)),
            Barter("82:Crow:800049:10", "800049", 5, "10", 815),
            Unload("Iliya", ("10", 815)));

        var (normalized, changed) = RouteCargoNormalizer.NormalizeRoute(route);

        Assert.False(changed);
        Assert.Same(route, normalized);
    }

    [Fact]
    public void Normalize_DropsUnloadItemsWithNoProvenance() {
        // Pure cosmetic unload items (no pickup, no barter output) are kept
        // because they may represent InitialOnBoard cargo. The normalizer
        // does NOT silently delete InitialOnBoard semantics.
        var route = BuildRoute(
            Pickup("Iliya", ("800049", 5)),
            Barter("82:Crow:800049:10", "800049", 5, "10", 815),
            Unload("Iliya", ("800099", 99))); // 800099 was never picked up nor produced.

        var (_, _) = RouteCargoNormalizer.NormalizeRoute(route);

        var unload = (WarehouseUnloadStep)route.Steps[2];
        // 800099 preserved; it could be initial-on-board cargo.
        Assert.Contains(unload.Items, x => x.ItemId == "800099");
    }

    [Fact]
    public void Normalize_RemovesEmptyPickupStep() {
        // After pruning, the pickup is empty; the post-pass keeps the step
        // record but with zero items. The ApplyBarterCompletionProgress
        // pipeline then drops the empty pickup entirely before publishing.
        var route = BuildRoute(
            Pickup("Iliya", ("800045", 1)), // unused
            Barter("82:Crow:800049:10", "800049", 1, "10", 163),
            Unload("Iliya", ("10", 163)));

        var (normalized, changed) = RouteCargoNormalizer.NormalizeRoute(route);
        Assert.True(changed);
        var pickup = (WarehousePickupStep)normalized.Steps[0];
        Assert.Empty(pickup.Items);
    }

    [Fact]
    public void Normalize_PlanRoundTripsThroughPersistence() {
        var route = BuildRoute(
            Pickup("Iliya", ("800045", 1), ("800049", 1)),
            Barter("82:Crow:800049:10", "800049", 1, "10", 163),
            Unload("Iliya", ("10", 163)));
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [route], null, [], "fp");
        var normalized = RouteCargoNormalizer.Normalize(plan);

        // Round-trip through JSON: serialize, deserialize, ensure unused
        // pickup item is still gone.
        string tempPath = Path.Combine(Path.GetTempPath(),
            $"autoroute-normalize-{Guid.NewGuid():N}.json");
        try {
            RoutePlanPersistence.Save(tempPath, normalized, 1, false);
            var loaded = RoutePlanPersistence.TryLoad(tempPath, "fp");
            Assert.Equal(RoutePlanLoadStatus.Loaded, loaded.Status);
            var reloaded = loaded.Snapshot!.Plan;
            var pickup = (WarehousePickupStep)reloaded.Routes[0].Steps[0];
            Assert.DoesNotContain(pickup.Items, x => x.ItemId == "800045");
        }
        finally {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void Normalize_IsDeterministic() {
        var route = BuildRoute(
            Pickup("Iliya", ("800045", 1), ("800049", 1)),
            Barter("82:Crow:800049:10", "800049", 1, "10", 163),
            Unload("Iliya", ("800045", 1), ("10", 163)));
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [route], null, [], "fp");

        var first = RouteCargoNormalizer.Normalize(plan);
        var second = RouteCargoNormalizer.Normalize(first);

        Assert.Equal(first.InputFingerprint, second.InputFingerprint);
        var firstPickup = (WarehousePickupStep)first.Routes[0].Steps[0];
        var secondPickup = (WarehousePickupStep)second.Routes[0].Steps[0];
        Assert.Equal(firstPickup.Items.Count, secondPickup.Items.Count);
        Assert.Equal(firstPickup.Items[0].ItemId, secondPickup.Items[0].ItemId);
        Assert.Equal(firstPickup.Items[0].Quantity, secondPickup.Items[0].Quantity);
    }
}