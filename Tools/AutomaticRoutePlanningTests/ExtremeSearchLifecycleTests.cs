using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public sealed class ExtremeSearchLifecycleTests {
    [Fact]
    public void Extreme_stops_at_the_ten_minute_maximum() {
        var (controller, clock) = CreateController();

        clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(
            ExtremeSearchTerminationReason.MaxDurationReached,
            controller.EvaluateTermination());
        Assert.Equal(TimeSpan.FromMinutes(10), controller.Snapshot().Elapsed);
    }

    [Fact]
    public void Extreme_converges_after_ninety_seconds_without_improvement() {
        var (controller, clock) = CreateController();
        Assert.True(controller.TryAcceptCandidate(Plan(distance: 200)));

        clock.Advance(TimeSpan.FromSeconds(90));

        Assert.Equal(
            ExtremeSearchTerminationReason.NoImprovementConverged,
            controller.EvaluateTermination());
    }

    [Fact]
    public void Better_candidate_at_eighty_seconds_restarts_the_ninety_second_window() {
        var (controller, clock) = CreateController();
        controller.TryAcceptCandidate(Plan(distance: 200));
        clock.Advance(TimeSpan.FromSeconds(80));
        Assert.True(controller.TryAcceptCandidate(Plan(distance: 150)));

        clock.Advance(TimeSpan.FromSeconds(89));
        Assert.Equal(ExtremeSearchTerminationReason.None, controller.EvaluateTermination());
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(
            ExtremeSearchTerminationReason.NoImprovementConverged,
            controller.EvaluateTermination());
        Assert.Equal(TimeSpan.FromSeconds(80), controller.LastImprovementElapsed);
    }

    [Fact]
    public void Pending_convergence_does_not_seal_before_the_final_slice_candidate_is_read() {
        var (controller, clock) = CreateController();
        controller.TryAcceptCandidate(Plan(distance: 200));
        clock.Advance(TimeSpan.FromSeconds(90));

        Assert.Equal(
            ExtremeSearchTerminationReason.NoImprovementConverged,
            controller.CheckTermination());
        Assert.Equal(ExtremeSearchTerminationReason.None, controller.TerminationReason);
        Assert.True(controller.TryAcceptCandidate(Plan(distance: 150)));
        Assert.Equal(TimeSpan.FromSeconds(90), controller.LastImprovementElapsed);
        Assert.Equal(ExtremeSearchTerminationReason.None, controller.EvaluateTermination());

        clock.Advance(TimeSpan.FromSeconds(90));
        Assert.Equal(
            ExtremeSearchTerminationReason.NoImprovementConverged,
            controller.EvaluateTermination());
    }

    [Fact]
    public void Equal_objective_does_not_reset_the_convergence_window() {
        var (controller, clock) = CreateController();
        RoutePlan first = Plan(distance: 200);
        controller.TryAcceptCandidate(first);
        clock.Advance(TimeSpan.FromSeconds(80));

        Assert.False(controller.TryAcceptCandidate(Plan(distance: 200)));
        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(
            ExtremeSearchTerminationReason.NoImprovementConverged,
            controller.EvaluateTermination());
        Assert.Same(first, controller.BestPlan);
        Assert.Equal(TimeSpan.Zero, controller.LastImprovementElapsed);
    }

    [Fact]
    public void Worse_objective_does_not_reset_the_convergence_window() {
        var (controller, clock) = CreateController();
        RoutePlan first = Plan(distance: 200);
        controller.TryAcceptCandidate(first);
        clock.Advance(TimeSpan.FromSeconds(80));

        Assert.False(controller.TryAcceptCandidate(Plan(distance: 250)));
        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(
            ExtremeSearchTerminationReason.NoImprovementConverged,
            controller.EvaluateTermination());
        Assert.Same(first, controller.BestPlan);
    }

    [Fact]
    public void Improvement_uses_the_complete_authoritative_route_objective() {
        var (controller, clock) = CreateController();
        controller.TryAcceptCandidate(Plan(distance: 200, routeCount: 2));
        clock.Advance(TimeSpan.FromSeconds(80));

        Assert.True(controller.TryAcceptCandidate(
            Plan(distance: 200, routeCount: 1)));
        Assert.Equal(TimeSpan.FromSeconds(80), controller.LastImprovementElapsed);
    }

    [Fact]
    public void No_solution_never_uses_the_no_improvement_stop() {
        var (controller, clock) = CreateController();
        clock.Advance(TimeSpan.FromSeconds(590));

        Assert.Equal(ExtremeSearchTerminationReason.None, controller.EvaluateTermination());
        Assert.Null(controller.Snapshot().ElapsedSinceLastImprovement);

        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(
            ExtremeSearchTerminationReason.MaxDurationReached,
            controller.EvaluateTermination());
    }

    [Fact]
    public void Custom_duration_disables_no_improvement_but_still_honors_its_deadline() {
        var clock = new FakeMonotonicClock();
        var controller = new ExtremeSearchController(
            TimeSpan.FromMinutes(5),
            TimeSpan.Zero,
            clock);
        RoutePlan best = Plan(distance: 200);
        Assert.True(controller.TryAcceptCandidate(best));

        clock.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(ExtremeSearchTerminationReason.None, controller.EvaluateTermination());
        Assert.Equal(TimeSpan.FromMinutes(4),
            controller.Snapshot().ElapsedSinceLastImprovement);

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(
            ExtremeSearchTerminationReason.MaxDurationReached,
            controller.EvaluateTermination());
        Assert.Same(best, controller.BestPlan);
    }

    [Fact]
    public void Early_stop_retains_the_best_candidate_from_the_entire_search() {
        var (controller, clock) = CreateController();
        controller.TryAcceptCandidate(Plan(distance: 300));
        RoutePlan best = Plan(distance: 100);
        clock.Advance(TimeSpan.FromSeconds(20));
        controller.TryAcceptCandidate(best);
        clock.Advance(TimeSpan.FromSeconds(10));
        controller.TryAcceptCandidate(Plan(distance: 200));
        clock.Advance(TimeSpan.FromSeconds(80));

        Assert.Equal(
            ExtremeSearchTerminationReason.NoImprovementConverged,
            controller.EvaluateTermination());
        Assert.Same(best, controller.BestPlan);
        Assert.Equal(100, controller.Snapshot().BestObjective?.TotalDistance);
    }

    [Fact]
    public void User_cancellation_has_priority_and_retains_the_pre_cancel_best() {
        var (controller, clock) = CreateController();
        RoutePlan best = Plan(distance: 100);
        controller.TryAcceptCandidate(best);
        clock.Advance(TimeSpan.FromSeconds(90));

        Assert.Equal(
            ExtremeSearchTerminationReason.UserCancelled,
            controller.EvaluateTermination(userCancellationRequested: true));
        Assert.Same(best, controller.BestPlan);
    }

    [Fact]
    public void Solver_client_returns_the_pre_cancel_best_when_already_cancelled() {
        RoutePlan best = Plan(distance: 100);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        ExtremeRouteSolverRunResult result = ExtremeRouteSolverClient.Solve(
            EmptyRequest(),
            best,
            TimeSpan.FromMinutes(10),
            new ExtremeRouteResources(1, 1_024),
            cancellation.Token,
            solverPath: "not-needed-for-pre-cancelled-search");

        Assert.Equal(ExtremeSearchTerminationReason.UserCancelled, result.TerminationReason);
        Assert.Same(best, result.Plan);
        Assert.Equal("NotRun", result.SolverStatus);
        Assert.Null(result.Failure);
    }

    [Fact]
    public void Quick_balanced_and_deep_profiles_do_not_enable_extreme_convergence() {
        var quick = RouteOptimizationProfile.For(RouteOptimizationMode.Quick);
        var balanced = RouteOptimizationProfile.For(RouteOptimizationMode.Balanced);
        var deep = RouteOptimizationProfile.For(RouteOptimizationMode.Deep);
        var extreme = RouteOptimizationProfile.For(RouteOptimizationMode.Extreme);

        Assert.Equal(TimeSpan.FromSeconds(3), quick.TotalTarget);
        Assert.Equal(TimeSpan.FromSeconds(10), balanced.TotalTarget);
        Assert.Equal(TimeSpan.FromSeconds(60), deep.TotalTarget);
        Assert.All([quick, balanced, deep], profile => {
            Assert.False(profile.UsesExtremeConvergence);
            Assert.Equal(TimeSpan.Zero, profile.ExtremeNoImprovementTimeout);
        });
        Assert.True(extreme.UsesExtremeConvergence);
        Assert.Equal(TimeSpan.FromMinutes(10), extreme.ExtremeMaxSearchDuration);
        Assert.Equal(TimeSpan.FromSeconds(90), extreme.ExtremeNoImprovementTimeout);
    }

    [Fact]
    public void Ui_presentation_distinguishes_all_terminal_reasons() {
        ExtremeSearchTerminationReason[] reasons = [
            ExtremeSearchTerminationReason.MaxDurationReached,
            ExtremeSearchTerminationReason.NoImprovementConverged,
            ExtremeSearchTerminationReason.UserCancelled,
            ExtremeSearchTerminationReason.Completed,
            ExtremeSearchTerminationReason.Error,
        ];

        string[] keys = reasons
            .Select(ExtremeSearchPresentation.TerminationLocalizationKey)
            .ToArray();

        Assert.All(keys, key => Assert.False(string.IsNullOrWhiteSpace(key)));
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Planner_ui_exposes_custom_duration_and_a_terminal_status_dismiss_button() {
        DirectoryInfo directory = new(AppContext.BaseDirectory);
        for (int i = 0; i < 5; i++) directory = directory.Parent!;
        string xaml = File.ReadAllText(Path.Combine(
            directory.FullName, "View", "PlannerControl.xaml"));

        Assert.Contains("Tag=\"Custom\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TextBox_CustomSearchMinutes", xaml, StringComparison.Ordinal);
        Assert.Contains("Button_DismissExtremeSearchStatus", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"Button_DismissExtremeSearchStatus_Click\"",
            xaml, StringComparison.Ordinal);
    }

    private static (ExtremeSearchController Controller, FakeMonotonicClock Clock)
        CreateController() {
        var clock = new FakeMonotonicClock();
        return (new ExtremeSearchController(
            ExtremeRouteSolverProtocol.ExtremeMaxSearchDuration,
            ExtremeRouteSolverProtocol.ExtremeNoImprovementTimeout,
            clock), clock);
    }

    private static RoutePlan Plan(double distance, int routeCount = 1) => new(
        RoutePlanStatus.BestKnownWithinLimit,
        [],
        new RoutePlanObjective(routeCount, distance, 1, 100, "stable"),
        [],
        "test-fingerprint");

    private static AutomaticRoutePlanningRequest EmptyRequest() => new(
        [],
        new Dictionary<string, RouteItem>(StringComparer.Ordinal),
        [],
        extraLT: 0,
        totalLT: 1_000,
        new RouteSearchLimits(1, 1),
        "test-fingerprint");

    private sealed class FakeMonotonicClock : IMonotonicClock {
        private long timestamp;
        public long Frequency => 1_000;
        public long GetTimestamp() => timestamp;
        public void Advance(TimeSpan duration) =>
            timestamp += checked((long)(duration.TotalSeconds * Frequency));
    }
}
