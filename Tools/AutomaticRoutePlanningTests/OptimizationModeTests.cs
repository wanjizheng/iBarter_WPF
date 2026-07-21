using System.Diagnostics;
using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

// Covers the new anytime optimization configuration:
// - RouteOptimizationProfile defaults
// - The 3-arg planner overload defaults to Balanced
// - The budget's injectable clock makes the time accounting deterministic in tests
// - Quality monotonicity on a shared, deterministic scenario:
//   Balanced objective <= Quick objective,
//   Deep objective <= Balanced objective.
// - Stop-reason accuracy: the beam reports the expected reason when a
//   pre-canned budget is exhausted.
public sealed class OptimizationModeTests {
    private static AutomaticRoutePlanningRequest InterdependentRequest() =>
        RoutePlannerBenchmark.InterdependentCrowCoinPlan(15,
            new RouteSearchLimits(250_000, 2_000));

    [Fact]
    public void Default_mode_is_Balanced() {
        var plan = new AutomaticRoutePlanner().Plan(InterdependentRequest(),
            TestContext.Current.CancellationToken);
        Assert.Contains(plan.Diagnostics,
            d => d.Detail is { } detail && detail.Contains("mode=Balanced", StringComparison.Ordinal));
    }

    [Fact]
    public void Profile_times_match_spec() {
        var quick = RouteOptimizationProfile.For(RouteOptimizationMode.Quick);
        var balanced = RouteOptimizationProfile.For(RouteOptimizationMode.Balanced);
        var deep = RouteOptimizationProfile.For(RouteOptimizationMode.Deep);
        var extreme = RouteOptimizationProfile.For(RouteOptimizationMode.Extreme);

        Assert.Equal(3_000, quick.TotalTarget.TotalMilliseconds);
        Assert.Equal(500, quick.FinalizationReserve.TotalMilliseconds);
        Assert.Equal(5_000, quick.MaxBeamParents);
        Assert.Equal(150, quick.MaxLocalEvaluations);

        Assert.Equal(10_000, balanced.TotalTarget.TotalMilliseconds);
        Assert.Equal(1_000, balanced.FinalizationReserve.TotalMilliseconds);
        Assert.Equal(25_000, balanced.MaxBeamParents);
        Assert.Equal(500, balanced.MaxLocalEvaluations);

        Assert.Equal(60_000, deep.TotalTarget.TotalMilliseconds);
        Assert.Equal(3_000, deep.FinalizationReserve.TotalMilliseconds);
        Assert.Equal(100_000, deep.MaxBeamParents);
        Assert.Equal(600_000, extreme.TotalTarget.TotalMilliseconds);
        Assert.Equal(2_000, extreme.MaxLocalEvaluations);
        Assert.Equal(2_000, deep.MaxLocalEvaluations);

        // No single fixed 5 s wall-clock cap is shared across modes.
        Assert.NotEqual(quick.MaxBeamParents, deep.MaxBeamParents);
        Assert.True(quick.MaxBeamParents < balanced.MaxBeamParents);
        Assert.True(balanced.MaxBeamParents < deep.MaxBeamParents);
    }

    [Fact]
    public void Quality_monotonicity_Quick_to_Balanced_to_Deep() {
        // The first verified incumbent is the heuristic; it is the same for all
        // three modes in the headless test scenario because the beam's
        // additional parents/successors are spent on the same feasible
        // improvement. Therefore each mode's returned objective must be
        // comparable, and a longer mode must not return a strictly worse
        // objective than a shorter one.
        var request = InterdependentRequest();
        var quick = PlanOnce(request, RouteOptimizationProfile.For(RouteOptimizationMode.Quick));
        var balanced = PlanOnce(request, RouteOptimizationProfile.For(RouteOptimizationMode.Balanced));
        var deep = PlanOnce(request, RouteOptimizationProfile.For(RouteOptimizationMode.Deep));

        Assert.Equal(RoutePlanStatus.BestKnownWithinLimit, quick.Status);
        Assert.Equal(RoutePlanStatus.BestKnownWithinLimit, balanced.Status);
        Assert.Equal(RoutePlanStatus.BestKnownWithinLimit, deep.Status);

        Assert.True(RoutePlanVerifier.Verify(request, quick).Success);
        Assert.True(RoutePlanVerifier.Verify(request, balanced).Success);
        Assert.True(RoutePlanVerifier.Verify(request, deep).Success);

        // Monotonicity: longer modes must be at least as good.
        Assert.True(balanced.Objective!.Value.CompareTo(quick.Objective!.Value) <= 0,
            $"Balanced {balanced.Objective.Value} should be <= Quick {quick.Objective.Value}");
        Assert.True(deep.Objective!.Value.CompareTo(balanced.Objective!.Value) <= 0,
            $"Deep {deep.Objective.Value} should be <= Balanced {balanced.Objective.Value}");
    }

