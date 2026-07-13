using iBarter.Planning;
using Xunit;

namespace PlannerAutoPlannerTests;

public sealed class PlannerAutoPlannerTests {
    [Fact]
    public void Request_rejects_duplicate_row_ids() {
        var route = Route("r1", 1, "A", 4, 1, "B", 5, 1, 10_000, 5);
        var request = new AutoPlanningRequest(
            [route, route], new Dictionary<string, int> { ["A"] = 10 },
            AutoPlanningStrategy.ProfitFirst, 10, 10, 1_000_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == "duplicate-row-id");
    }

    [Fact]
    public void Reverse_supply_uses_ceiling_quantity_not_equal_multiplier() {
        // A→2B upstream (3 exchanges make 6 B), B→C downstream (5 exchanges consume 5 B).
        // Parley = 20_000 * 3 + 4_000 * 5 = 60_000 + 20_000 = 80_000 (budget).
        // Routes moved to LV4–LV6 so ProfitFirst's tier filter accepts them as
        // direct targets; reserve is disabled to keep the test focused on chain
        // pull-in semantics rather than LV5 reserve pull-in.
        var rA = Route("rA", 7, "A", 4, 1, "B", 5, 2, 20_000, 3);
        var rB = Route("rB", 7, "B", 5, 1, "C", 6, 1, 4_000, 5);
        var request = PlanStrategyNoReserve(AutoPlanningStrategy.ProfitFirst, [rA, rB],
            new Dictionary<string, int> { ["A"] = 10, ["B"] = 0 }, 80_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.True(result.Success);
        Assert.Equal(3, result.Multipliers["rA"]);
        Assert.Equal(5, result.Multipliers["rB"]);
        Assert.Equal(1, result.ProjectedInventory["B"]);
    }

    [Fact]
    public void Reverse_supply_crosses_groups_via_inventory_after_producer_commits() {
        // Routes moved to LV4–LV5 (ProfitFirst's tier filter accepts these).
        // Reserve disabled (Lv5Target=0) to keep this test focused on
        // cross-group reverse supply, not LV5 reserve pull-in.
        // rA produces B (1 per exchange) in group 1; rB consumes B in group 2
        // (no in-group producer for B). The planner commits rA first to
        // produce B from A inventory; once rA=1 lands, rB's bundle succeeds
        // because B is now available across the group boundary — cross-group
        // reverse supply works through inventory, not the route graph.
        var rA = Route("rA", 1, "A", 4, 1, "B", 5, 1, 1_000, 5);
        var rB = Route("rB", 2, "B", 5, 1, "C", 6, 1, 1_000, 5);
        var request = PlanStrategyNoReserve(AutoPlanningStrategy.ProfitFirst, [rA, rB],
            new Dictionary<string, int> { ["A"] = 10, ["B"] = 0 }, 5_000);

        var result = new PlannerAutoPlanner().Plan(request);

        // rA produces B (1 per exchange); rB consumes B. With +1 per iter greedy:
        //   Iter 1: rB bundle's LV5→B deficit pulls no in-group producer for B in
        //          group 2, but A=10 has no reserve (Lv5Target=0) so rA wins. rA=1.
        //   Iter 2: rB now feasible (B=1 from rA). rB wins (tier parity). rB=1.
        //   Iter 3: rA wins → produces another B. rA=2, rB=2.
        //   Iter 4: rA wins (parley 1000 each, fits in 5k budget). rA=3.
        Assert.Equal(3, result.Multipliers["rA"]);
        Assert.Equal(2, result.Multipliers["rB"]);
        Assert.Equal(5_000, result.UsedParley);
    }

    [Fact]
    public void Three_level_reverse_chain_is_added_atomically() {
        // Routes promoted to LV4–LV7 so ProfitFirst's tier filter accepts the
        // chain end-to-end; reserve disabled so we exercise atomic chain
        // pull-in without LV5/LV6 reserve pull-in entanglement.
        var r1 = Route("r1", 3, "A", 4, 1, "B", 5, 1, 1_000, 2);
        var r2 = Route("r2", 3, "B", 5, 1, "C", 6, 1, 1_000, 2);
        var r3 = Route("r3", 3, "C", 6, 1, "D", 7, 1, 1_000, 2);
        var request = PlanStrategyNoReserve(AutoPlanningStrategy.ProfitFirst, [r1, r2, r3],
            new Dictionary<string, int> { ["A"] = 10, ["B"] = 0, ["C"] = 0 }, 100_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.Equal(2, result.Multipliers["r1"]);
        Assert.Equal(2, result.Multipliers["r2"]);
        Assert.Equal(2, result.Multipliers["r3"]);
        Assert.All(result.ProjectedInventory.Values, v => Assert.True(v >= 0));
    }

    [Fact]
    public void Failed_chain_fails_atomically() {
        // r1 needs Item X with no inventory and no producer → every bundle that
        // tries to satisfy r3 fails the whole chain atomically. Routes promoted
        // to LV4–LV6 to keep them in ProfitFirst's tier filter.
        var r1 = Route("r1", 3, "X", 4, 1, "B", 5, 1, 1_000, 1);
        var r2 = Route("r2", 3, "B", 5, 1, "C", 6, 1, 1_000, 5);
        var r3 = Route("r3", 3, "C", 6, 1, "D", 7, 1, 1_000, 2);
        var request = PlanStrategyNoReserve(AutoPlanningStrategy.ProfitFirst, [r1, r2, r3],
            new Dictionary<string, int> { ["X"] = 0, ["B"] = 0, ["C"] = 0 }, 100_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.Equal(0, result.Multipliers["r1"]);
        Assert.Equal(0, result.Multipliers["r2"]);
        Assert.Equal(0, result.Multipliers["r3"]);
    }

    [Fact]
    public void Failed_increment_stops_target_without_reverting_prior_commits() {
        // r1.Remaining=1 limits the chain: r3=2 requires r1=2 (impossible) so the
        // planner keeps the successful r3=1 commit instead of reverting everything.
        // This documents the incremental semantics: prior commits survive a later
        // failed increment. Routes promoted to LV4–LV6 for ProfitFirst filter.
        var r1 = Route("r1", 3, "A", 4, 1, "B", 5, 1, 1_000, 1); // remaining 1
        var r2 = Route("r2", 3, "B", 5, 1, "C", 6, 1, 1_000, 5);
        var r3 = Route("r3", 3, "C", 6, 1, "D", 7, 1, 1_000, 2);
        var request = PlanStrategyNoReserve(AutoPlanningStrategy.ProfitFirst, [r1, r2, r3],
            new Dictionary<string, int> { ["A"] = 10, ["B"] = 0, ["C"] = 0 }, 100_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.Equal(1, result.Multipliers["r1"]);
        Assert.Equal(1, result.Multipliers["r2"]);
        Assert.Equal(1, result.Multipliers["r3"]);
    }

    [Fact]
    public void Crow_partial_fill_when_full_remaining_exceeds_budget() {
        // rA.Remaining=5 at 60k each = 300k full, budget 100k only fits 1 exchange.
        // The shrink-to-fit algorithm should commit rA=1 and stop, leaving 40k
        // unused because rA=2 already exceeds the budget. No LV5/LV6 reserve
        // required here (the test exercises shrink-to-fit, not reserve pull-in).
        var rA = Route("rA", 1, "A", 5, 1, "CrowCoin", 6, 190, 60_000, 5, crow: true);
        var request = PlanStrategyNoReserve(AutoPlanningStrategy.CrowCoinFirst, [rA],
            new Dictionary<string, int> { ["A"] = 10 }, 100_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.Equal(1, result.Multipliers["rA"]);
        Assert.Equal(60_000, result.UsedParley);
    }

    [Fact]
    public void Profit_partial_fill_when_full_remaining_exceeds_budget() {
        // Full Remaining=3 costs 2.1M which exceeds budget. Shrink to fit: 1
        // exchange fits within budget at 700k. No LV5/LV6 reserve required
        // for this test (exercises shrink-to-fit, not reserve pull-in).
        var rLv7 = Route("rLv7", 1, "Lv7In", 6, 1, "Top", 7, 1, 700_000, 3);
        var request = PlanStrategyNoReserve(AutoPlanningStrategy.ProfitFirst, [rLv7],
            new Dictionary<string, int> { ["Lv7In"] = 10 }, 1_000_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.Equal(1, result.Multipliers["rLv7"]);
        Assert.Equal(700_000, result.UsedParley);
    }

    [Fact]
    public void Restock_partial_fill_when_full_remaining_exceeds_budget() {
        // Phase 2 candidate with Remaining=5 at 60k each = 300k full. Budget 100k
        // fits only 1 exchange.
        var rLv2 = Route("rLv2", 1, "A", 1, 1, "B", 2, 1, 60_000, 5);
        var request = new AutoPlanningRequest(
            [rLv2],
            new Dictionary<string, int> { ["A"] = 100, ["B"] = 0 },
            AutoPlanningStrategy.RestockFirst, 0, 0, 100_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.Equal(1, result.Multipliers["rLv2"]);
        Assert.Equal(60_000, result.UsedParley);
    }

    [Fact]
    public void Shared_upstream_gets_added_when_second_target_requires_more() {
        // Two downstream targets (rB, rC) share producer rA in the same group.
        // Routes promoted to LV4–LV5 tiers for ProfitFirst's filter, reserve
        // disabled to keep this test focused on chain-pull-in semantics.
        // First bundle commits rA=1 + rB=1 (1 B produced, 1 consumed by rB).
        // Second bundle for rC needs 1 more B; rA must be pulled in again
        // because rA=1's single B was already consumed. The producer's final
        // multiplier is the SUM of bundle increments, not the max.
        var rA = Route("rA", 5, "A", 4, 1, "B", 5, 1, 1_000, 2); // A→B producer, Remaining=2
        var rB = Route("rB", 5, "B", 5, 1, "C", 6, 1, 1_000, 1); // B→C consumer
        var rC = Route("rC", 5, "B", 5, 1, "D", 6, 1, 1_000, 1); // B→D consumer
        var request = PlanStrategyNoReserve(AutoPlanningStrategy.ProfitFirst, [rA, rB, rC],
            new Dictionary<string, int> { ["A"] = 10, ["B"] = 0 }, 100_000);

        var result = new PlannerAutoPlanner().Plan(request);

        // Iter 1: rB wins (LV5→LV6 tier tie with rC, lower row id wins).
        //         Bundle pulls rA=1 to produce 1 B (Item2Number=1).
        // Iter 2: rC wins. Bundle needs 1 more B, pulls rA=2 (uses rA's
        //         last remaining exchange).
        // Iter 3: rA ineligible (c=2, R=2). rB/rC ineligible. No candidates. Halt.
        // Final: rA=2, rB=1, rC=1. Parley = 2000 + 2000 = 4000.
        Assert.Equal(2, result.Multipliers["rA"]);
        Assert.Equal(1, result.Multipliers["rB"]);
        Assert.Equal(1, result.Multipliers["rC"]);
        Assert.Equal(4_000, result.UsedParley);
    }

    [Fact]
    public void Shared_upstream_cumulative_with_two_consumers_and_ceiling() {
        // rA produces 2 B per exchange. rB and rC each need 1 B. First bundle
        // commits rA=1 + rB=1 (rA produces 2 B; rB consumes 1, leaving 1 B in
        // inventory). Second bundle for rC finds 1 B available, no extra rA
        // needed. Routes promoted to LV4–LV6 for ProfitFirst's filter, reserve
        // disabled so indivisible-oversupply is the focus. rA.Remaining=1
        // limits rA to a single commit so the strategy halts cleanly once
        // both consumers are filled (without reintroducing demand tracking).
        var rA = Route("rA", 5, "A", 4, 1, "B", 5, 2, 1_000, 1);
        var rB = Route("rB", 5, "B", 5, 1, "C", 6, 1, 1_000, 1);
        var rC = Route("rC", 5, "B", 5, 1, "D", 6, 1, 1_000, 1);
        var request = PlanStrategyNoReserve(AutoPlanningStrategy.ProfitFirst, [rA, rB, rC],
            new Dictionary<string, int> { ["A"] = 10, ["B"] = 0 }, 100_000);

        var result = new PlannerAutoPlanner().Plan(request);

        // Iter 1: rB wins (LV5→LV6 tier tie with rC, lower row id wins).
        //         rB=1 needs 1 B, deficit 1 → rA=1 produces 2 B. After commit:
        //         B = 2-1 = 1.
        // Iter 2: rC wins. rC=1 needs 1 B, available=1, deficit 0. No
        //         producer needed. Bundle = rC=1.
        // Iter 3: rA ineligible (Remaining=1, committed=1). rB/rC ineligible.
        //         No candidates. Halt.
        // Final: rA=1, rB=1, rC=1. Parley = 2000 + 1000 = 3000. B = 0.
        Assert.Equal(1, result.Multipliers["rA"]);
        Assert.Equal(1, result.Multipliers["rB"]);
        Assert.Equal(1, result.Multipliers["rC"]);
        Assert.Equal(3_000, result.UsedParley);
        Assert.Equal(0, result.ProjectedInventory["B"]);
    }

    [Fact]
    public void Restock_re_ranks_after_each_increment_to_pick_lowest_projected() {
        // Phase 2 picks by lowest projected inventory; the ranker must re-evaluate
        // after each commit so the next lowest item wins. Two LV1-LV4 producers
        // with distinct starting stocks; B1 starts at 0 (lowest), so r1 wins first.
        // After r1 fills, the ranker picks r2 (B2 lower than B3).
        var r1 = Route("r1", 1, "A1", 1, 1, "B1", 2, 1, 10_000, 10);
        var r2 = Route("r2", 1, "A2", 2, 1, "B2", 3, 1, 10_000, 10);
        var r3 = Route("r3", 1, "A3", 3, 1, "B3", 4, 1, 10_000, 10);
        var request = new AutoPlanningRequest(
            [r1, r2, r3],
            new Dictionary<string, int> { ["A1"] = 100, ["A2"] = 100, ["A3"] = 100,
                                          ["B1"] = 0, ["B2"] = 5, ["B3"] = 10 },
            AutoPlanningStrategy.RestockFirst, 0, 0, 60_000);

        var result = new PlannerAutoPlanner().Plan(request);

        // Iter 1: B1=0 is lowest, r1 wins. Shrink-to-fit commits r1=6 (parley
        //         60k = budget). After commit B1=6.
        // Iter 2: candidates r2, r3. B2=5 < B3=10 → r2 wins. Bundle r2=10
        //         costs 100k > 0 remaining. Skip. Result: r1=6, others 0.
        Assert.Equal(6, result.Multipliers["r1"]);
        Assert.Equal(0, result.Multipliers["r2"]);
        Assert.Equal(0, result.Multipliers["r3"]);
        Assert.Equal(60_000, result.UsedParley);
    }

    [Fact]
    public void Routes_and_inventory_must_use_same_item_key() {
        // Regression for the WPF integration bug: routes used ItemID as the item
        // key while the inventory dict used ItemName. Real catalog data has
        // ItemID != ItemName (e.g. "800007" vs "Crow Coin"), so the planner's
        // CurrentInventory lookup always returned 0 and every chain silently
        // failed. This test verifies the planner behaves correctly when the
        // keys agree — which is now the contract enforced by the WPF code.
        // Note: this test does NOT exercise the WPF integration itself (that
        // requires running the UI); it verifies the planner is internally
        // consistent so the integration fix works in production.
        var route = new AutoPlanningRoute("r", 1, "800007", 4, 1, "800008", 5, 1, false, 10_000, 1);
        var request = new AutoPlanningRequest(
            [route],
            new Dictionary<string, int> { ["800007"] = 10 },  // keyed by ItemID
            AutoPlanningStrategy.ProfitFirst, 10, 10, 1_000_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.True(result.Success);
        Assert.Equal(1, result.Multipliers["r"]);
    }

    [Fact]
    public void Routes_and_inventory_with_mismatched_keys_fail_chain() {
        // The negative case: if routes use ItemID and inventory uses ItemName,
        // the planner cannot resolve the producer's input → bundle fails → r=0.
        // This is what was happening in production before the fix.
        var route = new AutoPlanningRoute("r", 1, "800007", 4, 1, "800008", 5, 1, false, 10_000, 1);
        var request = new AutoPlanningRequest(
            [route],
            new Dictionary<string, int> { ["Crow Coin"] = 10 },  // wrong key — ItemName instead of ItemID
            AutoPlanningStrategy.ProfitFirst, 10, 10, 1_000_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.Equal(0, result.Multipliers["r"]);
    }

    [Fact]
    public void Shared_item_inventory_accounts_for_all_consumers_and_producers() {
        var rA = Route("rA", 5, "A", 1, 1, "B", 2, 2, 1_000, 10); // A→2B producer
        var rC = Route("rC", 5, "B", 2, 1, "C", 3, 1, 1_000, 1); // B→C consumer
        var rD = Route("rD", 5, "B", 2, 1, "D", 4, 1, 1_000, 1); // B→D consumer
        var request = PlanProfit([rA, rC, rD],
            new Dictionary<string, int> { ["A"] = 10, ["B"] = 1 }, 100_000);

        var result = new PlannerAutoPlanner().Plan(request);

        int finalB = result.ProjectedInventory["B"];
        int producedB = result.Multipliers["rA"] * 2;
        int consumedB = result.Multipliers["rC"] * 1 + result.Multipliers["rD"] * 1;
        Assert.True(finalB >= 0);
        Assert.True(producedB >= consumedB - 1);
    }

    [Fact]
    public void Crow_first_maximizes_higher_coin_output_before_efficiency() {
        // Spec (design.md line 75–79): "higher coin output first; on tie, full
        // bundle efficiency". The higher-output route wins even though it's
        // less efficient per parley. Routes have Item1Level=4 (LV4) instead
        // of LV5 so they qualify under Crow remainder's LV4–LV6 input filter
        // and don't require LV5 reserve pull-in. Lv5Target=0 disables the
        // reserve for the test (focusing the test on coin-output ranking).
        var rA = Route("rA", 1, "A", 4, 1, "CrowCoin", 6, 190, 20_000, 1, crow: true);
        var rB = Route("rB", 1, "B", 4, 1, "CrowCoin", 6, 180, 10_000, 1, crow: true);
        var request = PlanStrategyNoReserve(AutoPlanningStrategy.CrowCoinFirst, [rA, rB],
            new Dictionary<string, int> { ["A"] = 10, ["B"] = 10 }, 20_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.True(result.Success);
        Assert.Equal(1, result.Multipliers["rA"]);
        Assert.Equal(0, result.Multipliers["rB"]);
        Assert.Equal(20_000, result.UsedParley);
    }

    [Fact]
    public void Crow_first_uses_bundle_efficiency_when_coin_output_ties() {
        // Same output (190 Crow), different bundle parley. Reserve disabled so
        // LV4 input has no deficit. rB wins on lower bundle parley.
        var rA = Route("rA", 1, "A", 4, 1, "CrowCoin", 6, 190, 20_000, 1, crow: true);
        var rB = Route("rB", 1, "B", 4, 1, "CrowCoin", 6, 190, 10_000, 1, crow: true);
        var request = PlanStrategyNoReserve(AutoPlanningStrategy.CrowCoinFirst, [rA, rB],
            new Dictionary<string, int> { ["A"] = 10, ["B"] = 10 }, 10_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.Equal(0, result.Multipliers["rA"]);
        Assert.Equal(1, result.Multipliers["rB"]);
        Assert.Equal(10_000, result.UsedParley);
    }

    [Fact]
    public void Crow_first_never_exceeds_budget_when_all_coin_routes_cannot_fit() {
        // Three Crow routes, each costs 400k, total budget 800k only fits 2.
        // Reserves disabled; routes use LV4 inputs so they qualify for the
        // Crow remainder filter without reserve pull-in.
        var rA = Route("rA", 1, "A", 4, 1, "CrowCoin", 6, 190, 400_000, 1, crow: true);
        var rB = Route("rB", 1, "B", 4, 1, "CrowCoin", 6, 190, 400_000, 1, crow: true);
        var rC = Route("rC", 1, "C", 4, 1, "CrowCoin", 6, 190, 400_000, 1, crow: true);
        var request = PlanStrategyNoReserve(AutoPlanningStrategy.CrowCoinFirst, [rA, rB, rC],
            new Dictionary<string, int> { ["A"] = 10, ["B"] = 10, ["C"] = 10 }, 800_000);

        var result = new PlannerAutoPlanner().Plan(request);

        int selected = result.Multipliers["rA"] + result.Multipliers["rB"] + result.Multipliers["rC"];
        Assert.Equal(2, selected);
        Assert.Equal(800_000, result.UsedParley);
    }

    [Fact]
    public void Crow_first_spends_remainder_in_lv4_then_lv5_then_lv6_input_order() {
        // No crow coin routes. Remainder phase: lowest input LV first.
        var rA = Route("rA", 1, "A", 4, 1, "B", 5, 1, 100_000, 1);
        var rB = Route("rB", 1, "B", 5, 1, "C", 6, 1, 100_000, 1);
        var rC = Route("rC", 1, "C", 6, 1, "D", 7, 1, 100_000, 1);
        var request = PlanStrategy(AutoPlanningStrategy.CrowCoinFirst, [rA, rB, rC],
            new Dictionary<string, int> { ["A"] = 10, ["B"] = 10, ["C"] = 10 }, 100_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.Equal(1, result.Multipliers["rA"]);
        Assert.Equal(0, result.Multipliers["rB"]);
        Assert.Equal(0, result.Multipliers["rC"]);
    }

    [Fact]
    public void Profit_first_excludes_every_crow_output() {
        // ProfitFirst filter excludes Item2Id == Crow. Both routes consume A
        // (LV6) which has no reserve (Lv6Target=0). rCrow's Item2Id is Crow
        // (filtered out), so only rLv7 commits.
        var rCrow = Route("rCrow", 1, "A", 6, 1, "CrowCoin", 6, 190, 5_000, 1, crow: true);
        var rLv7 = Route("rLv7", 1, "A", 6, 1, "Top", 7, 1, 5_000, 1);
        var request = PlanStrategyNoReserve(AutoPlanningStrategy.ProfitFirst, [rCrow, rLv7],
            new Dictionary<string, int> { ["A"] = 10 }, 10_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.Equal(0, result.Multipliers["rCrow"]);
        Assert.Equal(1, result.Multipliers["rLv7"]);
    }

    [Fact]
    public void Profit_first_prefers_lv6_to_lv7_over_lower_targets() {
        // ProfitFirst ranks by higher Item2Level (LV7 > LV6 > LV5), so rC wins
        // first among the LV4→LV5→LV6→LV7 chain. Budget 100_000 only fits one
        // 100k exchange, so rC commits; rA/rB stay at 0 (no chain pull-in
        // required because C=10 ≥ rC's demand).
        var rA = Route("rA", 1, "A", 4, 1, "B", 5, 1, 100_000, 1);
        var rB = Route("rB", 1, "B", 5, 1, "C", 6, 1, 100_000, 1);
        var rC = Route("rC", 1, "C", 6, 1, "D", 7, 1, 100_000, 1);
        var request = PlanStrategyNoReserve(AutoPlanningStrategy.ProfitFirst, [rA, rB, rC],
            new Dictionary<string, int> { ["A"] = 10, ["B"] = 10, ["C"] = 10 }, 100_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.Equal(0, result.Multipliers["rA"]);
        Assert.Equal(0, result.Multipliers["rB"]);
        Assert.Equal(1, result.Multipliers["rC"]);
    }

    [Fact]
    public void Strategy_ties_are_deterministic_by_row_id() {
        // rA/rB both LV6→LV7, both in ProfitFirst filter. Available A/B = 10,
        // reserve disabled so the LV6 input has no deficit. rA wins on row id
        // (alphabetical); result is deterministic regardless of input order.
        var rA = Route("rA", 1, "A", 6, 1, "Top", 7, 1, 5_000, 1);
        var rB = Route("rB", 1, "B", 6, 1, "Top", 7, 1, 5_000, 1);
        var fwdRequest = PlanStrategyNoReserve(AutoPlanningStrategy.ProfitFirst, [rA, rB],
            new Dictionary<string, int> { ["A"] = 10, ["B"] = 10 }, 5_000);
        var revRequest = PlanStrategyNoReserve(AutoPlanningStrategy.ProfitFirst, [rB, rA],
            new Dictionary<string, int> { ["A"] = 10, ["B"] = 10 }, 5_000);

        var fwdResult = new PlannerAutoPlanner().Plan(fwdRequest);
        var revResult = new PlannerAutoPlanner().Plan(revRequest);

        Assert.Equal(fwdResult.Multipliers, revResult.Multipliers);
        Assert.Equal(1, fwdResult.Multipliers["rA"]);
        Assert.Equal(0, fwdResult.Multipliers["rB"]);
    }

    [Fact]
    public void Restock_first_orders_lv5_and_lv6_by_deficit_ratio() {
        var request = PlanStrategy(AutoPlanningStrategy.RestockFirst,
            [
                Route("rLv5", 1, "Lv5In", 4, 1, "Lv5Out", 5, 1, 50_000, 1),
                Route("rLv6", 1, "Lv6In", 5, 1, "Lv6Out", 6, 1, 50_000, 1),
            ],
            new Dictionary<string, int> { ["Lv5In"] = 10, ["Lv6In"] = 10, ["Lv5Out"] = 1, ["Lv6Out"] = 5 }, 50_000);

        var result = new PlannerAutoPlanner().Plan(request);

        // LV5 deficit ratio = (10-1)/10 = 0.9 vs LV6 = (10-5)/10 = 0.5 → LV5 first.
        Assert.Equal(1, result.Multipliers["rLv5"]);
        Assert.Equal(0, result.Multipliers["rLv6"]);
    }

    [Fact]
    public void Restock_zero_target_disables_that_capped_tier() {
        // Lv5Target=0 means LV5 doesn't need restocking. LV6 target still active.
        var request = new AutoPlanningRequest(
            [
                Route("rLv5", 1, "Lv5In", 4, 1, "Lv5Out", 5, 1, 50_000, 1),
                Route("rLv6", 1, "Lv6In", 5, 1, "Lv6Out", 6, 1, 50_000, 1),
            ],
            new Dictionary<string, int> { ["Lv5In"] = 10, ["Lv6In"] = 10, ["Lv5Out"] = 1, ["Lv6Out"] = 5 },
            AutoPlanningStrategy.RestockFirst, 0, 10, 50_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.Equal(0, result.Multipliers["rLv5"]);
        Assert.Equal(1, result.Multipliers["rLv6"]);
    }

    [Fact]
    public void Restock_completes_capped_phase_before_lv1_to_lv4() {
        // LV5 below target AND LV1 also below — capped phase must beat uncapped.
        var request = PlanStrategy(AutoPlanningStrategy.RestockFirst,
            [
                Route("rLv5", 1, "Lv5In", 4, 1, "Lv5Out", 5, 1, 50_000, 1),
                Route("rLv1", 1, "Lv1In", 0, 1, "Lv1Out", 1, 1, 50_000, 1),
            ],
            new Dictionary<string, int> { ["Lv5In"] = 10, ["Lv1In"] = 10, ["Lv5Out"] = 1, ["Lv1Out"] = 0 }, 50_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.Equal(1, result.Multipliers["rLv5"]);
        Assert.Equal(0, result.Multipliers["rLv1"]);
    }

    [Fact]
    public void Restock_second_phase_always_selects_lowest_projected_inventory() {
        // LV1-LV4 producers with current stocks 1, 2, 3, 4 (each route already has its
        // input present, so the bundle is just the route). Lv5Target/Lv6Target are 0 so
        // phase 1 is empty and phase 2 runs the full budget. r1 has Remaining=2 so two
        // exchanges can stack onto the same row without bumping into the cap.
        var request = new AutoPlanningRequest(
            [
                Route("r1", 1, "A1", 1, 1, "B1", 2, 1, 25_000, 2),
                Route("r2", 1, "A2", 2, 1, "B2", 3, 1, 25_000, 1),
                Route("r3", 1, "A3", 3, 1, "B3", 4, 1, 25_000, 1),
                Route("r4", 1, "A4", 4, 1, "B4", 5, 1, 25_000, 1),
            ],
            new Dictionary<string, int> { ["A1"] = 10, ["A2"] = 10, ["A3"] = 10, ["A4"] = 10, ["B1"] = 1, ["B2"] = 2, ["B3"] = 3, ["B4"] = 4 },
            AutoPlanningStrategy.RestockFirst, 0, 0, 50_000);

        var result = new PlannerAutoPlanner().Plan(request);

        // First pick: B1=1 (lowest projected). After first commit B1=2 → still ≤ B2=2 (tied).
        // Second pick picks the tie winner by row id (r1 vs r2, r1 wins). r1 gets 2.
        Assert.Equal(2, result.Multipliers["r1"]);
        Assert.Equal(0, result.Multipliers["r2"]);
        Assert.Equal(0, result.Multipliers["r3"]);
        Assert.Equal(0, result.Multipliers["r4"]);
    }

    [Fact]
    public void Restock_lv1_to_lv4_has_no_hidden_cap() {
        var request = PlanStrategy(AutoPlanningStrategy.RestockFirst,
            [Route("rLv2", 1, "A", 1, 1, "B", 2, 1, 20_000, 5)],
            new Dictionary<string, int> { ["A"] = 100, ["B"] = 0 }, 100_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.Equal(5, result.Multipliers["rLv2"]);
    }

    [Fact]
    public void Restock_excludes_lv7_targets() {
        // Only an LV7-producing route exists — must stay at 0 in restock mode.
        var request = PlanStrategy(AutoPlanningStrategy.RestockFirst,
            [Route("rLv7", 1, "A", 6, 1, "B", 7, 1, 50_000, 1)],
            new Dictionary<string, int> { ["A"] = 10, ["B"] = 0 }, 50_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.Equal(0, result.Multipliers["rLv7"]);
    }

    [Fact]
    public void Restock_allows_only_unavoidable_indivisible_oversupply() {
        // LV5 stock 9, target 10, producer output 2 per exchange → one exchange overshoots
        // to 11 because the indivisible output exceeds the 1-unit deficit.
        var request = new AutoPlanningRequest(
            [Route("rLv5", 1, "Lv5In", 4, 1, "Lv5Out", 5, 2, 50_000, 1)],
            new Dictionary<string, int> { ["Lv5In"] = 10, ["Lv5Out"] = 9 },
            AutoPlanningStrategy.RestockFirst, 10, 10, 50_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.Equal(1, result.Multipliers["rLv5"]);
        Assert.Equal(11, result.ProjectedInventory["Lv5Out"]);
    }

    // ------------------------------------------------------------------
    // Plan-relevant reserve behavior. The Universal Reserve Phase (which
    // pre-fills every LV5/LV6 item before the strategy runs) must NOT
    // block strategies when an LV5/LV6 item is unused by the plan. Reserve
    // check fires only when a selected bundle consumes the item as Item1.
    // ------------------------------------------------------------------

    [Fact]
    public void Plan_relevant_reserve_does_not_block_Crow_Coin_on_unused_LV5() {
        // 800072 (LV5) has 0 inventory and only 4 Ancient island producer
        // exchanges (+1 each). Max LV5 = 5. CrowCoinFirst picks a Crow Coin
        // route (crowA consumes an LV5 item A that DOES have an in-group
        // producer aProd) and never touches 800072. The plan must succeed
        // and must NOT commit any Ancient producer exchanges — 800072 is
        // unconsumed inventory, not a target the user wants to pre-fill.
        var ancient = Route("ancient", 1, "Dagger", 4, 1, "800072", 5, 1, 1_000_000, 4);
        var aProd = Route("aProd", 2, "P", 1, 1, "A", 5, 1, 1_000, 5);
        var crowA = Route("crowA", 2, "A", 5, 1, AutoPlanningRoute.CrowCoinItemId, 6, 190, 60_000, 1, crow: true);
        var request = PlanStrategy(AutoPlanningStrategy.CrowCoinFirst,
            [ancient, aProd, crowA],
            new Dictionary<string, int> {
                ["800072"] = 0,
                ["Dagger"] = 100,
                ["P"] = 100,
                ["A"] = 10,
            },
            budget: 200_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.True(result.Success);
        Assert.Equal(0, result.Multipliers["ancient"]);
        Assert.Equal(1, result.Multipliers["crowA"]);
    }

    [Fact]
    public void Plan_relevant_reserve_does_not_block_on_unused_LV5_with_zero_inventory() {
        // LV5 item X has 0 inventory and no producer anywhere. Plan never
        // touches X. Plan must succeed because X is not plan-relevant.
        var route = Route("r", 1, "Y", 4, 1, "Z", 5, 1, 1_000, 5);
        var request = PlanStrategy(AutoPlanningStrategy.CrowCoinFirst,
            [route],
            new Dictionary<string, int> { ["Y"] = 10, ["Z"] = 5, ["X"] = 0 },
            budget: 10_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.True(result.Success);
    }

    [Fact]
    public void Plan_relevant_reserve_pulls_same_group_producer_for_consumed_LV5() {
        // Bundled LV5 consumer drops stock below target. Same-group producer
        // must be pulled into the bundle so projected inventory stays at or
        // above Max LV5 after commit. Budget is comfortably above every
        // iteration's cost so no reserve-budget-exceeded fires.
        var consumer = Route("consumer", 1, "X", 5, 1, "Top", 6, 1, 10_000, 3);
        var producer = Route("producer", 1, "Y", 1, 1, "X", 5, 1, 5_000, 10);
        var request = PlanStrategy(AutoPlanningStrategy.CrowCoinFirst,
            [consumer, producer],
            new Dictionary<string, int> { ["X"] = 10, ["Y"] = 100, ["Top"] = 0 },
            budget: 1_000_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.True(result.Success);
        Assert.True(result.Multipliers["consumer"] >= 1);
        Assert.True(result.Multipliers["producer"] >= 1);
        Assert.True(result.ProjectedInventory["X"] >= 10,
            $"projected X = {result.ProjectedInventory["X"]}, expected >= 10");
    }

    [Fact]
    public void Plan_relevant_reserve_fails_when_consumed_LV5_has_no_producer() {
        // Plan commits a bundle that consumes LV5 X. No same-group producer
        // for X exists. The plan must fail with reserve-no-producer.
        var consumer = Route("consumer", 1, "X", 5, 1, "Top", 6, 1, 10_000, 5);
        var request = PlanStrategy(AutoPlanningStrategy.CrowCoinFirst,
            [consumer],
            new Dictionary<string, int> { ["X"] = 10, ["Top"] = 0 },
            budget: 50_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == "reserve-no-producer");
    }

    [Fact]
    public void Plan_relevant_reserve_isolated_by_BarterGroup() {
        // Plan consumes LV5 X. The only producer for X is in a different
        // BarterGroup. Cross-group supply is forbidden. Plan must fail
        // with reserve-no-producer, NOT silently succeed.
        var consumer = Route("consumer", 7, "X", 5, 1, "Top", 6, 1, 10_000, 5);
        var crossProducer = Route("cross", 9, "Z", 1, 1, "X", 5, 1, 5_000, 10); // wrong group
        var request = PlanStrategy(AutoPlanningStrategy.CrowCoinFirst,
            [consumer, crossProducer],
            new Dictionary<string, int> { ["X"] = 10, ["Z"] = 100, ["Top"] = 0 },
            budget: 50_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == "reserve-no-producer");
    }

    [Fact]
    public void Plan_relevant_reserve_does_not_prefill_unused_LV5_under_Restock_first() {
        // RestockFirst must also not be blocked by an unconsumed LV5 item.
        // LV5 X has low stock and no in-group producer; the only restock
        // candidate is a LV1→LV2 route that never touches X.
        var lv2 = Route("lv2", 1, "A", 1, 1, "B", 2, 1, 1_000, 5);
        var request = new AutoPlanningRequest(
            [lv2],
            new Dictionary<string, int> { ["A"] = 100, ["B"] = 0, ["X"] = 0 },
            AutoPlanningStrategy.RestockFirst, 10, 10, 10_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.True(result.Success);
        Assert.Equal(5, result.Multipliers["lv2"]);
    }

    [Fact]
    public void Plan_relevant_reserve_does_not_prefill_unused_LV5_under_Profit_first() {
        // ProfitFirst must also not be blocked by an unconsumed LV5 item.
        // 800072 has 0 inventory and only 4 Ancient producers; the user's
        // profit target is a separate LV4→LV5 route that never touches
        // 800072. Plan-relevant reserve must keep the profit plan alive.
        var ancient = Route("ancient", 1, "Dagger", 4, 1, "800072", 5, 1, 1_000_000, 4);
        var profit = Route("profit", 2, "Common", 4, 1, "Top", 5, 1, 10_000, 5);
        var request = PlanStrategy(AutoPlanningStrategy.ProfitFirst,
            [ancient, profit],
            new Dictionary<string, int> {
                ["800072"] = 0,
                ["Dagger"] = 100,
                ["Common"] = 100,
                ["Top"] = 0,
            },
            budget: 100_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.True(result.Success);
        Assert.Equal(0, result.Multipliers["ancient"]);
        Assert.True(result.Multipliers["profit"] >= 1);
    }

    [Fact]
    public void Plan_relevant_reserve_preserves_LV1_LV3_exclusion_in_Crow_remainder() {
        // CrowCoinFirst remainder phase is restricted to LV4-LV6 inputs.
        // LV1-LV3 are never direct remainder candidates even when below target.
        var lv3 = Route("lv3", 1, "A", 1, 1, "B", 3, 1, 100_000, 1); // LV1→LV3
        var lv4to5 = Route("lv4to5", 2, "C", 4, 1, "D", 5, 1, 100_000, 1); // LV4→LV5
        var request = PlanStrategy(AutoPlanningStrategy.CrowCoinFirst,
            [lv3, lv4to5],
            new Dictionary<string, int> { ["A"] = 100, ["B"] = 0, ["C"] = 10, ["D"] = 0 },
            budget: 100_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.Equal(0, result.Multipliers["lv3"]);
        Assert.Equal(1, result.Multipliers["lv4to5"]);
    }

    [Fact]
    public void Plan_relevant_reserve_discards_unselected_candidate_failure_under_Profit() {
        // Group 1 has a viable LV4→LV5 route prefiller. Group 2 has a
        // consumer that consumes LV5 X with NO same-group producer. Both
        // appear in ProfitFirst candidates. Plan-relevant reserve semantics:
        // the consumer probe fails reserve-no-producer inside its LOCAL
        // diagnostics, but the strategy picks prefiller (the only viable
        // winner) and commits it. Consumer's reserve failure must be
        // discarded because consumer was NOT the winner. Success=true and
        // diagnostics must contain zero reserve-* entries.
        var prefiller = Route("prefiller", 1, "A", 4, 1, "B", 5, 1, 5_000, 5);
        var consumer = Route("consumer", 2, "X", 5, 1, "Top", 6, 1, 10_000, 5);
        var request = PlanStrategy(AutoPlanningStrategy.ProfitFirst,
            [prefiller, consumer],
            new Dictionary<string, int> { ["A"] = 10, ["B"] = 0, ["X"] = 10, ["Top"] = 0 },
            budget: 50_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.True(result.Success);
        Assert.True(result.Multipliers["prefiller"] >= 1);
        Assert.Equal(0, result.Multipliers["consumer"]);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code.StartsWith("reserve-"));
    }

    [Fact]
    public void Plan_relevant_reserve_discards_unselected_candidate_under_Crow_remainder() {
        // Same setup but under CrowCoinFirst remainder (LV4-LV6 input)
        // candidates, where the loser was a Crow-coin remainder candidate.
        var prefiller = Route("prefiller", 1, "A", 4, 1, "B", 5, 1, 5_000, 5);
        var consumer = Route("consumer", 2, "X", 5, 1, "Top", 6, 1, 10_000, 5);
        var request = PlanStrategy(AutoPlanningStrategy.CrowCoinFirst,
            [prefiller, consumer],
            new Dictionary<string, int> { ["A"] = 10, ["B"] = 0, ["X"] = 10, ["Top"] = 0 },
            budget: 50_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.True(result.Success);
        Assert.True(result.Multipliers["prefiller"] >= 1);
        Assert.Equal(0, result.Multipliers["consumer"]);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code.StartsWith("reserve-"));
    }

    private static AutoPlanningRequest PlanProfit(
        IReadOnlyList<AutoPlanningRoute> routes,
        IReadOnlyDictionary<string, int> inventory,
        int budget = 1_000_000) =>
        new(routes, inventory, AutoPlanningStrategy.ProfitFirst, 10, 10, budget);

    private static AutoPlanningRequest PlanStrategy(
        AutoPlanningStrategy strategy,
        IReadOnlyList<AutoPlanningRoute> routes,
        IReadOnlyDictionary<string, int> inventory,
        int budget = 1_000_000) =>
        new(routes, inventory, strategy, 10, 10, budget);

    private static AutoPlanningRequest PlanStrategyNoReserve(
        AutoPlanningStrategy strategy,
        IReadOnlyList<AutoPlanningRoute> routes,
        IReadOnlyDictionary<string, int> inventory,
        int budget = 1_000_000) =>
        new(routes, inventory, strategy, 0, 0, budget);

    private static AutoPlanningRoute Route(
        string id, int group, string item1, int lv1, int n1,
        string item2, int lv2, int n2, int parley, int remaining,
        bool crow = false) =>
        // When crow=true, the route's Item2Id is forced to the locale-independent
        // CrowCoinItemId constant ("10") rather than the descriptive "CrowCoin"
        // string the test author wrote. This matches the design-doc contract for
        // tests: only Item2Id == CrowCoinItemId is treated as a Crow route by
        // the service. item2 here is just a label used to make test sources
        // readable.
        new(id, group, item1, lv1, n1,
            crow ? AutoPlanningRoute.CrowCoinItemId : item2,
            lv2, n2, crow, parley, remaining);
}