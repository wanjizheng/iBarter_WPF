using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public sealed class DemandBundleGeneratorTests {
    [Fact]
    public void Single_task_loads_only_the_missing_quantity() {
        var request = RouteTestData.SingleTask(inputStock: 20, inputQuantity: 2);
        var bundles = DemandBundleGenerator.Generate(
            request, RouteSimulationState.CreateInitial(request), "W", 1);

        var bundle = Assert.Single(bundles);
        var item = Assert.Single(bundle.Items);
        Assert.Equal("IN", item.ItemId);
        Assert.Equal(2, item.Quantity);
    }

    [Fact]
    public void Production_chain_needs_only_the_root_input() {
        var request = ChainRequest();
        var bundles = DemandBundleGenerator.Generate(
            request, RouteSimulationState.CreateInitial(request), "W", 3);

        Assert.Contains(bundles, bundle =>
            bundle.SupportedTaskMask == 3 && bundle.Items.Count == 1 &&
            bundle.Items[0] == new RouteItemQuantity("A", 1));
    }

    [Fact]
    public void Independent_tasks_generate_single_and_joint_deduplicated_bundles() {
        var request = RouteTestData.TwoItemRequest(false);
        var bundles = DemandBundleGenerator.Generate(
            request, RouteSimulationState.CreateInitial(request), "W", 3);

        Assert.Contains(bundles, x => x.Items.Count == 1 && x.Items[0].ItemId == "A");
        Assert.Contains(bundles, x => x.Items.Count == 1 && x.Items[0].ItemId == "B");
        Assert.Contains(bundles, x => x.Items.Count == 2);
        Assert.Equal(bundles.Count, bundles.Select(x => x.StableKey).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Overweight_bundles_are_filtered_and_order_is_deterministic() {
        var forward = RouteTestData.TwoItemRequest(false);
        var reverse = RouteTestData.TwoItemRequest(true);
        var limited = new AutomaticRoutePlanningRequest(
            forward.Tasks, forward.Items, forward.Warehouses, 0, 500, forward.Limits, "limited");

        var limitedBundles = DemandBundleGenerator.Generate(
            limited, RouteSimulationState.CreateInitial(limited), "W", 3);
        Assert.DoesNotContain(limitedBundles, x => x.Items.Count == 2);

        var a = DemandBundleGenerator.Generate(forward, RouteSimulationState.CreateInitial(forward), "W", 3);
        var b = DemandBundleGenerator.Generate(reverse, RouteSimulationState.CreateInitial(reverse), "W", 3);
        Assert.Equal(a.Select(x => x.StableKey), b.Select(x => x.StableKey));
    }

    private static AutomaticRoutePlanningRequest ChainRequest() {
        var items = new Dictionary<string, RouteItem> {
            ["A"] = new("A", "A", 1, 100),
            ["B"] = new("B", "B", 1, 100),
            ["C"] = new("C", "C", 1, 100),
        };
        return new AutomaticRoutePlanningRequest(
            [
                new("r1", "I1", new RoutePoint(1, 0), "A", 1, "B", 1),
                new("r2", "I2", new RoutePoint(2, 0), "B", 1, "C", 1),
            ], items,
            [new RouteWarehouse("W", "W", new RoutePoint(0, 0), new Dictionary<string, int> { ["A"] = 10 })],
            0, 1_000, new RouteSearchLimits(10_000, 100), "chain");
    }
}
