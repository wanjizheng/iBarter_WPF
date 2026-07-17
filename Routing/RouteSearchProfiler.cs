using System.Diagnostics;
using System.Text;

namespace iBarter.Routing;

/// <summary>
/// Opt-in, low-overhead planning profiler. When <see cref="Current"/> is null
/// (the default) every hook is a single null check, so production planning pays
/// nothing. The planner and search stages increment plain counters and add
/// elapsed ticks for coarse phases; the benchmark/tests read the summary after a
/// run. Deliberately UI-independent so it compiles into the headless test host.
/// </summary>
public sealed class RouteSearchProfiler {
    [ThreadStatic] public static RouteSearchProfiler? Current;

    // Coarse phase timings (raw Stopwatch ticks).
    public long PreflightTicks;
    public long HeuristicTicks;
    public long BeamTicks;
    public long FinalOptimizeTicks;
    public long FinalVerifyTicks;

    // Search shape.
    public long BeamParentsExpanded;
    public long SuccessorsGenerated;
    public long SuccessorsEvaluated;
    public long CandidatesDeduped;
    public long TrimCandidatesCalls;
    public long CompleteCandidatesFound;
    public int LocalEvaluations;

    // Beam region timings (raw Stopwatch ticks).
    public long BeamCompleteTicks;
    public long BeamExpandTicks;
    public long BeamRankTicks;
    public long DistanceTicks;
    public long BundleGenTicks;

    // Anytime-mode fields (set by the planner / beam / verifier).
    public string OptimizationMode = "";
    public string StopReason = "";
    public long PlanTicks;
    public double TotalTargetMs;
    public double SearchDeadlineMs;
    public double FirstVerifiedIncumbentMs;
    public double BestImprovementMs;
    public double FinalElapsedMs;
    public int FinalRoutes;
    public double FinalDistance;
    public bool FinalVerified;

    // Hot-method call counts.
    public long SearchKeyCalls;
    public long StableKeyCalls;
    public long TryBarterCalls;
    public long TryPickupCalls;
    public long TryUnloadCalls;
    public long HasExecutableBarterCalls;
    public long CanBarterFastCalls;
    public long CloneWarehouseInventoryCalls;
    public long DistanceCalls;
    public long VerifyCalls;
    public long VerifyAndImproveCalls;
    public long IntraRouteImproveCalls;
    public long RoutePairRebuildCalls;

    public static IDisposable? Enable() {
        if (Current is not null) return null;
        Current = new RouteSearchProfiler();
        return new Scope();
    }

    public static RouteSearchProfiler? Snapshot() => Current;

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    public string Report() {
        var sb = new StringBuilder();
        sb.AppendLine("--- RouteSearchProfiler ---");
        if (!string.IsNullOrEmpty(OptimizationMode))
            sb.AppendLine($"mode={OptimizationMode} target={TotalTargetMs:F0}ms searchDeadline={SearchDeadlineMs:F0}ms " +
                $"stop={StopReason} verified={FinalVerified}");
        if (FirstVerifiedIncumbentMs > 0 || BestImprovementMs > 0 || FinalElapsedMs > 0)
            sb.AppendLine($"timing_ms firstIncumbent={FirstVerifiedIncumbentMs:F0} " +
                $"bestImprovement={BestImprovementMs:F0} final={FinalElapsedMs:F0}");
        if (FinalRoutes > 0 || FinalDistance > 0)
            sb.AppendLine($"result routes={FinalRoutes} distance={FinalDistance:F1}");
        sb.AppendLine($"phase_ms preflight={Ms(PreflightTicks):F1} heuristic={Ms(HeuristicTicks):F1} " +
            $"beam={Ms(BeamTicks):F1} finalOptimize={Ms(FinalOptimizeTicks):F1} finalVerify={Ms(FinalVerifyTicks):F1}");
        sb.AppendLine($"beam parentsExpanded={BeamParentsExpanded} successorsGenerated={SuccessorsGenerated} " +
            $"candidatesDeduped={CandidatesDeduped} trimCalls={TrimCandidatesCalls} " +
            $"completes={CompleteCandidatesFound} localEval={LocalEvaluations}");
        sb.AppendLine($"beam_region_ms complete={Ms(BeamCompleteTicks):F1} expand={Ms(BeamExpandTicks):F1} " +
            $"rank={Ms(BeamRankTicks):F1} distance={Ms(DistanceTicks):F1} bundleGen={Ms(BundleGenTicks):F1}");
        sb.AppendLine($"calls searchKey={SearchKeyCalls} stableKey={StableKeyCalls} " +
            $"tryBarter={TryBarterCalls} tryPickup={TryPickupCalls} tryUnload={TryUnloadCalls}");
        sb.AppendLine($"calls hasExecBarter={HasExecutableBarterCalls} canBarterFast={CanBarterFastCalls} " +
            $"cloneWarehouseInv={CloneWarehouseInventoryCalls} distance={DistanceCalls}");
        sb.AppendLine($"calls verify={VerifyCalls} verifyAndImprove={VerifyAndImproveCalls} " +
            $"intraRoute={IntraRouteImproveCalls} pairRebuild={RoutePairRebuildCalls}");
        return sb.ToString();
    }

    private sealed class Scope : IDisposable {
        public void Dispose() => Current = null;
    }
}
