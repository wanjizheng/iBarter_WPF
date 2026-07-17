using System.Diagnostics;
using System.Numerics;
using System.Text;

namespace iBarter.Routing;

public sealed class AutomaticRoutePlanner {
    // Legacy entry: defaults to Balanced so the call site is unchanged.
    public RoutePlan Plan(
        AutomaticRoutePlanningRequest request,
        CancellationToken cancellationToken = default)
        => Plan(request, RouteOptimizationProfile.For(RouteOptimizationMode.Balanced), cancellationToken);

    public RoutePlan Plan(
        AutomaticRoutePlanningRequest request,
        RouteOptimizationProfile profile,
        CancellationToken cancellationToken = default) {
        string fingerprint = RoutePlanFingerprint.Compute(request);
        var budget = new RouteSearchBudget(profile, taskCount: request.Tasks.Count);
        long planStart = budget.PlanStartTimestamp;
        if (RouteSearchProfiler.Current is { } sp) {
            sp.OptimizationMode = profile.Mode.ToString();
            sp.TotalTargetMs = profile.TotalTarget.TotalMilliseconds;
            sp.SearchDeadlineMs = (budget.SearchDeadlineTimestamp - planStart) * 1000L / Stopwatch.Frequency;
        }
        RoutePlan? incumbent = null;
        long firstVerifiedIncumbentTimestamp = 0;
        try {
            cancellationToken.ThrowIfCancellationRequested();
            var preflightStart = Stopwatch.GetTimestamp();
            var preflight = AutomaticRoutePreflight.Validate(request);
            if (RouteSearchProfiler.Current is { } pp) pp.PreflightTicks += Stopwatch.GetTimestamp() - preflightStart;
            if (!preflight.IsValid) {
                bool provenInfeasible = preflight.Diagnostics.Any(x =>
                    x.Code is "unreachable-input" or "task-overweight");
                return new RoutePlan(
                    provenInfeasible ? RoutePlanStatus.Infeasible : RoutePlanStatus.InvalidInput,
                    [], null, preflight.Diagnostics, fingerprint);
            }
            if (request.Tasks.Count == 0)
                return new RoutePlan(RoutePlanStatus.Optimal, [],
                    new RoutePlanObjective(0, 0, 0, request.ExtraLT, ""), [], fingerprint);

            // 1) Heuristic: get a complete, verified incumbent as early as possible.
            var heuristicStart = Stopwatch.GetTimestamp();
            var heuristicIncumbent = AutomaticRouteHeuristic.TryBuildIncumbent(
                request, preflight, cancellationToken)?.Plan;
            if (RouteSearchProfiler.Current is { } hp) hp.HeuristicTicks += Stopwatch.GetTimestamp() - heuristicStart;
            if (heuristicIncumbent is not null) {
                var checkedIncumbent = RoutePlanVerifier.Verify(request, heuristicIncumbent);
                if (checkedIncumbent.Success) {
                    incumbent = checkedIncumbent.VerifiedPlan;
                    firstVerifiedIncumbentTimestamp = budget.Clock();
                    if (RouteSearchProfiler.Current is { } f) {
                        f.FirstVerifiedIncumbentMs = (firstVerifiedIncumbentTimestamp - planStart) * 1000L / Stopwatch.Frequency;
                    }
                }
            }

            if (request.Tasks.Count > AutomaticRouteSearchPolicy.ExactTaskLimit) {
                // 2) Anytime beam: search in remaining time-budget, keep best complete.
                var beamStart = Stopwatch.GetTimestamp();
                var beamResult = AutomaticRouteBeamSearch.TryBuildIncumbent(
                    request, preflight, budget, cancellationToken);
                if (RouteSearchProfiler.Current is { } bp) {
                    bp.BeamTicks += Stopwatch.GetTimestamp() - beamStart;
                    bp.BeamParentsExpanded = budget.ParentsExpanded;
                    bp.SuccessorsEvaluated = budget.SuccessorsEvaluated;
                    bp.CompleteCandidatesFound = budget.CompleteCandidatesFound;
                    bp.LocalEvaluations = budget.LocalEvaluations;
                    bp.StopReason = beamResult.StopReason.ToString();
                    bp.EffectiveBeamWidth = beamResult.EffectiveBeamWidth;
                    bp.LocalEvaluationBudgetExhausted = beamResult.LocalEvaluationBudgetExhausted;
                }
                if (beamResult.Incumbent is not null) {
                    // Re-verify on the planner side too — this is the only verified
                    // incumbent that may be published.  If verification fails we
                    // keep the previously verified heuristic plan, never adopting
                    // an unverified improvement.
                    var checkedBeam = RoutePlanVerifier.Verify(request, beamResult.Incumbent.Plan);
                    if (checkedBeam.Success && checkedBeam.VerifiedPlan?.Objective is { } beamObjective
                        && (incumbent?.Objective is null
                            || beamObjective.CompareTo(incumbent.Objective.Value) < 0)) {
                        incumbent = checkedBeam.VerifiedPlan;
                        if (RouteSearchProfiler.Current is { } bm) {
                            bm.BestImprovementMs = (budget.Clock() - planStart) * 1000L / Stopwatch.Frequency;
                        }
                    }
                }

                // 3) Final verify the published incumbent.
                var finalVerifyStart = Stopwatch.GetTimestamp();
                RoutePlan? finalPlan = null;
                if (incumbent is not null) {
                    var verification = RoutePlanVerifier.Verify(request, incumbent);
                    if (RouteSearchProfiler.Current is { } fv) fv.FinalVerifyTicks += Stopwatch.GetTimestamp() - finalVerifyStart;
                    finalPlan = verification.Success ? verification.VerifiedPlan : null;
                } else {
                    if (RouteSearchProfiler.Current is { } fv) fv.FinalVerifyTicks += Stopwatch.GetTimestamp() - finalVerifyStart;
                }

                if (RouteSearchProfiler.Current is { } fp) {
                    fp.FinalElapsedMs = (budget.Clock() - planStart) * 1000L / Stopwatch.Frequency;
                    if (finalPlan is not null) {
                        fp.FinalRoutes = finalPlan.Routes.Count;
                        fp.FinalDistance = finalPlan.Objective?.TotalDistance ?? 0;
                        fp.FinalVerified = true;
                    }
                }

                if (finalPlan is null)
                    return new RoutePlan(RoutePlanStatus.NoFeasibleSolutionWithinLimit,
                        [], null, [AnytimeDiagnostic(profile.Mode, beamResult.StopReason,
                            request.Tasks.Count, beamResult.EffectiveBeamWidth,
                            beamResult.LocalEvaluationBudgetExhausted)], fingerprint);
                return new RoutePlan(RoutePlanStatus.BestKnownWithinLimit,
                    finalPlan.Routes, finalPlan.Objective,
                    [AnytimeDiagnostic(profile.Mode, beamResult.StopReason,
                        request.Tasks.Count, beamResult.EffectiveBeamWidth,
                        beamResult.LocalEvaluationBudgetExhausted)], fingerprint);
            }

            // Exact search path for small plans (≤ ExactTaskLimit). This path
            // remains budget-aware so a Quick mode can also impose a wall-clock
            // ceiling, but it is the only path that can return Optimal.
            var initial = RouteSimulationState.CreateInitial(request);
            var queue = new PriorityQueue<RouteSimulationState, SearchPriority>();
            long sequence = 0;
            queue.Enqueue(initial, Priority(initial, request, sequence++));
            var bestByState = new Dictionary<string, SearchCost>(StringComparer.Ordinal) {
                [SearchKey(initial)] = Cost(initial),
            };
            bool limited = false;
            int expanded = 0;
            ulong fullMask = request.Tasks.Count == 64 ? ulong.MaxValue : (1UL << request.Tasks.Count) - 1;

            while (queue.Count > 0) {
                cancellationToken.ThrowIfCancellationRequested();
                // Honor the tighter of the profile's parent budget and the
                // request's MaxExpandedStates (legacy test contracts rely on
                // the request's value when it is the smaller one).
                int parentCap = Math.Min(profile.MaxBeamParents, request.Limits.MaxExpandedStates);
                if (expanded >= parentCap || budget.SearchTimeExpired) {
                    limited = true;
                    break;
                }
                var state = queue.Dequeue();
                expanded++;

                if (incumbent?.Objective is { } upper && LowerBound(state, request).CompareTo(upper) > 0)
                    continue;
                if (state.CompletedMask == fullMask && state.CurrentRouteSteps.Count == 0) {
                    var candidate = RoutePlanFactory.FromState(
                        request, state, RoutePlanStatus.BestKnownWithinLimit, []);
                    var verification = RoutePlanVerifier.Verify(request, candidate);
                    if (verification.Success &&
                        (incumbent?.Objective is null ||
                         verification.VerifiedPlan!.Objective!.Value.CompareTo(incumbent.Objective.Value) < 0))
                        incumbent = verification.VerifiedPlan;
                    continue;
                }

                foreach (var successor in Expand(request, state, fullMask)) {
                    if (incumbent?.Objective is { } bound && LowerBound(successor, request).CompareTo(bound) > 0)
                        continue;
                    string key = SearchKey(successor);
                    var cost = Cost(successor);
                    if (bestByState.TryGetValue(key, out var existing) && existing.CompareTo(cost) <= 0)
                        continue;
                    bestByState[key] = cost;
                    queue.Enqueue(successor, Priority(successor, request, sequence++));
                }
            }

            if (limited) {
                if (incumbent is null)
                    return new RoutePlan(RoutePlanStatus.NoFeasibleSolutionWithinLimit, [], null, [], fingerprint);
                return WithStatus(incumbent, RoutePlanStatus.BestKnownWithinLimit);
            }
            if (incumbent is null)
                return new RoutePlan(RoutePlanStatus.Infeasible, [], null, [], fingerprint);
            return WithStatus(incumbent, RoutePlanStatus.Optimal);
        }
        catch (OperationCanceledException) {
            if (RouteSearchProfiler.Current is { } p) p.StopReason = BeamStopReason.Cancelled.ToString();
            return new RoutePlan(RoutePlanStatus.Cancelled, [], null, [], fingerprint);
        }
        catch (OutOfMemoryException) {
            var diagnostic = new RouteDiagnostic(
                "memory-limit",
                Detail: "The bounded route search reached the process memory limit.");
            return incumbent is null
                ? new RoutePlan(
                    RoutePlanStatus.NoFeasibleSolutionWithinLimit,
                    [],
                    null,
                    [diagnostic],
                    fingerprint)
                : new RoutePlan(
                    RoutePlanStatus.BestKnownWithinLimit,
                    incumbent.Routes,
                    incumbent.Objective,
                    [diagnostic],
                    fingerprint);
        }
    }

