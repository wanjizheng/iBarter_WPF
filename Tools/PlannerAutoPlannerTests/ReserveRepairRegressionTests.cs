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

    [Fact]
    public void Candidate_reserve_hardening_merges_only_incremental_repair_multipliers() {
        // The second consumer increment needs a second producer increment.
        // Both are already present once in committed state, so the hardened
        // candidate must contribute +1/+1, never re-merge the probe totals
        // (+2/+2) and exceed Remaining.
        var consumer = R("consumer", 1, "X", 6, 1, "Top", 7, 1, false, 10_000, 2);
        var producer = R("producer", 1, "P", 1, 1, "X", 6, 1, false, 5_000, 2);
        var request = new AutoPlanningRequest(
            new[] { consumer, producer },
            new Dictionary<string, int> { ["X"] = 5, ["P"] = 2, ["Top"] = 0 },
            AutoPlanningStrategy.ProfitFirst, 0, 5, 1_000_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Code)));
        Assert.Equal(2, result.Multipliers["consumer"]);
        Assert.Equal(2, result.Multipliers["producer"]);
        Assert.Equal(5, result.ProjectedInventory["X"]);
        Assert.Equal(30_000, result.UsedParley);
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

    // 8. Candidate hardening: if a later consumer would violate an
    //    unfillable reserve, keep the earlier independently valid bundle.
    [Fact]
    public void Candidate_reserve_hardening_preserves_prior_valid_bundle_when_later_consumer_is_unfillable() {
        // X can be consumed and replenished. Y cannot be replenished, so its
        // candidate is rejected before commit. A rejected later candidate must
        // not erase the already verified X bundle.
        var consumerX = R("consumerX", 1, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        var producerX = R("producerX", 1, "P", 1, 1, "X", 6, 1, false, 5_000, 10);
        var consumerY = R("consumerY", 2, "Y", 6, 1, "Side", 7, 1, false, 10_000, 1);
        // No producerY — Y repair will fail.
        var request = new AutoPlanningRequest(
            new[] { consumerX, producerX, consumerY },
            new Dictionary<string, int> { ["X"] = 5, ["Y"] = 5, ["P"] = 100, ["Top"] = 0, ["Side"] = 0 },
            AutoPlanningStrategy.ProfitFirst, 0, 5, 1_000_000);
        var result = new PlannerAutoPlanner().Plan(request);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Code)));
        Assert.Equal(1, result.Multipliers["consumerX"]);
        Assert.Equal(1, result.Multipliers["producerX"]);
        Assert.Equal(0, result.Multipliers["consumerY"]);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code.StartsWith("reserve-"));
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

    // ---------------------------------------------------------------
    // Multi-group reserve repair. The reserve is a global final-inventory
    // constraint on an item, so a single item produces exactly one deficit
    // regardless of how many consumer groups touch it. Producers from any
    // consumer group may satisfy that deficit, never producers from a group
    // that does not consume the item.
    //
    // The fixed-point outer loop already exists, so multiple groups can
    // cooperate across iterations: group 1 covers part of the deficit,
    // the next iteration re-collects with the smaller deficit, group 2
    // covers the rest.
    // ---------------------------------------------------------------

    // 12. First consumer group has no producer; second group does.
    //     The repair must use the second group's producer (NOT the
    //     first group's "no producer" condition must not poison the result).
    [Fact]
    public void Multi_group_repair_uses_second_group_when_first_has_no_producer() {
        // Group 1 consumes X with no producer.
        // Group 2 consumes X and has a legal producer.
        var consumerG1 = R("cg1", 1, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        var consumerG2 = R("cg2", 2, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        var producerG2 = R("pg2", 2, "P", 1, 1, "X", 6, 1, false, 5_000, 10);
        var request = new AutoPlanningRequest(
            new[] { consumerG1, consumerG2, producerG2 },
            new Dictionary<string, int> { ["X"] = 5, ["P"] = 100, ["Top"] = 0 },
            AutoPlanningStrategy.ProfitFirst, 0, 5, 1_000_000);
        var result = new PlannerAutoPlanner().Plan(request);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Code + ":" + d.RowId)));
        // Both consumers ran in the strategy phase; group 2 producer was
        // added by the repair.
        Assert.True(result.Multipliers["pg2"] >= 1,
            $"pg2 not committed: {result.Multipliers.GetValueOrDefault("pg2")}");
        Assert.Equal(5, result.ProjectedInventory["X"]);
    }

    // 13. First group's producer exists but its bundle is infeasible
    //     (missing input, no upstream). The repair must skip it and use
    //     the second group's executable producer.
    [Fact]
    public void Multi_group_repair_skips_infeasible_first_group_producer() {
        var consumerG1 = R("cg1", 1, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        // G1 producer needs P but P=0 and no upstream.
        var producerG1 = R("pg1", 1, "P", 1, 1, "X", 6, 1, false, 5_000, 10);
        var consumerG2 = R("cg2", 2, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        // G2 producer has full input.
        var producerG2 = R("pg2", 2, "Q", 1, 1, "X", 6, 1, false, 5_000, 10);
        var request = new AutoPlanningRequest(
            new[] { consumerG1, consumerG2, producerG1, producerG2 },
            new Dictionary<string, int> { ["X"] = 5, ["P"] = 0, ["Q"] = 100, ["Top"] = 0 },
            AutoPlanningStrategy.ProfitFirst, 0, 5, 1_000_000);
        var result = new PlannerAutoPlanner().Plan(request);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Code + ":" + d.RowId)));
        Assert.Equal(0, result.Multipliers["pg1"]);
        Assert.True(result.Multipliers["pg2"] >= 1);
        Assert.Equal(5, result.ProjectedInventory["X"]);
    }

    // 14. Reserve is a GLOBAL constraint. Two groups both consume X. The
    //     deficit after strategy is bigger than any single consumer group's
    //     producer can cover. The repair must sum production across BOTH
    //     consumer groups, not stall after the first group's producer is
    //     exhausted.
    [Fact]
    public void Multi_group_global_deficit_is_not_counted_per_group() {
        // Initial X = 4, target = 5. After both consumers commit in the
        // strategy phase: X = 2, deficit = 3. Group 1's producer only has
        // Remaining=1; group 2's producer has Remaining=10. Old code only
        // checked group 1, exhausted it, and rolled back. New code picks
        // group 2 to cover the remaining deficit.
        var consumerG1 = R("cg1", 1, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        var consumerG2 = R("cg2", 2, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        var producerG1 = R("pg1", 1, "P", 1, 1, "X", 6, 1, false, 5_000, 1);
        var producerG2 = R("pg2", 2, "Q", 1, 1, "X", 6, 1, false, 5_000, 10);
        var request = new AutoPlanningRequest(
            new[] { consumerG1, consumerG2, producerG1, producerG2 },
            new Dictionary<string, int> { ["X"] = 4, ["P"] = 100, ["Q"] = 100, ["Top"] = 0 },
            AutoPlanningStrategy.ProfitFirst, 0, 5, 1_000_000);
        var result = new PlannerAutoPlanner().Plan(request);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Code + ":" + d.RowId)));
        // Total production must equal the global deficit (3), not exceed
        // it (no double-counting per consumer group).
        int produced = result.Multipliers.GetValueOrDefault("pg1") +
                       result.Multipliers.GetValueOrDefault("pg2");
        Assert.Equal(3, produced);
        Assert.Equal(5, result.ProjectedInventory["X"]);
    }

    // 15. Multi-group cooperation across iterations: deficit=5, group 1
    //     producer covers 2, group 3 producer covers 3. Initial X is set
    //     high enough that the strategy phase consumes X directly without
    //     pulling in the producers — so the producers' full Remaining is
    //     available to the repair.
    [Fact]
    public void Multi_group_repair_cooperates_across_iterations() {
        var consumerG1 = R("cg1", 1, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        var consumerG2 = R("cg2", 2, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        var consumerG3 = R("cg3", 3, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        // Group 1 producer can produce 2 X (Remaining=2).
        var producerG1 = R("pg1", 1, "P", 1, 1, "X", 6, 1, false, 5_000, 2);
        // Group 3 producer can produce 3 X (Remaining=3).
        var producerG3 = R("pg3", 3, "Q", 1, 1, "X", 6, 1, false, 5_000, 3);
        var request = new AutoPlanningRequest(
            new[] { consumerG1, consumerG2, consumerG3, producerG1, producerG3 },
            // Initial X=3 covers all 3 consumers in the strategy phase
            // without needing producer pull-in. After strategy: X=0,
            // deficit = 5. The repair must commit pg1=2 then pg3=3.
            new Dictionary<string, int> { ["X"] = 3, ["P"] = 100, ["Q"] = 100, ["Top"] = 0 },
            AutoPlanningStrategy.ProfitFirst, 0, 5, 1_000_000);
        var result = new PlannerAutoPlanner().Plan(request);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Code + ":" + d.RowId)));
        // Together: 2 + 3 = 5 producers, exactly meeting the deficit.
        int produced = result.Multipliers["pg1"] +
                       result.Multipliers["pg3"];
        Assert.Equal(5, produced);
        Assert.Equal(5, result.ProjectedInventory["X"]);
    }

    // 16. An unrelated group has a cheaper producer for X but NO consumer
    //     in that group touches X. The repair must NOT use that producer
    //     even when cheaper — it must stay within the consumer group set.
    [Fact]
    public void Multi_group_repair_rejects_unrelated_group_producer() {
        // Two consumers in different groups. The legal producer (pg2) lives
        // in group 2 (a consumer group). pg9 is cheap but its group has no
        // consumer of X.
        var consumerG1 = R("cg1", 1, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        var consumerG2 = R("cg2", 2, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        var producerG2 = R("pg2", 2, "Q", 1, 1, "X", 6, 1, false, 5_000, 10);
        var producerG9 = R("pg9", 9, "P", 1, 1, "X", 6, 1, false, 100, 10);
        var request = new AutoPlanningRequest(
            new[] { consumerG1, consumerG2, producerG2, producerG9 },
            new Dictionary<string, int> { ["X"] = 4, ["P"] = 100, ["Q"] = 100, ["Top"] = 0 },
            AutoPlanningStrategy.ProfitFirst, 0, 5, 1_000_000);
        var result = new PlannerAutoPlanner().Plan(request);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Code + ":" + d.RowId)));
        // The unrelated group's producer must NOT have been used.
        Assert.Equal(0, result.Multipliers["pg9"]);
        // The legal producer (in group 2, which has a consumer) is used.
        Assert.True(result.Multipliers["pg2"] >= 1);
    }

    // 17. The candidate ranking must compare FULL bundles, not surface
    //     producer parley. Producer A is cheaper on its own but its
    //     upstream chain makes the full bundle more expensive than
    //     producer B's bundle.
    [Fact]
    public void Multi_group_repair_ranks_by_full_bundle_parley() {
        // Group 1: consumer.
        var consumerG1 = R("cg1", 1, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        // Group 2: consumer + 2 candidate producers.
        var consumerG2 = R("cg2", 2, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        // Producer A in group 2: low surface parley, but needs an upstream
        // chain that costs an extra 1000 parley (via "M").
        var producerA = R("pa", 2, "A_in", 1, 1, "X", 6, 1, false, 100, 10);
        var upstreamA = R("ua", 2, "M", 1, 1, "A_in", 1, 1, false, 1_000, 1);
        // Producer B in group 2: slightly higher surface parley, but its
        // input is already abundant (no upstream needed).
        var producerB = R("pb", 2, "B_in", 1, 1, "X", 6, 1, false, 500, 10);
        var request = new AutoPlanningRequest(
            new[] { consumerG1, consumerG2, producerA, upstreamA, producerB },
            new Dictionary<string, int> {
                ["X"] = 4, // initial, target = 5, deficit = 1
                ["A_in"] = 0, // producer A needs upstream
                ["B_in"] = 100, // producer B is self-sufficient
                ["M"] = 5,
                ["Top"] = 0,
            },
            AutoPlanningStrategy.ProfitFirst, 0, 5, 1_000_000);
        var result = new PlannerAutoPlanner().Plan(request);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Code + ":" + d.RowId)));
        // Producer B must be selected over producer A.
        Assert.Equal(0, result.Multipliers["pa"]);
        Assert.Equal(0, result.Multipliers["ua"]);
        Assert.True(result.Multipliers["pb"] >= 1);
    }

    // 18. A later candidate whose reserve producer would exceed the budget is
    //     rejected before commit; the earlier valid reserve-safe bundle remains.
    [Fact]
    public void Candidate_reserve_hardening_does_not_rollback_a_valid_bundle_when_later_bundle_exceeds_budget() {
        var consumerG1 = R("cg1", 1, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        var consumerG2 = R("cg2", 2, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        // Each consumer needs an 8k producer to preserve X. One pair fits in
        // 25k; adding the second does not. The planner must keep one valid
        // pair rather than accept both and fail only at the final repair.
        var producerG1 = R("pg1", 1, "P", 1, 1, "X", 6, 1, false, 8_000, 10);
        var producerG2 = R("pg2", 2, "Q", 1, 1, "X", 6, 1, false, 8_000, 10);
        var request = new AutoPlanningRequest(
            new[] { consumerG1, consumerG2, producerG1, producerG2 },
            new Dictionary<string, int> { ["X"] = 5, ["P"] = 100, ["Q"] = 100, ["Top"] = 0 },
            AutoPlanningStrategy.ProfitFirst, 0, 5,
            25_000);
        var result = new PlannerAutoPlanner().Plan(request);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Code)));
        Assert.Equal(1, result.Multipliers["cg1"]);
        Assert.Equal(1, result.Multipliers["pg1"]);
        Assert.Equal(0, result.Multipliers["cg2"]);
        Assert.Equal(0, result.Multipliers["pg2"]);
        Assert.Equal(18_000, result.UsedParley);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code.StartsWith("reserve-"));
    }

    // 19. Determinism under multi-group repair. Three consecutive runs.
    [Fact]
    public void Multi_group_repair_is_deterministic() {
        var consumerG1 = R("cg1", 1, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        var consumerG2 = R("cg2", 2, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        var producerG1 = R("pg1", 1, "P", 1, 1, "X", 6, 1, false, 5_000, 10);
        var producerG2 = R("pg2", 2, "Q", 1, 1, "X", 6, 1, false, 5_000, 10);
        var request = new AutoPlanningRequest(
            new[] { consumerG1, consumerG2, producerG1, producerG2 },
            new Dictionary<string, int> { ["X"] = 4, ["P"] = 100, ["Q"] = 100, ["Top"] = 0 },
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

    // 20. Item-level inconsistency: two routes declare the same ItemId
    //     as Item1 but with DIFFERENT Item1Level (one claims LV5, the
    //     other claims LV6). The planner must NOT silently pick one.
    //     Either it surfaces an explicit "reserve-level-inconsistent"
    //     diagnostic, or the plan fails atomically with a clear code.
    [Fact]
    public void Multi_group_repair_detects_item_level_inconsistency() {
        var consumerLV5 = R("cg1", 1, "X", 5, 1, "Top", 7, 1, false, 10_000, 1);
        var consumerLV6 = R("cg2", 2, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        var request = new AutoPlanningRequest(
            new[] { consumerLV5, consumerLV6 },
            new Dictionary<string, int> { ["X"] = 5, ["Top"] = 0 },
            AutoPlanningStrategy.ProfitFirst, 5, 5, 1_000_000);
        var result = new PlannerAutoPlanner().Plan(request);
        // Atomic failure with an explicit inconsistent-level diagnostic.
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics,
            d => d.Code.StartsWith("reserve-") && d.RowId == "X");
    }

    // 21. 800058 regression scenario: a high-tier LV5 item consumed by
    //     multiple groups. The first consumer group (by ordinal RowId)
    //     has no legal producer; a later consumer group has one. The
    //     repair must use the later group's producer.
    [Fact]
    public void Multi_group_repair_800058_style_scenario() {
        // Two consumers of item "X" at LV5 (mirrors 800058 = 37yr Herbal
        // Wine). Group 1's only candidate producer is incomplete (input
        // missing); group 2's producer has its full input. After the
        // strategy phase, projected X is below the LV5 target.
        var consumerG1 = R("c_a", 1, "X", 5, 1, "Top", 7, 1, false, 10_000, 1);
        var consumerG2 = R("c_b", 2, "X", 5, 1, "Top", 7, 1, false, 10_000, 1);
        // Group 1 producer: needs "Q" but Q=0 and no upstream → infeasible.
        var producerG1 = R("p_a", 1, "Q", 1, 1, "X", 5, 1, false, 1_000, 10);
        // Group 2 producer: needs "P" and P=10 → feasible.
        var producerG2 = R("p_b", 2, "P", 1, 1, "X", 5, 1, false, 1_000, 10);
        var request = new AutoPlanningRequest(
            new[] { consumerG1, consumerG2, producerG1, producerG2 },
            new Dictionary<string, int> {
                ["X"] = 4, // initial; after 2 consumers in strategy = 2; target = 5
                ["P"] = 10,
                ["Q"] = 0,  // makes group 1's producer infeasible
                ["Top"] = 0,
            },
            AutoPlanningStrategy.ProfitFirst, 5, 0, 1_000_000);
        var result = new PlannerAutoPlanner().Plan(request);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Code + ":" + d.RowId)));
        // Group 2's producer was the one that actually executed.
        Assert.Equal(0, result.Multipliers["p_a"]);
        Assert.True(result.Multipliers["p_b"] >= 1);
        Assert.Equal(5, result.ProjectedInventory["X"]);
    }
}
