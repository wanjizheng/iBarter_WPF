using System.Diagnostics;

namespace iBarter.Routing;

public enum RouteOptimizationMode {
    Quick,
    Balanced,
    Deep,
    Extreme,
}

public enum BeamStopReason {
    NotSet = 0,   // sentinel: the search hasn't recorded a reason yet
    Completed,
    ParentBudget,
    SuccessorBudget,
    TimeBudget,
    DepthLimit,
    FrontierExhausted,
    Cancelled,
}

public sealed record RouteOptimizationProfile(
    RouteOptimizationMode Mode,
    TimeSpan TotalTarget,
    TimeSpan FinalizationReserve,
    int MaxBeamParents,
    long MaxSuccessors,
    int MaxLocalEvaluations,
    int BeamWidth) {

    public TimeSpan ExtremeMaxSearchDuration => Mode == RouteOptimizationMode.Extreme
        ? TotalTarget
        : TimeSpan.Zero;
    public TimeSpan ExtremeNoImprovementTimeout => Mode == RouteOptimizationMode.Extreme
        ? ExtremeRouteSolverProtocol.ExtremeNoImprovementTimeout
        : TimeSpan.Zero;
    public bool UsesExtremeConvergence => Mode == RouteOptimizationMode.Extreme;

    public static RouteOptimizationProfile For(RouteOptimizationMode mode) => mode switch {
        RouteOptimizationMode.Quick => new(
            Mode: RouteOptimizationMode.Quick,
            TotalTarget: TimeSpan.FromSeconds(3),
            FinalizationReserve: TimeSpan.FromMilliseconds(500),
            MaxBeamParents: 5_000,
            MaxSuccessors: 200_000,
            MaxLocalEvaluations: 150,
            BeamWidth: 384),
        RouteOptimizationMode.Balanced => new(
            Mode: RouteOptimizationMode.Balanced,
            TotalTarget: TimeSpan.FromSeconds(10),
            FinalizationReserve: TimeSpan.FromSeconds(1),
            MaxBeamParents: 25_000,
            MaxSuccessors: 1_000_000,
            MaxLocalEvaluations: 500,
            BeamWidth: 384),
        RouteOptimizationMode.Deep => new(
            Mode: RouteOptimizationMode.Deep,
            TotalTarget: TimeSpan.FromSeconds(60),
            FinalizationReserve: TimeSpan.FromSeconds(3),
            MaxBeamParents: 100_000,
            MaxSuccessors: 4_000_000,
            MaxLocalEvaluations: 2_000,
            BeamWidth: 1_024),
        RouteOptimizationMode.Extreme => new(
            Mode: RouteOptimizationMode.Extreme,
            TotalTarget: ExtremeRouteSolverProtocol.ExtremeMaxSearchDuration,
            FinalizationReserve: TimeSpan.Zero,
            MaxBeamParents: 100_000,
            MaxSuccessors: 4_000_000,
            MaxLocalEvaluations: 2_000,
            BeamWidth: 1_024),
        _ => For(RouteOptimizationMode.Balanced),
    };
}

public sealed record BeamSearchResult(
    RouteIncumbent? Incumbent,
    BeamStopReason StopReason,
    long ParentsExpanded,
    long SuccessorsEvaluated,
    int CompleteCandidatesFound,
    int EffectiveBeamWidth,
    bool LocalEvaluationBudgetExhausted);

/// <summary>
/// Shared, testable search budget. Time, parent count, successor count, and
/// local-evaluation count are all enforced through a single object so the
/// beam search, intra-route optimizer, and route-pair rebuilder cannot each
/// independently grab a fresh quota. An injectable clock makes the time
/// accounting deterministic in tests without resorting to Thread.Sleep.
/// </summary>
public sealed class RouteSearchBudget {
    public long PlanStartTimestamp { get; }
    public long SearchDeadlineTimestamp { get; }
    public long TargetEndTimestamp { get; }
    public int MaxParents { get; }
    public long MaxSuccessors { get; }
    public int MaxLocalEvaluations { get; }

    // Effective beam width actually used by the search. The profile declares a
    // *requested* BeamWidth; the budget reduces it via a task-count cap so
    // large plans don't carry an oversize frontier. This is the SINGLE source
    // of truth for the width; BeamSearchResult and the profiler both read it
    // from here rather than re-deriving it.
    public int EffectiveBeamWidth { get; }