    private static RouteDiagnostic AnytimeDiagnostic(
        RouteOptimizationMode mode, BeamStopReason reason, int taskCount,
        int effectiveBeamWidth, bool localEvalExhausted) {
        // For large-task plans the historical "exact-search-skipped" code is
        // preserved as the diagnostic code so existing UI log messages keep
        // working; the structured mode + reason + effective width + finalization
        // signal are recorded in the detail.
        string code = "exact-search-skipped";
        return new RouteDiagnostic(code,
            Detail: $"mode={mode} reason={reason} tasks={taskCount} " +
                    $"effectiveBeamWidth={effectiveBeamWidth} " +
                    $"localEvalExhausted={localEvalExhausted}");
    }

    private static IEnumerable<RouteSimulationState> Expand(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        ulong fullMask) {
        foreach (int index in Enumerable.Range(0, request.Tasks.Count)
                     .Where(i => (state.CompletedMask & (1UL << i)) == 0)
                     .OrderBy(i => request.Tasks[i].RowId, StringComparer.Ordinal)) {
            var result = RouteStateTransition.TryBarter(request, state, index);
            if (result.Success) yield return result.State;
        }

        ulong remaining = fullMask & ~state.CompletedMask;
        foreach (var warehouse in request.Warehouses.OrderBy(x => x.WarehouseId, StringComparer.Ordinal)) {
            if (state.VisitedWarehouseIds.Contains(warehouse.WarehouseId)) continue;
            foreach (var bundle in DemandBundleGenerator.Generate(request, state, warehouse.WarehouseId, remaining)) {
                var result = RouteStateTransition.TryPickup(request, state, warehouse.WarehouseId, bundle.Items);
                if (result.Success && AutomaticRouteSearchPolicy.HasExecutableBarter(
                        request, result.State, remaining))
                    yield return result.State;
            }
        }

        if (state.CurrentRouteSteps.OfType<BarterStep>().Any()) {
            var result = WarehouseUnloadPlanner.TryCompleteRoute(request, state);
            if (result.Success) yield return result.State;
        }
    }

