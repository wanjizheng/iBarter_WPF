using System.Diagnostics;
using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

// Committed regression coverage for the interdependent 15-task/4-warehouse
// crow-coin scenario that reproduced the ~184s field blowup. These assert
// operation-level correctness plus a loose time ceiling (the real target is
// well under 3s; the ceiling is generous to avoid flakiness on shared CI).
public sealed class InterdependentPlanTests {
    [Fact]
    public void Interdependent_15_task_plan_is_fast_and_verified() {
        var request = RoutePlannerBenchmark.InterdependentCrowCoinPlan(
            15, new RouteSearchLimits(250_000, 2_000));

        var sw = Stopwatch.StartNew();
        var plan = new AutomaticRoutePlanner().Plan(request, TestContext.Current.CancellationToken);
        sw.Stop();

        Assert.Equal(RoutePlanStatus.BestKnownWithinLimit, plan.Status);
        Assert.True(RoutePlanVerifier.Verify(request, plan).Success);
        Assert.NotNull(plan.Objective);
        Assert.True(plan.Objective!.Value.RouteCount >= 1);
        // Loose ceiling: locally ~2s. The wall-clock hard cap is 5s inside the
        // planner, so any regression that reintroduces the exponential blowup
        // (previously 75s+) trips this well before CI variance matters.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20),
            $"interdependent 15-task plan took {sw.Elapsed}");
    }

    [Fact]
    public void Interdependent_plan_is_deterministic() {
        var a = Solve();
        var b = Solve();

        Assert.Equal(a.Status, b.Status);
        Assert.Equal(a.Objective!.Value.RouteCount, b.Objective!.Value.RouteCount);
        Assert.Equal(a.Objective!.Value.TotalDistance, b.Objective!.Value.TotalDistance);
        Assert.Equal(a.Objective!.Value.PickupStopCount, b.Objective!.Value.PickupStopCount);
        Assert.Equal(a.Objective!.Value.MaxPeakLT, b.Objective!.Value.MaxPeakLT);
        Assert.Equal(a.Objective!.Value.StableTieBreak, b.Objective!.Value.StableTieBreak);

        static RoutePlan Solve() {
            var request = RoutePlannerBenchmark.InterdependentCrowCoinPlan(
                15, new RouteSearchLimits(250_000, 2_000));
            return new AutomaticRoutePlanner().Plan(request, TestContext.Current.CancellationToken);
        }
    }
}