    public long ParentsExpanded { get; private set; }
    public long SuccessorsEvaluated { get; private set; }
    public int LocalEvaluations { get; private set; }

    public int CompleteCandidatesFound { get; private set; }

    private BeamStopReason stopReason = BeamStopReason.NotSet;
    public BeamStopReason StopReason => stopReason;

    /// <summary>
    /// Records the beam search's stop reason. The FIRST recorded reason wins;
    /// later calls are ignored so a single iteration that hits both a parent
    /// cap and the time budget (for example) reports the earlier cause rather
    /// than the later one. Use <see cref="ForceStopReason"/> only when the
    /// caller truly intends to overwrite (e.g. classifying a NotSet fallback).
    /// </summary>
    public void MarkStop(BeamStopReason reason) {
        if (stopReason == BeamStopReason.NotSet) stopReason = reason;
    }

    public void ForceStopReason(BeamStopReason reason) => stopReason = reason;

    // Finalization (post-beam local optimization) records its own exhaustion
    // signal here so it cannot silently poison the beam's stop reason. The
    // planner publishes this as a separate diagnostic tag for the UI.
    public bool LocalEvaluationBudgetExhausted { get; private set; }
    public void MarkLocalEvaluationBudgetExhausted() => LocalEvaluationBudgetExhausted = true;

    private readonly Func<long> clock;
    public Func<long> Clock => clock;

    public RouteSearchBudget(
        RouteOptimizationProfile profile,
        Func<long>? clock = null,
        long? planStartTimestamp = null,
        int taskCount = 0) {
        this.clock = clock ?? Stopwatch.GetTimestamp;
        MaxParents = profile.MaxBeamParents;
        MaxSuccessors = profile.MaxSuccessors;
        MaxLocalEvaluations = profile.MaxLocalEvaluations;
        // Effective beam width: never exceed the profile's request, and
        // additionally cap by task count so the frontier stays manageable
        // for very large plans. Tests can pass taskCount=0 to disable the
        // task-count cap.
        int taskCap = profile.Mode is RouteOptimizationMode.Deep or RouteOptimizationMode.Extreme
            ? int.MaxValue
            : taskCount switch {
                >= 20 => 128,
                >= 17 => 256,
                _ => int.MaxValue,
            };
        EffectiveBeamWidth = Math.Min(profile.BeamWidth, taskCap);
        PlanStartTimestamp = planStartTimestamp ?? this.clock();
        long freq = Stopwatch.Frequency;
        long totalTicks = (long)(profile.TotalTarget.TotalSeconds * freq);
        long reserveTicks = (long)(profile.FinalizationReserve.TotalSeconds * freq);
        TargetEndTimestamp = PlanStartTimestamp + totalTicks;
        // Keep the search phase strictly within (target - finalization reserve)
        // so the planner can still run the final local optimization + verifier
        // even when the wall clock is slow.
        SearchDeadlineTimestamp = TargetEndTimestamp - reserveTicks;
    }

    public bool SearchTimeExpired => clock() >= SearchDeadlineTimestamp;
    public bool TargetTimeExpired => clock() >= TargetEndTimestamp;

    public bool TryConsumeParent() {
        if (ParentsExpanded >= MaxParents) {
            MarkStop(BeamStopReason.ParentBudget);
            return false;
        }
        ParentsExpanded++;
        return true;
    }

    public bool TryConsumeSuccessor() {
        if (SuccessorsEvaluated >= MaxSuccessors) {
            MarkStop(BeamStopReason.SuccessorBudget);
            return false;
        }
        SuccessorsEvaluated++;
        return true;
    }

    public bool TryConsumeLocalEvaluation() {
        if (LocalEvaluations >= MaxLocalEvaluations) {
            // Finalization budget exhausted: do NOT touch the beam stop
            // reason. The beam already recorded why it stopped (e.g.
            // FrontierExhausted); changing it here would be a lie.
            MarkLocalEvaluationBudgetExhausted();
            return false;
        }
        LocalEvaluations++;
        return true;
    }

    public void RegisterComplete() => CompleteCandidatesFound++;
}
