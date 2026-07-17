using System.Diagnostics;
using System.Numerics;
using System.Text;

namespace iBarter.Routing;

/// <summary>
/// A bounded feasibility fallback for large plans.  It keeps several distinct
/// partial cargo states instead of committing to the heuristic's single greedy
/// pickup/exchange choice.
/// </summary>
public static class AutomaticRouteBeamSearch {
    private const int BeamWidth = 512;
    private const int CandidateRetainLimit = BeamWidth * 4;
    private const int CandidateTrimThreshold = CandidateRetainLimit * 2;

    public static RouteIncumbent? TryBuildIncumbent(
        AutomaticRoutePlanningRequest request,
        RoutePreflightResult preflight,
        CancellationToken cancellationToken,
        int? maxExpandedStates = null,
        long deadlineTimestamp = 0) {
        if (!preflight.IsValid || request.Tasks.Count == 0) return null;
        ulong fullMask = request.Tasks.Count == 64 ? ulong.MaxValue : (1UL << request.Tasks.Count) - 1;
        var frontier = new[] { RouteSimulationState.CreateInitial(request) };
        int expanded = 0;
        int budget = maxExpandedStates ?? request.Limits.MaxExpandedStates;
        int maxDepth = Math.Max(64, request.Tasks.Count * 4 + request.Warehouses.Count * request.Tasks.Count);

        // The best fully-complete state seen so far, tracked cheaply via its raw
        // objective (which already carries a deterministic stable tie-break). The
        // expensive local optimization and full verification run exactly once on
        // this final winner after the search loop, rather than on every
        // completion at every beam depth.
        RouteSimulationState? bestRaw = null;
        RoutePlanObjective? bestRawObjective = null;
        bool stop = false;

        for (int depth = 0; depth < maxDepth && frontier.Length > 0 && !stop; depth++) {
            cancellationToken.ThrowIfCancellationRequested();
            if (Expired(deadlineTimestamp)) break;

            var completeStart = Stopwatch.GetTimestamp();
            foreach (var state in frontier) {
                if (state.CompletedMask != fullMask || state.CurrentRouteSteps.Count != 0) continue;
                var objective = Objective(request, state);
                if (bestRawObjective is null || objective.CompareTo(bestRawObjective.Value) < 0) {
                    bestRaw = state;
                    bestRawObjective = objective;
                }
            }
            if (RouteSearchProfiler.Current is { } tc) tc.BeamCompleteTicks += Stopwatch.GetTimestamp() - completeStart;

            var expandStart = Stopwatch.GetTimestamp();
            var candidates = new Dictionary<string, RouteSimulationState>(StringComparer.Ordinal);
            foreach (var state in frontier) {
                if (state.CompletedMask == fullMask && state.CurrentRouteSteps.Count == 0) continue;
                if (++expanded > budget || Expired(deadlineTimestamp)) { stop = true; break; }
                if (RouteSearchProfiler.Current is { } pp) pp.BeamParentsExpanded++;
                foreach (var successor in Expand(request, state, fullMask)) {
                    if (RouteSearchProfiler.Current is { } sp) sp.SuccessorsGenerated++;
                    string key = SearchKey(successor);
                    if (!candidates.TryGetValue(key, out var existing) || IsBetter(successor, existing))
                        candidates[key] = successor;
                    if (candidates.Count >= CandidateTrimThreshold)
                        candidates = TrimCandidates(candidates, CandidateRetainLimit);
                }
            }
            if (RouteSearchProfiler.Current is { } cp) cp.CandidatesDeduped += candidates.Count;
            if (RouteSearchProfiler.Current is { } te) te.BeamExpandTicks += Stopwatch.GetTimestamp() - expandStart;
            if (stop) break;

            var rankStart = Stopwatch.GetTimestamp();
            frontier = RankCandidates(candidates.Values)
                .Take(BeamWidth)
                .ToArray();
            if (RouteSearchProfiler.Current is { } tr) tr.BeamRankTicks += Stopwatch.GetTimestamp() - rankStart;
        }

        if (bestRaw is null) return null;
        var optimizeStart = Stopwatch.GetTimestamp();
        var result = VerifyAndImprove(request, bestRaw, cancellationToken);
        if (RouteSearchProfiler.Current is { } fo) fo.FinalOptimizeTicks += Stopwatch.GetTimestamp() - optimizeStart;
        return result;
    }

    private static bool Expired(long deadlineTimestamp) =>
        deadlineTimestamp != 0 && Stopwatch.GetTimestamp() >= deadlineTimestamp;

    private static IOrderedEnumerable<RouteSimulationState> RankCandidates(
        IEnumerable<RouteSimulationState> candidates) =>
        candidates
            .OrderByDescending(state => BitOperations.PopCount(state.CompletedMask))
            .ThenBy(state => state.FinishedRoutes.Count)
            .ThenByDescending(state => state.CurrentRouteSteps.OfType<BarterStep>().Count())
            .ThenBy(state => state.TotalDistance)
            .ThenBy(state => state.PickupStopCount)
            .ThenBy(state => StableKey(state), StringComparer.Ordinal);

    private static Dictionary<string, RouteSimulationState> TrimCandidates(
        Dictionary<string, RouteSimulationState> candidates,
        int retainCount) {
        if (RouteSearchProfiler.Current is { } p) p.TrimCandidatesCalls++;
        return RankCandidates(candidates.Values)
            .Take(retainCount)
            .ToDictionary(SearchKey, state => state, StringComparer.Ordinal);
    }

    private static RouteIncumbent? VerifyAndImprove(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        CancellationToken cancellationToken) {
        if (RouteSearchProfiler.Current is { } p) p.VerifyAndImproveCalls++;
        state = IntraRouteOrderOptimizer.Improve(request, state, cancellationToken);
        state = RoutePairRebuilder.Improve(request, state, cancellationToken);
        state = IntraRouteOrderOptimizer.Improve(request, state, cancellationToken);
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
