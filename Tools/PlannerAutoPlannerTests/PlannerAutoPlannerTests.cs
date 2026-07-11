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
        var rA = Route("rA", 7, "A", 1, 1, "B", 2, 2, 20_000, 3);
        var rB = Route("rB", 7, "B", 2, 1, "C", 3, 1, 4_000, 5);
        var request = PlanProfit([rA, rB], new Dictionary<string, int> { ["A"] = 10, ["B"] = 0 }, 80_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.True(result.Success);
        Assert.Equal(3, result.Multipliers["rA"]);
        Assert.Equal(5, result.Multipliers["rB"]);
        Assert.Equal(1, result.ProjectedInventory["B"]);
    }

    [Fact]
    public void Reverse_supply_never_crosses_group() {
        var rA = Route("rA", 1, "A", 1, 1, "B", 2, 1, 1_000, 5);
        var rB = Route("rB", 2, "B", 2, 1, "C", 3, 1, 1_000, 5);
        var request = PlanProfit([rA, rB], new Dictionary<string, int> { ["A"] = 10, ["B"] = 0 }, 10_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.Equal(0, result.Multipliers["rA"]);
        Assert.Equal(0, result.Multipliers["rB"]);
    }

    [Fact]
    public void Three_level_reverse_chain_is_added_atomically() {
        var r1 = Route("r1", 3, "A", 1, 1, "B", 2, 1, 1_000, 5);
        var r2 = Route("r2", 3, "B", 2, 1, "C", 3, 1, 1_000, 5);
        var r3 = Route("r3", 3, "C", 3, 1, "D", 4, 1, 1_000, 2);
        var request = PlanProfit([r1, r2, r3],
            new Dictionary<string, int> { ["A"] = 10, ["B"] = 0, ["C"] = 0 }, 100_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.Equal(2, result.Multipliers["r1"]);
        Assert.Equal(2, result.Multipliers["r2"]);
        Assert.Equal(2, result.Multipliers["r3"]);
        Assert.All(result.ProjectedInventory.Values, v => Assert.True(v >= 0));
    }

    [Fact]
    public void Failed_bundle_leaves_every_multiplier_unchanged() {
        var r1 = Route("r1", 3, "A", 1, 1, "B", 2, 1, 1_000, 1); // remaining 1
        var r2 = Route("r2", 3, "B", 2, 1, "C", 3, 1, 1_000, 5);
        var r3 = Route("r3", 3, "C", 3, 1, "D", 4, 1, 1_000, 2);
        var request = PlanProfit([r1, r2, r3],
            new Dictionary<string, int> { ["A"] = 10, ["B"] = 0, ["C"] = 0 }, 100_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.Equal(0, result.Multipliers["r1"]);
        Assert.Equal(0, result.Multipliers["r2"]);
        Assert.Equal(0, result.Multipliers["r3"]);
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
        var rA = Route("rA", 1, "A", 5, 1, "CrowCoin", 6, 190, 20_000, 1, crow: true);
        var rB = Route("rB", 1, "B", 5, 1, "CrowCoin", 6, 180, 10_000, 1, crow: true);
        var request = PlanStrategy(AutoPlanningStrategy.CrowCoinFirst, [rA, rB],
            new Dictionary<string, int> { ["A"] = 10, ["B"] = 10 }, 20_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.True(result.Success);
        Assert.Equal(1, result.Multipliers["rA"]);
        Assert.Equal(0, result.Multipliers["rB"]);
        Assert.Equal(20_000, result.UsedParley);
    }

    [Fact]
    public void Crow_first_uses_bundle_efficiency_when_coin_output_ties() {
        var rA = Route("rA", 1, "A", 5, 1, "CrowCoin", 6, 190, 20_000, 1, crow: true);
        var rB = Route("rB", 1, "B", 5, 1, "CrowCoin", 6, 190, 10_000, 1, crow: true);
        var request = PlanStrategy(AutoPlanningStrategy.CrowCoinFirst, [rA, rB],
            new Dictionary<string, int> { ["A"] = 10, ["B"] = 10 }, 10_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.Equal(0, result.Multipliers["rA"]);
        Assert.Equal(1, result.Multipliers["rB"]);
        Assert.Equal(10_000, result.UsedParley);
    }

    [Fact]
    public void Crow_first_never_exceeds_budget_when_all_coin_routes_cannot_fit() {
        var rA = Route("rA", 1, "A", 5, 1, "CrowCoin", 6, 190, 400_000, 1, crow: true);
        var rB = Route("rB", 1, "B", 5, 1, "CrowCoin", 6, 190, 400_000, 1, crow: true);
        var rC = Route("rC", 1, "C", 5, 1, "CrowCoin", 6, 190, 400_000, 1, crow: true);
        var request = PlanStrategy(AutoPlanningStrategy.CrowCoinFirst, [rA, rB, rC],
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
        var rCrow = Route("rCrow", 1, "A", 6, 1, "CrowCoin", 6, 190, 5_000, 1, crow: true);
        var rLv7 = Route("rLv7", 1, "A", 6, 1, "Top", 7, 1, 5_000, 1);
        var request = PlanStrategy(AutoPlanningStrategy.ProfitFirst, [rCrow, rLv7],
            new Dictionary<string, int> { ["A"] = 10 }, 10_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.Equal(0, result.Multipliers["rCrow"]);
        Assert.Equal(1, result.Multipliers["rLv7"]);
    }

    [Fact]
    public void Profit_first_prefers_lv6_to_lv7_over_lower_targets() {
        var rA = Route("rA", 1, "A", 4, 1, "B", 5, 1, 100_000, 1);
        var rB = Route("rB", 1, "B", 5, 1, "C", 6, 1, 100_000, 1);
        var rC = Route("rC", 1, "C", 6, 1, "D", 7, 1, 100_000, 1);
        var request = PlanStrategy(AutoPlanningStrategy.ProfitFirst, [rA, rB, rC],
            new Dictionary<string, int> { ["A"] = 10, ["B"] = 10, ["C"] = 10 }, 100_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.Equal(0, result.Multipliers["rA"]);
        Assert.Equal(0, result.Multipliers["rB"]);
        Assert.Equal(1, result.Multipliers["rC"]);
    }

    [Fact]
    public void Strategy_ties_are_deterministic_by_row_id() {
        var rA = Route("rA", 1, "A", 6, 1, "Top", 7, 1, 5_000, 1);
        var rB = Route("rB", 1, "B", 6, 1, "Top", 7, 1, 5_000, 1);
        var fwdRequest = PlanStrategy(AutoPlanningStrategy.ProfitFirst, [rA, rB],
            new Dictionary<string, int> { ["A"] = 10, ["B"] = 10 }, 5_000);
        var revRequest = PlanStrategy(AutoPlanningStrategy.ProfitFirst, [rB, rA],
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

    private static AutoPlanningRoute Route(
        string id, int group, string item1, int lv1, int n1,
        string item2, int lv2, int n2, int parley, int remaining,
        bool crow = false) =>
        new(id, group, item1, lv1, n1, item2, lv2, n2, crow, parley, remaining);
}