    private static RoutePlanObjective LowerBound(
        RouteSimulationState state,
        AutomaticRoutePlanningRequest request) {
        bool currentHasBarter = state.CurrentRouteSteps.OfType<BarterStep>().Any();
        bool remains = state.CompletedMask != (request.Tasks.Count == 64
            ? ulong.MaxValue
            : (1UL << request.Tasks.Count) - 1);
        int routes = state.FinishedRoutes.Count + (currentHasBarter || remains ? 1 : 0);
        int peak = Math.Max(
            state.CurrentRoutePeakLT,
            state.FinishedRoutes.Count == 0 ? request.ExtraLT : state.FinishedRoutes.Max(x => x.PeakLT));
        return new RoutePlanObjective(routes, state.TotalDistance, state.PickupStopCount, peak, "");
    }

    private static SearchPriority Priority(
        RouteSimulationState state,
        AutomaticRoutePlanningRequest request,
        long sequence) {
        var bound = LowerBound(state, request);
        return new SearchPriority(
            bound.RouteCount, bound.TotalDistance, bound.PickupStopCount, bound.MaxPeakLT, sequence);
    }

    private static SearchCost Cost(RouteSimulationState state) => new(
        state.TotalDistance,
        state.PickupStopCount,
        Math.Max(state.CurrentRoutePeakLT,
            state.FinishedRoutes.Count == 0 ? 0 : state.FinishedRoutes.Max(x => x.PeakLT)),
        RoutePlanFactory.StableRouteKey(state.FinishedRoutes, state.CurrentRouteSteps));

