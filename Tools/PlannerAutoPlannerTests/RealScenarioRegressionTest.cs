using iBarter.Planning;
using System.Collections.Generic;
using Xunit;

namespace PlannerAutoPlannerTests;

// Regression tests for the real WPF scenario:
//   贝尔利亚 has 5 锋利的红花刀箱子 (LV6), LV6 reserve = 5
//   杜鹃渡口 produces 锋利的红花刀箱子 from P
//   艾裴莉亚 consumes 锋利的红花刀箱子 and produces 黄金老鹰胸针
// The bug was that PlannerAutoPlanner's effectiveAvailable formula locked
// initial warehouse stock, forcing producer-before-consumer and making the
// final projected inventory depend on the chain order. The fix changes
// the formula to allow initial stock consumption, then enforces the
// final projected inventory via a post-strategy reserve check.
public sealed class RealScenarioRegressionTest {
    [Fact]
    public void Planner_allows_consuming_reserved_initial_stock_when_producer_is_also_planned() {
        // LV6 X is consumed (1 unit). Initial stock = 5, Lv6 target = 5.
        // Producer rProducer adds 1 X. Net final = 5 + 1 - 1 = 5 >= 5. OK.
        var consumer = new AutoPlanningRoute("consumer", 1, "X", 6, 1, "Top", 7, 1, false, 10_000, 1);
        var producer = new AutoPlanningRoute("producer", 1, "P", 1, 1, "X", 6, 1, false, 5_000, 10);
        var request = new AutoPlanningRequest(
            new[] { consumer, producer },
            new Dictionary<string, int> { ["X"] = 5, ["P"] = 100, ["Top"] = 0 },
            AutoPlanningStrategy.ProfitFirst, 0, 5, 1_000_000);
        var result = new PlannerAutoPlanner().Plan(request);
        Assert.True(result.Success);
        Assert.Equal(1, result.Multipliers["consumer"]);
        Assert.Equal(1, result.Multipliers["producer"]);
    }

    [Fact]
    public void Planner_final_reserve_check_fails_when_no_producer_available() {
        // LV5 X is consumed, no producer exists. Plan must fail.
        var request = new AutoPlanningRequest(
            new[] {
                new AutoPlanningRoute("consumer", 1, "X", 5, 1, "Top", 6, 1, false, 10_000, 5),
            },
            new Dictionary<string, int> { ["X"] = 10, ["Top"] = 0 },
            AutoPlanningStrategy.ProfitFirst, 10, 10, 50_000);
        var result = new PlannerAutoPlanner().Plan(request);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == "reserve-no-producer");
    }

    [Fact]
    public void Planner_final_reserve_check_passes_when_initial_stock_satisfies() {
        // LV5 X is consumed, initial stock 6 >= target 5. No producer needed.
        var request = new AutoPlanningRequest(
            new[] {
                new AutoPlanningRoute("consumer", 1, "X", 5, 1, "Top", 6, 1, false, 10_000, 1),
            },
            new Dictionary<string, int> { ["X"] = 6, ["Top"] = 0 },
            AutoPlanningStrategy.ProfitFirst, 5, 5, 50_000);
        var result = new PlannerAutoPlanner().Plan(request);
        Assert.True(result.Success);
        Assert.Equal(1, result.Multipliers["consumer"]);
    }
}
