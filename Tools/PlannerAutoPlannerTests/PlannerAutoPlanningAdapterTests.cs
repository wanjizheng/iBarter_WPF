using iBarter.Planning;
using Xunit;

namespace PlannerAutoPlannerTests;

public sealed class PlannerAutoPlanningAdapterTests {
    [Fact]
    public void Manual_selection_preserves_every_existing_multiplier() {
        var adapter = new PlannerAutoPlanningAdapter();
        var selected = Snapshot("selected", exchangeDone: false, existingMultiplier: 3,
            new AutoPlanningRoute(
                "selected", 1, "A", 4, 2, "B", 5, 1,
                false, 12_000, 5));
        var unselected = Snapshot("unselected", exchangeDone: false, existingMultiplier: 0,
            new AutoPlanningRoute(
                "unselected", 1, "C", 4, 1, "D", 5, 1,
                false, 8_000, 5));
        var finished = Snapshot("finished", exchangeDone: true, existingMultiplier: 2,
            new AutoPlanningRoute(
                "finished", 1, "E", 4, 1, "F", 5, 1,
                false, 5_000, 5));

        var calculation = adapter.Calculate(
            [selected, unselected, finished],
            new Dictionary<string, int>(),
            AutoPlanningStrategy.ManualSelection,
            10,
            10,
            1_000_000);

        Assert.NotNull(calculation.ApplySet);
        Assert.Equal(3, calculation.ApplySet!.Multipliers["selected"]);
        Assert.Equal(0, calculation.ApplySet.Multipliers["unselected"]);
        Assert.Equal(2, calculation.ApplySet.Multipliers["finished"]);
        Assert.Equal(36_000, calculation.UsedParley);
    }

    [Fact]
    public void Manual_selection_rejects_a_negative_multiplier_without_applying_changes() {
        var adapter = new PlannerAutoPlanningAdapter();
        var invalid = Snapshot("invalid", exchangeDone: false, existingMultiplier: -1,
            new AutoPlanningRoute(
                "invalid", 1, "A", 4, 1, "B", 5, 1,
                false, 10_000, 5));

        var calculation = adapter.Calculate(
            [invalid],
            new Dictionary<string, int>(),
            AutoPlanningStrategy.ManualSelection,
            10,
            10,
            1_000_000);

        Assert.Null(calculation.ApplySet);
        Assert.Contains(calculation.Diagnostics,
            diagnostic => diagnostic.Code == "manual-negative-multiplier");
    }

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

    [Fact]
    public void Calculate_returns_no_apply_set_when_reserve_unfillable() {
        // Plan-relevant reserve: when a bundle consumes LV5 stock that cannot
        // be replenished via same-group producers, the planner must surface
        // a reserve-* diagnostic and the adapter must return ApplySet=null
        // so no partial multiplier is applied to the live WPF rows.
        var adapter = new PlannerAutoPlanningAdapter();
        var consumer = Snapshot("consumer", exchangeDone: false, existingMultiplier: 0,
            new AutoPlanningRoute("consumer", 1, "X", 5, 1, "Top", 6, 1, false, 10_000, 5));
        var inventory = new Dictionary<string, int> { ["X"] = 10, ["Top"] = 0 };

        var calculation = adapter.Calculate(
            [consumer], inventory, AutoPlanningStrategy.CrowCoinFirst, 10, 10, 1_000_000);

        Assert.Null(calculation.ApplySet);
        Assert.NotEmpty(calculation.Diagnostics);
        Assert.Contains(calculation.Diagnostics, d => d.Code.StartsWith("reserve-"));
    }

    private static PlannerRowSnapshot Snapshot(
        string rowId, bool exchangeDone, int existingMultiplier, AutoPlanningRoute route) =>
        new(rowId, exchangeDone, existingMultiplier, route);
}
