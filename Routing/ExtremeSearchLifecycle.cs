using System.Diagnostics;

namespace iBarter.Routing;

public enum ExtremeSearchTerminationReason {
    None = 0,
    MaxDurationReached,
    NoImprovementConverged,
    UserCancelled,
    Completed,
    Error,
}

public interface IMonotonicClock {
    long GetTimestamp();
    long Frequency { get; }
}

public sealed class StopwatchMonotonicClock : IMonotonicClock {
    public static StopwatchMonotonicClock Instance { get; } = new();
    public long GetTimestamp() => Stopwatch.GetTimestamp();
    public long Frequency => Stopwatch.Frequency;
    private StopwatchMonotonicClock() { }
}

public sealed record ExtremeSearchProgressSnapshot(
    TimeSpan Elapsed,
    RoutePlanObjective? BestObjective,
    TimeSpan? LastImprovementElapsed,
    TimeSpan? ElapsedSinceLastImprovement,
    ExtremeSearchTerminationReason TerminationReason,
    string? ErrorDetail = null) {
    public bool IsTerminal => TerminationReason != ExtremeSearchTerminationReason.None;
}

/// <summary>
/// Owns Extreme's monotonic search lifecycle. Only a candidate that is
/// strictly better under RoutePlanObjective.CompareTo resets the convergence
/// timer. Equal and worse candidates leave the incumbent and timestamp intact.
/// </summary>
public sealed class ExtremeSearchController {
    private readonly IMonotonicClock clock;
    private readonly TimeSpan maxDuration;
    private readonly TimeSpan? noImprovementTimeout;
    private readonly long searchStartedAt;
    private long lastImprovementAt;
    private TimeSpan? lastImprovementElapsed;
    private RoutePlan? bestPlan;
    private ExtremeSearchTerminationReason terminationReason;
    private string? errorDetail;

    public ExtremeSearchController(
        TimeSpan maxDuration,
        TimeSpan? noImprovementTimeout,
        IMonotonicClock? clock = null) {
        if (maxDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxDuration));
        if (noImprovementTimeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(noImprovementTimeout));
        this.clock = clock ?? StopwatchMonotonicClock.Instance;
        if (this.clock.Frequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(clock), "Clock frequency must be positive.");
        this.maxDuration = maxDuration;
        this.noImprovementTimeout = noImprovementTimeout;
        searchStartedAt = this.clock.GetTimestamp();
        lastImprovementAt = searchStartedAt;
    }

    public long SearchStartedAt => searchStartedAt;
    public long LastImprovementAt => lastImprovementAt;
    public TimeSpan? LastImprovementElapsed => lastImprovementElapsed;
    public RoutePlan? BestPlan => bestPlan;
    public ExtremeSearchTerminationReason TerminationReason => terminationReason;

    public bool TryAcceptCandidate(RoutePlan? candidate) {
        if (terminationReason != ExtremeSearchTerminationReason.None
            || candidate?.Status is not (RoutePlanStatus.Optimal
                or RoutePlanStatus.BestKnownWithinLimit)
            || candidate.Objective is not { } candidateObjective)
            return false;

        if (bestPlan?.Objective is { } bestObjective
            && candidateObjective.CompareTo(bestObjective) >= 0)
            return false;

        long now = clock.GetTimestamp();
        bestPlan = candidate;
        lastImprovementAt = now;
        lastImprovementElapsed = ElapsedAt(now);
        return true;
    }

    public ExtremeSearchTerminationReason EvaluateTermination(
        bool userCancellationRequested = false) {
        if (terminationReason != ExtremeSearchTerminationReason.None)
            return terminationReason;
        ExtremeSearchTerminationReason pending = CheckTermination(
            userCancellationRequested);
        return pending == ExtremeSearchTerminationReason.None
            ? pending
            : Finish(pending);
    }

    /// <summary>
    /// Inspects the monotonic deadlines without sealing the lifecycle. The
    /// solver client uses this while a CP-SAT slice is still running so it can
    /// request a graceful stop, read the slice's final candidate, and only
    /// then decide whether convergence still applies.
    /// </summary>
    public ExtremeSearchTerminationReason CheckTermination(
        bool userCancellationRequested = false) {
        if (terminationReason != ExtremeSearchTerminationReason.None)
            return terminationReason;
        if (userCancellationRequested)
            return ExtremeSearchTerminationReason.UserCancelled;

        long now = clock.GetTimestamp();
        if (ElapsedAt(now) >= maxDuration)
            return ExtremeSearchTerminationReason.MaxDurationReached;
        if (bestPlan is not null
            && noImprovementTimeout is { } convergenceTimeout
            && convergenceTimeout > TimeSpan.Zero
            && ElapsedBetween(lastImprovementAt, now) >= convergenceTimeout)
            return ExtremeSearchTerminationReason.NoImprovementConverged;
        return ExtremeSearchTerminationReason.None;
    }

    public ExtremeSearchTerminationReason Finish(
        ExtremeSearchTerminationReason reason,
        string? detail = null) {
        if (reason == ExtremeSearchTerminationReason.None)
            throw new ArgumentOutOfRangeException(nameof(reason));
        if (terminationReason == ExtremeSearchTerminationReason.None) {
            terminationReason = reason;
            errorDetail = detail;
        }
        return terminationReason;
    }

    public TimeSpan RemainingUntilMaximum {
        get {
            TimeSpan remaining = maxDuration - ElapsedAt(clock.GetTimestamp());
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    public ExtremeSearchProgressSnapshot Snapshot() {
        long now = clock.GetTimestamp();
        return new ExtremeSearchProgressSnapshot(
            ElapsedAt(now),
            bestPlan?.Objective,
            lastImprovementElapsed,
            bestPlan is not null
                ? ElapsedBetween(lastImprovementAt, now)
                : null,
            terminationReason,
            errorDetail);
    }

    private TimeSpan ElapsedAt(long timestamp) =>
        ElapsedBetween(searchStartedAt, timestamp);

    private TimeSpan ElapsedBetween(long start, long end) {
        long ticks = Math.Max(0, end - start);
        return TimeSpan.FromSeconds(ticks / (double)clock.Frequency);
    }
}

public static class ExtremeSearchPresentation {
    public static string TerminationLocalizationKey(
        ExtremeSearchTerminationReason reason) => reason switch {
        ExtremeSearchTerminationReason.MaxDurationReached =>
            "str.Planner.Extreme.Termination.MaxDuration",
        ExtremeSearchTerminationReason.NoImprovementConverged =>
            "str.Planner.Extreme.Termination.NoImprovement",
        ExtremeSearchTerminationReason.UserCancelled =>
            "str.Planner.Extreme.Termination.Cancelled",
        ExtremeSearchTerminationReason.Completed =>
            "str.Planner.Extreme.Termination.Completed",
        ExtremeSearchTerminationReason.Error =>
            "str.Planner.Extreme.Termination.Error",
        _ => string.Empty,
    };
}
