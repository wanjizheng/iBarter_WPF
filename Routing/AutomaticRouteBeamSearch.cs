using System.Diagnostics;
using System.Numerics;
using System.Text;

namespace iBarter.Routing;

/// <summary>
/// A bounded feasibility fallback for large plans.  It keeps several distinct
/// partial cargo states instead of committing to the heuristic's single greedy
/// pickup/exchange choice. The new overload accepts a <see cref="RouteSearchBudget"/>
/// that controls parent/successor/time/local-evaluation budgets and returns a
/// structured <see cref="BeamSearchResult"/> so callers can reason about the
/// stop reason instead of guessing from null vs. non-null.
/// </summary>
public static class AutomaticRouteBeamSearch {
    private const int CandidateRetainLimit = 2_048;
    private const int CandidateTrimThreshold = CandidateRetainLimit * 2;

    // Backwards-compatible entry point used by older callers and tests that did
    // not yet pass a profile/budget. Falls back to request.Limits.MaxExpandedStates
    // and never sets a deadline; preserves the previous return shape.
    public static RouteIncumbent? TryBuildIncumbent(
        AutomaticRoutePlanningRequest request,
        RoutePreflightResult preflight,
        CancellationToken cancellationToken,
        int? maxExpandedStates = null,
        long deadlineTimestamp = 0) {
        var budget = new RouteSearchBudget(
            new RouteOptimizationProfile(
                Mode: RouteOptimizationMode.Balanced,
                TotalTarget: TimeSpan.FromMilliseconds(deadlineTimestamp == 0 ? 0 : Math.Max(1, (deadlineTimestamp - Stopwatch.GetTimestamp()) * 1000L / Stopwatch.Frequency)),
                FinalizationReserve: TimeSpan.Zero,
                MaxBeamParents: maxExpandedStates ?? request.Limits.MaxExpandedStates,
                MaxSuccessors: long.MaxValue,
                MaxLocalEvaluations: request.Limits.MaxLocalMoves,
                BeamWidth: 384),
            clock: deadlineTimestamp == 0 ? null : () => Stopwatch.GetTimestamp(),
            planStartTimestamp: deadlineTimestamp == 0 ? null : Stopwatch.GetTimestamp(),
            taskCount: request.Tasks.Count);
        // The legacy overload never times out (deadline=0 means "disabled").
        if (deadlineTimestamp == 0)
            budget = new RouteSearchBudget(
                new RouteOptimizationProfile(
                    RouteOptimizationMode.Balanced,
                    TimeSpan.FromHours(1), TimeSpan.Zero,
                    maxExpandedStates ?? request.Limits.MaxExpandedStates,
                    long.MaxValue, request.Limits.MaxLocalMoves, 384),
                taskCount: request.Tasks.Count);
        return TryBuildIncumbent(request, preflight, budget, cancellationToken).Incumbent;
    }

