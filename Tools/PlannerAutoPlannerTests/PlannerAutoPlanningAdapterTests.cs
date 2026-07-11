using iBarter.Planning;
using Xunit;

namespace PlannerAutoPlannerTests;

public sealed class PlannerAutoPlanningAdapterTests {
    [Fact]
    public void Calculate_preserves_ck_multiplier_and_replaces_unfinished_from_zero() {
        var adapter = new PlannerAutoPlanningAdapter();
        var ckRoute = Snapshot("ck", exchangeDone: true, existingMultiplier: 3,
            new AutoPlanningRoute("ck", 1, "A", 4, 1, "B", 5, 1, false, 10_000, 5));
        var unfinishedRoute = Snapshot("un", exchangeDone: false, existingMultiplier: 4,
            new AutoPlanningRoute("un", 1, "C", 4, 1, "D", 5, 1, false, 10_000, 1));
        var inventory = new Dictionary<string, int> { ["A"] = 10, ["C"] = 10 };

        var calculation = adapter.Calculate(
            [ckRoute, unfinishedRoute], inventory, AutoPlanningStrategy.ProfitFirst, 10, 10, 1_000_000);

        Assert.NotNull(calculation.ApplySet);
        Assert.Equal(3, calculation.ApplySet!.Multipliers["ck"]);
        // The unfinished row is replaced from a zero baseline; the fresh Profit plan
        // for the only remaining route lands at its full Remaining=1.
        Assert.Equal(1, calculation.ApplySet.Multipliers["un"]);
    }

    [Fact]
    public void Calculate_returns_no_apply_set_when_service_fails() {
        var adapter = new PlannerAutoPlanningAdapter();
        var rA = Snapshot("rA", exchangeDone: false, existingMultiplier: 0,
            new AutoPlanningRoute("rA", 1, "A", 4, 1, "B", 5, 1, false, 10_000, 5));
        // Duplicate RowId forces PlannerAutoPlanner to fail with diagnostic.
        var dup = Snapshot("rA", exchangeDone: false, existingMultiplier: 0,
            new AutoPlanningRoute("rA", 1, "C", 4, 1, "D", 5, 1, false, 10_000, 5));
        var inventory = new Dictionary<string, int> { ["A"] = 10, ["C"] = 10 };

        var calculation = adapter.Calculate(
            [rA, dup], inventory, AutoPlanningStrategy.ProfitFirst, 10, 10, 1_000_000);

        Assert.Null(calculation.ApplySet);
        Assert.NotEmpty(calculation.Diagnostics);
    }

    [Fact]
    public void Calculate_all_zero_success_is_still_applicable() {
        var adapter = new PlannerAutoPlanningAdapter();
        // Route is unaffordable — profit plan returns a successful zero plan with a
        // no-feasible-candidate diagnostic, but the adapter must still produce an
        // apply set so the UI can keep the existing zero multipliers unchanged.
        var r = Snapshot("r", exchangeDone: false, existingMultiplier: 0,
            new AutoPlanningRoute("r", 1, "A", 4, 1, "B", 5, 1, false, 100_000_000, 5));
        var inventory = new Dictionary<string, int> { ["A"] = 10 };

        var calculation = adapter.Calculate(
            [r], inventory, AutoPlanningStrategy.ProfitFirst, 10, 10, 1_000_000);

        Assert.NotNull(calculation.ApplySet);
        Assert.Equal(0, calculation.ApplySet!.Multipliers["r"]);
    }

    private static PlannerRowSnapshot Snapshot(
        string rowId, bool exchangeDone, int existingMultiplier, AutoPlanningRoute route) =>
        new(rowId, exchangeDone, existingMultiplier, route);
}