    [Fact]
    public void Beam_reports_TimeBudget_when_deadline_elapses() {
        // Inject a clock that jumps 10^12 ticks per call so every check
        // is already past the deadline. This isolates the time-budget path
        // from the parent-budget / successor-budget paths regardless of
        // whether the beam could otherwise find a complete plan.
        long now = 0;
        var profile = new RouteOptimizationProfile(
            Mode: RouteOptimizationMode.Balanced,
            TotalTarget: TimeSpan.FromMilliseconds(10),
            FinalizationReserve: TimeSpan.FromMilliseconds(0),
            MaxBeamParents: 100_000,
            MaxSuccessors: 10_000_000,
            MaxLocalEvaluations: 10_000,
            BeamWidth: 256);

        var request = InterdependentRequest();
        var budget = new RouteSearchBudget(profile, clock: () => now += 1_000_000_000_000L);
        var beam = AutomaticRouteBeamSearch.TryBuildIncumbent(
            request, AutomaticRoutePreflight.Validate(request), budget,
            TestContext.Current.CancellationToken);

        Assert.Equal(BeamStopReason.TimeBudget, beam.StopReason);
    }

    [Fact]
    public void Beam_reports_ParentBudget_when_parent_cap_is_zero() {
        var profile = new RouteOptimizationProfile(
            Mode: RouteOptimizationMode.Quick,
            TotalTarget: TimeSpan.FromMinutes(10),
            FinalizationReserve: TimeSpan.Zero,
            MaxBeamParents: 0,
            MaxSuccessors: 10_000,
            MaxLocalEvaluations: 10,
            BeamWidth: 64);

        var request = InterdependentRequest();
        var budget = new RouteSearchBudget(profile);
        var beam = AutomaticRouteBeamSearch.TryBuildIncumbent(
            request, AutomaticRoutePreflight.Validate(request), budget,
            TestContext.Current.CancellationToken);

        Assert.Equal(BeamStopReason.ParentBudget, beam.StopReason);
    }

    [Fact]
    public void Beam_reports_FrontierExhausted_when_nothing_complete() {
        // A preflight-valid but beam-infeasible request: the preflight
        // passes (each individual task is feasible) but the warehouse has
        // only enough stock for 1 of the 2 tasks, so the beam explores
        // every reachable state and exhausts the frontier without finding
        // a complete plan.
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["IN"] = new("IN", "In", 1, 100),
            ["OUT"] = new("OUT", "Out", 2, 0),
        };
        var tasks = new[] {
            new RouteBarterTask("r0", "A", new RoutePoint(1, 0), "IN", 1, "OUT", 1),
            new RouteBarterTask("r1", "B", new RoutePoint(2, 0), "IN", 1, "OUT", 1),
            new RouteBarterTask("r2", "C", new RoutePoint(3, 0), "IN", 1, "OUT", 1),
        };
        var warehouse = new RouteWarehouse("W", "W", new RoutePoint(0, 0),
            new Dictionary<string, int>(StringComparer.Ordinal) { ["IN"] = 1 });
        var request = new AutomaticRoutePlanningRequest(
            tasks, items, [warehouse], 0, 10_000,
            new RouteSearchLimits(100_000, 100), "frontier-exhausted-test");