    public static BeamSearchResult TryBuildIncumbent(
        AutomaticRoutePlanningRequest request,
        RoutePreflightResult preflight,
        RouteSearchBudget budget,
        CancellationToken cancellationToken) {
        if (!preflight.IsValid || request.Tasks.Count == 0)
            return new BeamSearchResult(null, BeamStopReason.Completed,
                0, 0, 0, budget.EffectiveBeamWidth, budget.LocalEvaluationBudgetExhausted);

        ulong fullMask = request.Tasks.Count == 64 ? ulong.MaxValue : (1UL << request.Tasks.Count) - 1;
        var frontier = new[] { RouteSimulationState.CreateInitial(request) };
        int maxDepth = Math.Max(64, request.Tasks.Count * 4 + request.Warehouses.Count * request.Tasks.Count);

        // The best fully-complete state seen so far. Updated both during the
        // per-depth frontier sweep AND for every complete successor found
        // inside the per-parent expand loop, so a stop triggered by budget
        // exhaustion mid-depth never silently drops a better complete plan
        // that was just generated.
        RouteSimulationState? bestRaw = null;
        RoutePlanObjective? bestRawObjective = null;

        void ConsiderComplete(RouteSimulationState state) {
            if (state.CompletedMask != fullMask || state.CurrentRouteSteps.Count != 0) return;
            budget.RegisterComplete();
            var objective = Objective(request, state);
            if (bestRawObjective is null || objective.CompareTo(bestRawObjective.Value) < 0) {
                bestRaw = state;
                bestRawObjective = objective;
            }
        }

        for (int depth = 0; depth < maxDepth && frontier.Length > 0; depth++) {
            cancellationToken.ThrowIfCancellationRequested();
            if (budget.SearchTimeExpired) { budget.MarkStop(BeamStopReason.TimeBudget); break; }

            var completeStart = Stopwatch.GetTimestamp();
            for (int i = 0; i < frontier.Length; i++) ConsiderComplete(frontier[i]);
            if (RouteSearchProfiler.Current is { } tc) tc.BeamCompleteTicks += Stopwatch.GetTimestamp() - completeStart;

            var expandStart = Stopwatch.GetTimestamp();
            var candidates = new Dictionary<string, RouteSimulationState>(StringComparer.Ordinal);
            bool stop = false;
            for (int p = 0; p < frontier.Length && !stop; p++) {
                var state = frontier[p];
                if (state.CompletedMask == fullMask && state.CurrentRouteSteps.Count == 0) continue;
                if (!budget.TryConsumeParent()) { stop = true; break; }
                if (budget.SearchTimeExpired) { budget.MarkStop(BeamStopReason.TimeBudget); stop = true; break; }
                if (RouteSearchProfiler.Current is { } pp) pp.BeamParentsExpanded++;

                foreach (var successor in Expand(request, state, fullMask)) {
                    if (!budget.TryConsumeSuccessor()) { stop = true; break; }
                    if (RouteSearchProfiler.Current is { } sp) sp.SuccessorsGenerated++;
                    // Per-successor complete check: a complete state produced
                    // by this expansion is captured even if a later budget
                    // stop interrupts this depth layer.
                    ConsiderComplete(successor);
                    string key = SearchKey(successor);
                    if (!candidates.TryGetValue(key, out var existing) || IsBetter(successor, existing))
                        candidates[key] = successor;
                    if (candidates.Count >= CandidateTrimThreshold)
                        candidates = TrimCandidates(candidates, CandidateRetainLimit);
                }
                if (stop) break;
            }
            if (RouteSearchProfiler.Current is { } cp) cp.CandidatesDeduped += candidates.Count;
            if (RouteSearchProfiler.Current is { } te) te.BeamExpandTicks += Stopwatch.GetTimestamp() - expandStart;
            if (stop) break;

            var rankStart = Stopwatch.GetTimestamp();
            int beamWidth = budget.EffectiveBeamWidth;
            var ranked = RankCandidates(candidates.Values, beamWidth).ToArray();
            if (ranked.Length > beamWidth) {
                var trimmed = new RouteSimulationState[beamWidth];
                Array.Copy(ranked, trimmed, beamWidth);
                frontier = trimmed;
            } else {
                frontier = ranked;
            }
            if (RouteSearchProfiler.Current is { } tr) tr.BeamRankTicks += Stopwatch.GetTimestamp() - rankStart;

            if (frontier.Length == 0) { budget.MarkStop(BeamStopReason.FrontierExhausted); break; }
        }
        if (budget.StopReason is BeamStopReason.NotSet) budget.MarkStop(BeamStopReason.DepthLimit);

        if (bestRaw is null) {
            // Nothing complete was found at all. If we still have no explicit
            // reason, attribute the absence to an exhausted frontier.
            if (budget.StopReason is BeamStopReason.DepthLimit)
                budget.MarkStop(BeamStopReason.FrontierExhausted);
            return new BeamSearchResult(
                null, budget.StopReason,
                budget.ParentsExpanded, budget.SuccessorsEvaluated, budget.CompleteCandidatesFound,
                budget.EffectiveBeamWidth, budget.LocalEvaluationBudgetExhausted);
        }

        // A complete plan was produced. We KEEP the actual recorded stop
        // reason (e.g. DepthLimit, TimeBudget) — it is the honest answer
        // about why the search stopped, and matters more to the user than
        // a synthetic "Completed" that would also claim global optimality
        // by implication. A beam-mode search is never globally optimal
        // (the planner has a separate `Optimal` status for that path).
        var optimizeStart = Stopwatch.GetTimestamp();
        var final = VerifyAndImprove(request, bestRaw, budget, cancellationToken);
        if (RouteSearchProfiler.Current is { } fo) fo.FinalOptimizeTicks += Stopwatch.GetTimestamp() - optimizeStart;
        return new BeamSearchResult(
            final, budget.StopReason,
            budget.ParentsExpanded, budget.SuccessorsEvaluated, budget.CompleteCandidatesFound,
            budget.EffectiveBeamWidth, budget.LocalEvaluationBudgetExhausted);
    }

    private static IEnumerable<RouteSimulationState> RankCandidates(
        IEnumerable<RouteSimulationState> candidates, int beamWidth) =>
        candidates
            .OrderByDescending(state => BitOperations.PopCount(state.CompletedMask))
            .ThenBy(state => state.FinishedRoutes.Count)
            .ThenByDescending(state => state.CurrentRouteSteps.OfType<BarterStep>().Count())
            .ThenBy(state => state.TotalDistance)
            .ThenBy(state => state.PickupStopCount)
            .ThenBy(state => StableKey(state), StringComparer.Ordinal)
            .Take(beamWidth);

    private static Dictionary<string, RouteSimulationState> TrimCandidates(
        Dictionary<string, RouteSimulationState> candidates,
        int retainCount) {
        if (RouteSearchProfiler.Current is { } p) p.TrimCandidatesCalls++;
        return RankCandidates(candidates.Values, retainCount)
            .ToDictionary(SearchKey, state => state, StringComparer.Ordinal);
    }

