using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public class RouteReplayTests {
    [Fact]
    public void Replay_RoundTripPreservesItems() {
        var request = BuildRequest(("800045", 4), ("800049", 1), ("10", 1));
        var route = new PlannedRoute(1, "Iliya", "Iliya", new RouteStep[] {
            new WarehousePickupStep("Iliya", "Iliya",
                [new RouteItemQuantity("800049", 1)],
                new RouteLoadSnapshot(0, 0, 0)),
            new BarterStep("Crow:800049:10", "Crow",
                new RouteItemQuantity("800049", 1),
                new RouteItemQuantity("10", 163),
                new RouteLoadSnapshot(0, 0, 0)),
            new WarehouseUnloadStep("Iliya", "Iliya",
                [new RouteItemQuantity("10", 163)],
                new RouteLoadSnapshot(0, 0, 0)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);

        var replayed = RouteReplay.ReplayRoute(request, route);
        Assert.NotNull(replayed);
        Assert.Equal(100, replayed!.Steps[0].Load.CargoLT);  // 800049 × 1
        var barter = (BarterStep)replayed.Steps[1];
        Assert.Equal(16_300, barter.Load.CargoLT);            // 10 × 163
    }

    [Fact]
    public void Replay_AfterNormalize_RemovedItem_DoesNotAppear() {
        var request = BuildRequest(("800045", 4), ("800049", 1), ("10", 1));
        var original = new PlannedRoute(1, "Iliya", "Iliya", new RouteStep[] {
            new WarehousePickupStep("Iliya", "Iliya",
                [new RouteItemQuantity("800045", 1),
                 new RouteItemQuantity("800049", 1)],
                new RouteLoadSnapshot(0, 0, 0)),
            new BarterStep("Crow:800049:10", "Crow",
                new RouteItemQuantity("800049", 1),
                new RouteItemQuantity("10", 163),
                new RouteLoadSnapshot(0, 0, 0)),
            new WarehouseUnloadStep("Iliya", "Iliya",
                [new RouteItemQuantity("800045", 1),
                 new RouteItemQuantity("10", 163)],
                new RouteLoadSnapshot(0, 0, 0)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);

        // After normalize: 800045 removed
        var candidate = new PlannedRoute(1, "Iliya", "Iliya", new RouteStep[] {
            new WarehousePickupStep("Iliya", "Iliya",
                [new RouteItemQuantity("800049", 1)],
                new RouteLoadSnapshot(0, 0, 0)),
            new BarterStep("Crow:800049:10", "Crow",
                new RouteItemQuantity("800049", 1),
                new RouteItemQuantity("10", 163),
                new RouteLoadSnapshot(0, 0, 0)),
            new WarehouseUnloadStep("Iliya", "Iliya",
                [new RouteItemQuantity("10", 163)],
                new RouteLoadSnapshot(0, 0, 0)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);

        var (normalized, changed) = RouteCargoNormalizer.NormalizeRoute(request, original);
        // The normalizer should detect 800045 was unused and prune it.
        Assert.True(changed);
        // And the replayed pickup's LT should be 100 (just 800049 × 1).
        Assert.Equal(100, normalized.Steps[0].Load.CargoLT);
        Assert.DoesNotContain(((WarehousePickupStep)normalized.Steps[0]).Items,
            x => x.ItemId == "800045");
    }

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
}