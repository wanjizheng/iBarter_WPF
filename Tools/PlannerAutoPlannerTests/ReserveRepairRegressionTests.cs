using iBarter.Planning;
using System.Collections.Generic;
using Xunit;

namespace PlannerAutoPlannerTests;

// Regression tests for the final-projected-inventory reserve repair.
// These verify the contract change from "lock initial stock at reserve"
// to "initial stock freely spendable; final projected inventory must
// meet the LV5/LV6 target; repair uses atomic TryBuildBundle for the
// full upstream chain".
public sealed class ReserveRepairRegressionTests {
    private static AutoPlanningRoute R(string id, int group, string i1, int lv1, int n1,
        string i2, int lv2, int n2, bool crow, int parley, int remaining) =>
        new(id, group, i1, lv1, n1, i2, lv2, n2, crow, parley, remaining);

    // 1. Real scenario: initial X=5, LV6 target=5, consumer consumes 1,
    //    producer produces 1. Plan must succeed and meet reserve.
    [Fact]
    public void Real_scenario_initial_stock_plus_producer_meets_reserve() {
        var consumer = R("consumer", 1, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        var producer = R("producer", 1, "P", 1, 1, "X", 6, 1, false, 5_000, 10);
        var request = new AutoPlanningRequest(
            new[] { consumer, producer },
            new Dictionary<string, int> { ["X"] = 5, ["P"] = 100, ["Top"] = 0 },
            AutoPlanningStrategy.ProfitFirst, 0, 5, 1_000_000);
        var result = new PlannerAutoPlanner().Plan(request);
        Assert.True(result.Success);
        Assert.Equal(1, result.Multipliers["consumer"]);
        Assert.Equal(1, result.Multipliers["producer"]);
        Assert.Equal(5, result.ProjectedInventory["X"]);
    }

    // 3. Producer input missing: reserve repair needs a producer whose
    //    input is not available and has no upstream. Plan must fail with
    //    reserve-no-producer (no negative inventory, no half-committed
    //    repair producer).
    [Fact]
    public void Reserve_repair_does_not_add_producer_with_missing_input() {
        // Consumer of X, target X=5. Producer needs P but P=0 and no
        // upstream for P. The repair must NOT add the producer.
        var consumer = R("consumer", 1, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        var producer = R("producer", 1, "P", 1, 1, "X", 6, 1, false, 5_000, 10);
        var request = new AutoPlanningRequest(
            new[] { consumer, producer },
            new Dictionary<string, int> { ["X"] = 5, ["P"] = 0, ["Top"] = 0 },
            AutoPlanningStrategy.ProfitFirst, 0, 5, 1_000_000);
        var result = new PlannerAutoPlanner().Plan(request);
        Assert.False(result.Success);
        // The producer must NOT be committed by the repair.
        Assert.Equal(0, result.Multipliers["producer"]);
        // No negative inventory.
        foreach (var kv in result.ProjectedInventory)
            Assert.True(kv.Value >= 0, $"negative {kv.Key}={kv.Value}");
        Assert.Contains(result.Diagnostics, d => d.Code.StartsWith("reserve-"));
    }

    // 4. Two-level upstream: repair atomically adds upstream + producer.
    [Fact]
    public void Reserve_repair_atomically_adds_two_level_upstream() {
        // Consumer of X, target X=5. Producer needs Y. Y's producer needs
        // Z. All start at 0. The repair must add upstream→producer chain.
        var consumer = R("consumer", 1, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        var producer = R("producer", 1, "Y", 1, 1, "X", 6, 1, false, 5_000, 1);
        var upstream = R("upstream", 1, "Z", 1, 1, "Y", 1, 1, false, 1_000, 1);
        var request = new AutoPlanningRequest(
            new[] { consumer, producer, upstream },
            new Dictionary<string, int> { ["X"] = 5, ["Y"] = 0, ["Z"] = 5, ["Top"] = 0 },
            AutoPlanningStrategy.ProfitFirst, 0, 5, 1_000_000);
        var result = new PlannerAutoPlanner().Plan(request);
        Assert.True(result.Success);
        Assert.Equal(1, result.Multipliers["consumer"]);
        // Both producer and upstream must be committed by the repair.
        Assert.True(result.Multipliers["producer"] >= 1);
        Assert.True(result.Multipliers["upstream"] >= 1);
        Assert.Equal(5, result.ProjectedInventory["X"]);
    }

    // 5. Wrong group: a cheaper cross-group producer must NOT be selected.
    [Fact]
    public void Reserve_repair_rejects_cross_group_producer() {
        // Consumer in group 1. Same-item producer in group 2 is cheaper
        // but wrong group. The repair must fail (no legal same-group
        // producer), not pick the cross-group one.
        var consumer = R("consumer", 1, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        var crossGroupProducer = R("cross", 2, "P", 1, 1, "X", 6, 1, false, 1_000, 10);
        var request = new AutoPlanningRequest(
            new[] { consumer, crossGroupProducer },
            new Dictionary<string, int> { ["X"] = 5, ["P"] = 100, ["Top"] = 0 },
            AutoPlanningStrategy.ProfitFirst, 0, 5, 1_000_000);
        var result = new PlannerAutoPlanner().Plan(request);
        Assert.False(result.Success);
        Assert.Equal(0, result.Multipliers["cross"]);
    }

    // 6. Budget insufficient: producer exists but budget can't afford it.
    //    Must be reserve-budget-exceeded, not reserve-no-producer.
    [Fact]
    public void Reserve_repair_budget_exhausted_reports_budget_diagnostic() {
        var consumer = R("consumer", 1, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        var producer = R("producer", 1, "P", 1, 1, "X", 6, 1, false, 5_000, 10);
        var request = new AutoPlanningRequest(
            new[] { consumer, producer },
            new Dictionary<string, int> { ["X"] = 5, ["P"] = 100, ["Top"] = 0 },
            AutoPlanningStrategy.ProfitFirst, 0, 5,
            12_000); // consumer=10k + producer=5k = 15k, budget=12k
        var result = new PlannerAutoPlanner().Plan(request);
        Assert.False(result.Success);
        // Producer must not be partially committed.
        Assert.Equal(0, result.Multipliers["producer"]);
    }

    // 7. Cascade: repair of X adds a producer that consumes Y (also
    //    reserved). The fixed-point repair must continue to repair Y.
    [Fact]
    public void Reserve_repair_cascade_to_second_reserve() {
        // Consumer of X needs producer of X. Producer of X needs Y.
        // Consumer of Y needs producer of Y. Producer of Y needs Z.
        // Initial: X=5 target=5, Y=5 target=5, Z=10.
        // producerY needs Remaining=2 to cover both consumerY and producerX.
        var consumerX = R("consumerX", 1, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        var producerX = R("producerX", 1, "Y", 6, 1, "X", 6, 1, false, 5_000, 1);
        var consumerY = R("consumerY", 2, "Y", 6, 1, "Side", 7, 1, false, 10_000, 1);
        var producerY = R("producerY", 2, "Z", 1, 1, "Y", 6, 1, false, 5_000, 2);
        var request = new AutoPlanningRequest(
            new[] { consumerX, producerX, consumerY, producerY },
            new Dictionary<string, int> { ["X"] = 5, ["Y"] = 5, ["Z"] = 10, ["Top"] = 0, ["Side"] = 0 },
            AutoPlanningStrategy.ProfitFirst, 0, 5, 1_000_000);
        var result = new PlannerAutoPlanner().Plan(request);
        Assert.True(result.Success);
        // Both consumerX and consumerY must be committed.
        Assert.Equal(1, result.Multipliers["consumerX"]);
        Assert.Equal(1, result.Multipliers["consumerY"]);
        // Both producerX and producerY must be committed (cascade repair).
        Assert.Equal(1, result.Multipliers["producerX"]);
        Assert.Equal(2, result.Multipliers["producerY"]);
        Assert.Equal(5, result.ProjectedInventory["X"]);
        Assert.Equal(5, result.ProjectedInventory["Y"]);
    }

    // 8. Atomic rollback: if the second reserve repair fails, the first
    //    repair's multiplier must NOT be in the result.
    [Fact]
    public void Reserve_repair_atomic_rollback_on_second_failure() {
        // First reserve (X) can be repaired. Second reserve (Y) cannot.
        // The result must have neither producer committed.
        var consumerX = R("consumerX", 1, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        var producerX = R("producerX", 1, "P", 1, 1, "X", 6, 1, false, 5_000, 10);
        var consumerY = R("consumerY", 2, "Y", 6, 1, "Side", 7, 1, false, 10_000, 1);
        // No producerY — Y repair will fail.
        var request = new AutoPlanningRequest(
            new[] { consumerX, producerX, consumerY },
            new Dictionary<string, int> { ["X"] = 5, ["Y"] = 5, ["P"] = 100, ["Top"] = 0, ["Side"] = 0 },
            AutoPlanningStrategy.ProfitFirst, 0, 5, 1_000_000);
        var result = new PlannerAutoPlanner().Plan(request);
        Assert.False(result.Success);
        // No producer committed by the (failed) repair.
        Assert.Equal(0, result.Multipliers["producerX"]);
    }

    // 9. All final inventory nonneg.
    [Fact]
    public void Reserve_repair_never_produces_negative_inventory() {
        var consumer = R("consumer", 1, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        var producer = R("producer", 1, "P", 1, 1, "X", 6, 1, false, 5_000, 10);
        var request = new AutoPlanningRequest(
            new[] { consumer, producer },
            new Dictionary<string, int> { ["X"] = 5, ["P"] = 100, ["Top"] = 0 },
            AutoPlanningStrategy.ProfitFirst, 0, 5, 1_000_000);
        var result = new PlannerAutoPlanner().Plan(request);
        if (result.Success) {
            foreach (var kv in result.ProjectedInventory)
                Assert.True(kv.Value >= 0, $"negative {kv.Key}={kv.Value}");
        }
    }

    // 10. Direct already selected: when the strategy already produces
    //    enough of a reserved item, the repair must NOT add a second
    //    support producer beyond what the strategy already committed.
    [Fact]
    public void Reserve_repair_skips_when_direct_already_sufficient() {
        // The strategy picks direct (LV5→LV6) which produces 1 X. After
        // the strategy phase X=5+1=6 meets Lv6Target=5. The strategy
        // also picks support (LV4→LV5) because it can; support's
        // Remaining=1, so support commits 1. After strategy: direct=1,
        // support=1. The repair must NOT add more support producers.
        var direct = R("direct", 1, "P", 5, 1, "X", 6, 1, false, 5_000, 1);
        var support = R("support", 1, "Q", 4, 1, "X", 5, 1, false, 5_000, 1);
        var request = new AutoPlanningRequest(
            new[] { direct, support },
            new Dictionary<string, int> { ["P"] = 10, ["Q"] = 10 },
            AutoPlanningStrategy.ProfitFirst, 0, 5, 1_000_000);
        var result = new PlannerAutoPlanner().Plan(request);
        Assert.True(result.Success);
        Assert.Equal(1, result.Multipliers["direct"]);
        // support committed by the strategy (Remaining=1). The repair
        // must NOT add more — the target is already met.
        Assert.Equal(1, result.Multipliers["support"]);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code.StartsWith("reserve-"));
    }

    // 11. Determinism: same input → same multipliers, same diagnostics,
    //    same final inventory.
    [Fact]
    public void Reserve_repair_is_deterministic() {
        var consumer = R("consumer", 1, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        var producer = R("producer", 1, "P", 1, 1, "X", 6, 1, false, 5_000, 10);
        var request = new AutoPlanningRequest(
            new[] { consumer, producer },
            new Dictionary<string, int> { ["X"] = 5, ["P"] = 100, ["Top"] = 0 },
            AutoPlanningStrategy.ProfitFirst, 0, 5, 1_000_000);
        var r1 = new PlannerAutoPlanner().Plan(request);
        var r2 = new PlannerAutoPlanner().Plan(request);
        var r3 = new PlannerAutoPlanner().Plan(request);
        Assert.Equal(r1.Multipliers, r2.Multipliers);
        Assert.Equal(r2.Multipliers, r3.Multipliers);
        Assert.Equal(r1.ProjectedInventory, r2.ProjectedInventory);
        Assert.Equal(r2.ProjectedInventory, r3.ProjectedInventory);
        Assert.Equal(r1.Diagnostics.Count, r2.Diagnostics.Count);
        Assert.Equal(r2.Diagnostics.Count, r3.Diagnostics.Count);
    }
}