    private static RouteIncumbent? VerifyAndImprove(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        RouteSearchBudget budget,
        CancellationToken cancellationToken) {
        if (RouteSearchProfiler.Current is { } p) p.VerifyAndImproveCalls++;
        // The local optimizers consume the same shared budget so a Deep run
        // cannot keep re-acquiring fresh quotas inside the finalization phase.
        // When the budget is exhausted we still call Verify so the planner
        // always returns a verified incumbent (correctness over last-mile
        // distance improvement).
        state = IntraRouteOrderOptimizer.Improve(request, state, budget, cancellationToken);
        if (budget.LocalEvaluations < budget.MaxLocalEvaluations)
            state = RoutePairRebuilder.Improve(request, state, budget, cancellationToken);
        if (budget.LocalEvaluations < budget.MaxLocalEvaluations)
            state = IntraRouteOrderOptimizer.Improve(request, state, budget, cancellationToken);
        var plan = RoutePlanFactory.FromState(
            request, state, RoutePlanStatus.BestKnownWithinLimit, []);
        var verification = RoutePlanVerifier.Verify(request, plan);
        return verification.Success ? new RouteIncumbent(verification.VerifiedPlan!, state) : null;
    }

    private static IEnumerable<RouteSimulationState> Expand(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        ulong fullMask) {
        foreach (int index in Enumerable.Range(0, request.Tasks.Count)
            .Where(index => (state.CompletedMask & (1UL << index)) == 0)
            .OrderBy(index => request.Tasks[index].RowId, StringComparer.Ordinal)) {
            var barter = RouteStateTransition.TryBarter(request, state, index);
            if (barter.Success) yield return barter.State;
        }

        ulong remaining = fullMask & ~state.CompletedMask;
        foreach (var warehouse in request.Warehouses.OrderBy(x => x.WarehouseId, StringComparer.Ordinal)) {
            if (state.VisitedWarehouseIds.Contains(warehouse.WarehouseId)) continue;
            foreach (var bundle in DemandBundleGenerator.Generate(
                request, state, warehouse.WarehouseId, remaining)) {
                var pickup = RouteStateTransition.TryPickup(
                    request, state, warehouse.WarehouseId, bundle.Items);
                if (pickup.Success && AutomaticRouteSearchPolicy.HasExecutableBarter(
                        request, pickup.State, remaining))
                    yield return pickup.State;
            }
        }

        if (state.CurrentRouteSteps.OfType<BarterStep>().Any()) {
            var unload = WarehouseUnloadPlanner.TryCompleteRoute(request, state);
            if (unload.Success) yield return unload.State;
        }
    }

    private static bool IsBetter(RouteSimulationState candidate, RouteSimulationState existing) {
        int routes = candidate.FinishedRoutes.Count.CompareTo(existing.FinishedRoutes.Count);
        if (routes != 0) return routes < 0;
        int distance = candidate.TotalDistance.CompareTo(existing.TotalDistance);
        if (distance != 0) return distance < 0;
        int pickups = candidate.PickupStopCount.CompareTo(existing.PickupStopCount);
        if (pickups != 0) return pickups < 0;
        return StringComparer.Ordinal.Compare(StableKey(candidate), StableKey(existing)) < 0;
    }

    private static string SearchKey(RouteSimulationState state) {
        if (RouteSearchProfiler.Current is { } p) p.SearchKeyCalls++;
        var builder = new StringBuilder();
        builder.Append(state.CurrentRouteNumber).Append('|')
            .Append(state.CurrentIslandId).Append('|')
            .Append(state.CompletedMask).Append('|')
            .Append(state.CurrentRouteSteps.OfType<BarterStep>().Any() ? '1' : '0').Append('|');
        foreach (string id in state.VisitedWarehouseIds.OrderBy(x => x, StringComparer.Ordinal))
            builder.Append(id).Append(',');
        AppendInventory(builder, state.OnBoard);
        foreach (var warehouse in state.WarehouseInventory.OrderBy(x => x.Key, StringComparer.Ordinal)) {
            builder.Append('#').Append(warehouse.Key).Append(':');
            AppendInventory(builder, warehouse.Value);
        }
        return builder.ToString();
    }

    private static void AppendInventory(
        StringBuilder builder,
        IReadOnlyDictionary<string, int> inventory) {
        foreach (var pair in inventory.Where(x => x.Value != 0).OrderBy(x => x.Key, StringComparer.Ordinal))
            builder.Append(pair.Key).Append('=').Append(pair.Value).Append(';');
    }

    private static string StableKey(RouteSimulationState state) =>
        RoutePlanFactory.StableRouteKey(state.FinishedRoutes, state.CurrentRouteSteps);

    private static RoutePlanObjective Objective(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state) => RoutePlanFactory.FromState(
            request, state, RoutePlanStatus.BestKnownWithinLimit, []).Objective!.Value;
}
