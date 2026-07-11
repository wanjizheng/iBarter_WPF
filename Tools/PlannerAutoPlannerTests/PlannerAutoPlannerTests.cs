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

    private static AutoPlanningRequest PlanProfit(
        IReadOnlyList<AutoPlanningRoute> routes,
        IReadOnlyDictionary<string, int> inventory,
        int budget = 1_000_000) =>
        new(routes, inventory, AutoPlanningStrategy.ProfitFirst, 10, 10, budget);

    private static AutoPlanningRoute Route(
        string id, int group, string item1, int lv1, int n1,
        string item2, int lv2, int n2, int parley, int remaining,
        bool crow = false) =>
        new(id, group, item1, lv1, n1, item2, lv2, n2, crow, parley, remaining);
}