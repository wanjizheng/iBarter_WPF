using System.Diagnostics;

namespace iBarter.Routing;

public enum RouteOptimizationMode {
    Quick,
    Balanced,
    Deep,
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

    public static RouteOptimizationProfile For(RouteOptimizationMode mode) => mode switch {
        RouteOptimizationMode.Quick => new(
            Mode: RouteOptimizationMode.Quick,
            TotalTarget: TimeSpan.FromSeconds(3),
            FinalizationReserve: TimeSpan.FromMilliseconds(500),
            MaxBeamParents: 5_000,
            MaxSuccessors: 200_000,
            MaxLocalEvaluations: 150,
            BeamWidth: 512),
        RouteOptimizationMode.Balanced => new(
            Mode: RouteOptimizationMode.Balanced,
            TotalTarget: TimeSpan.FromSeconds(10),
            FinalizationReserve: TimeSpan.FromSeconds(1),
            MaxBeamParents: 25_000,
            MaxSuccessors: 1_000_000,
            MaxLocalEvaluations: 500,
            BeamWidth: 512),
        RouteOptimizationMode.Deep => new(
            Mode: RouteOptimizationMode.Deep,
            TotalTarget: TimeSpan.FromSeconds(30),
            FinalizationReserve: TimeSpan.FromSeconds(2),
            MaxBeamParents: 100_000,
            MaxSuccessors: 4_000_000,
            MaxLocalEvaluations: 2_000,
            BeamWidth: 384),
        _ => For(RouteOptimizationMode.Balanced),
    };
}

public sealed record BeamSearchResult(
    RouteIncumbent? Incumbent,
    BeamStopReason StopReason,
    long ParentsExpanded,
    long SuccessorsEvaluated,
    int CompleteCandidatesFound);

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

    public long ParentsExpanded { get; private set; }
    public long SuccessorsEvaluated { get; private set; }
    public int LocalEvaluations { get; private set; }

    public int CompleteCandidatesFound { get; private set; }

    private BeamStopReason stopReason = BeamStopReason.NotSet;
    public BeamStopReason StopReason => stopReason;
    public void MarkStop(BeamStopReason reason) {
        // First reason wins so a single iteration can record both a parent
        // exhaustion and a time expiry without overwriting the earlier one.
        if (stopReason is BeamStopReason.Completed) return;
        stopReason = reason;
    }

    private readonly Func<long> clock;
    public Func<long> Clock => clock;

    public RouteSearchBudget(
        RouteOptimizationProfile profile,
        Func<long>? clock = null,
        long? planStartTimestamp = null) {
        this.clock = clock ?? Stopwatch.GetTimestamp;
        MaxParents = profile.MaxBeamParents;
        MaxSuccessors = profile.MaxSuccessors;
        MaxLocalEvaluations = profile.MaxLocalEvaluations;
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
            MarkStop(BeamStopReason.SuccessorBudget);
            return false;
        }
        LocalEvaluations++;
        return true;
    }

    public void RegisterComplete() => CompleteCandidatesFound++;
}