        var profile = new RouteOptimizationProfile(
            Mode: RouteOptimizationMode.Quick,
            TotalTarget: TimeSpan.FromMinutes(10),
            FinalizationReserve: TimeSpan.Zero,
            MaxBeamParents: 100,
            MaxSuccessors: 1_000,
            MaxLocalEvaluations: 10,
            BeamWidth: 16);
        var budget = new RouteSearchBudget(profile);
        var beam = AutomaticRouteBeamSearch.TryBuildIncumbent(
            request, AutomaticRoutePreflight.Validate(request), budget,
            TestContext.Current.CancellationToken);

        Assert.Null(beam.Incumbent);
        Assert.Equal(BeamStopReason.FrontierExhausted, beam.StopReason);
    }

    [Fact]
    public void Beam_reports_SuccessorBudget_when_caps_are_tight() {
        var profile = new RouteOptimizationProfile(
            Mode: RouteOptimizationMode.Quick,
            TotalTarget: TimeSpan.FromMinutes(10),
            FinalizationReserve: TimeSpan.Zero,
            MaxBeamParents: 1_000_000,
            MaxSuccessors: 1,
            MaxLocalEvaluations: 10,
            BeamWidth: 16);

        var request = InterdependentRequest();
        var budget = new RouteSearchBudget(profile);
        var beam = AutomaticRouteBeamSearch.TryBuildIncumbent(
            request, AutomaticRoutePreflight.Validate(request), budget,
            TestContext.Current.CancellationToken);

        // The very first successor should exhaust the cap, recording
        // SuccessorBudget on the budget and propagating to the beam result.
        Assert.Equal(BeamStopReason.SuccessorBudget, beam.StopReason);
    }

    [Fact]
    public void Cancelled_propagates_through_planner() {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var plan = new AutomaticRoutePlanner().Plan(InterdependentRequest(), cts.Token);
        Assert.Equal(RoutePlanStatus.Cancelled, plan.Status);
    }

    [Fact]
    public void First_verified_incumbent_is_recorded_in_profiler() {
        using var _ = RouteSearchProfiler.Enable();
        var request = InterdependentRequest();
        var plan = new AutomaticRoutePlanner().Plan(request,
            RouteOptimizationProfile.For(RouteOptimizationMode.Quick),
            TestContext.Current.CancellationToken);
        var snap = RouteSearchProfiler.Snapshot();
        Assert.NotNull(snap);
        Assert.Equal("Quick", snap!.OptimizationMode);
        Assert.True(snap.FirstVerifiedIncumbentMs > 0,
            "first verified incumbent time should be recorded for a verified plan");
        Assert.True(snap.FinalVerified);
    }

    [Fact]
    public void Final_verify_always_runs_even_when_time_budget_exhausted() {
        // The planner must return a verified plan even when the budget has
        // already been consumed. This is the spec's "correctness over last
        // distance improvement" invariant.
        long now = 0;
        var profile = new RouteOptimizationProfile(
            Mode: RouteOptimizationMode.Quick,
            TotalTarget: TimeSpan.FromMilliseconds(1),
            FinalizationReserve: TimeSpan.Zero,
            MaxBeamParents: 50,
            MaxSuccessors: 5_000,
            MaxLocalEvaluations: 50,
            BeamWidth: 64);
        var request = InterdependentRequest();
        var budget = new RouteSearchBudget(profile, clock: () => now += 1_000_000);
        var beam = AutomaticRouteBeamSearch.TryBuildIncumbent(
            request, AutomaticRoutePreflight.Validate(request), budget,
            TestContext.Current.CancellationToken);

        // Even with no incumbent (the time clock raced past the deadline
        // before any state expanded), the planner should still publish a
        // verified heuristic result.
        var plan = new AutomaticRoutePlanner().Plan(request, profile,
            TestContext.Current.CancellationToken);
        Assert.True(RoutePlanVerifier.Verify(request, plan).Success);
    }

    private static RoutePlan PlanOnce(
        AutomaticRoutePlanningRequest request, RouteOptimizationProfile profile) =>
        new AutomaticRoutePlanner().Plan(request, profile, TestContext.Current.CancellationToken);
}