    private static string SearchKey(RouteSimulationState state) {
        if (RouteSearchProfiler.Current is { } p) p.SearchKeyCalls++;
        var builder = new StringBuilder();
        builder.Append(state.CurrentRouteNumber).Append('|')
            .Append(state.CurrentIslandId).Append('|')
            .Append(state.CompletedMask).Append('|')
            .Append(state.CurrentRouteSteps.OfType<BarterStep>().Any() ? '1' : '0').Append('|');
        foreach (string id in state.VisitedWarehouseIds.OrderBy(x => x, StringComparer.Ordinal))
            builder.Append(id).Append(',');
        builder.Append('|');
        AppendInventory(builder, state.OnBoard);
        foreach (var warehouse in state.WarehouseInventory.OrderBy(x => x.Key, StringComparer.Ordinal)) {
            builder.Append('#').Append(warehouse.Key).Append(':');
            AppendInventory(builder, warehouse.Value);
        }
        return builder.ToString();
    }

    private static void AppendInventory(StringBuilder builder, IReadOnlyDictionary<string, int> inventory) {
        foreach (var pair in inventory.Where(x => x.Value != 0).OrderBy(x => x.Key, StringComparer.Ordinal))
            builder.Append(pair.Key).Append('=').Append(pair.Value).Append(';');
    }

    private static RoutePlan WithStatus(RoutePlan plan, RoutePlanStatus status) =>
        new(status, plan.Routes, plan.Objective, plan.Diagnostics, plan.InputFingerprint);

    private readonly record struct SearchPriority(
        int Routes, double Distance, int Pickups, int Peak, long Sequence) : IComparable<SearchPriority> {
        public int CompareTo(SearchPriority other) {
            int result = Routes.CompareTo(other.Routes);
            if (result != 0) return result;
            result = Distance.CompareTo(other.Distance);
            if (result != 0) return result;
            result = Pickups.CompareTo(other.Pickups);
            if (result != 0) return result;
            result = Peak.CompareTo(other.Peak);
            return result != 0 ? result : Sequence.CompareTo(other.Sequence);
        }
    }

    private readonly record struct SearchCost(
        double Distance, int Pickups, int Peak, string StableKey) : IComparable<SearchCost> {
        public int CompareTo(SearchCost other) {
            int result = Distance.CompareTo(other.Distance);
            if (result != 0) return result;
            result = Pickups.CompareTo(other.Pickups);
            if (result != 0) return result;
            result = Peak.CompareTo(other.Peak);
            return result != 0 ? result : StringComparer.Ordinal.Compare(StableKey, other.StableKey);
        }
    }
}

internal static class AutomaticRouteSearchPolicy {
    // Exact subset search is exponential. Above this boundary the planner
    // publishes the fully replayed heuristic incumbent and reports its status
    // truthfully as BestKnownWithinLimit instead of freezing before UI publish.
    public const int ExactTaskLimit = 12;

    public static bool HasExecutableBarter(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        ulong remainingMask) {
        if (RouteSearchProfiler.Current is { } p) p.HasExecutableBarterCalls++;
        for (int index = 0; index < request.Tasks.Count; index++) {
            if ((remainingMask & (1UL << index)) == 0) continue;
            if (RouteStateTransition.CanBarterFast(request, state, index))
                return true;
        }
        return false;
    }
}
