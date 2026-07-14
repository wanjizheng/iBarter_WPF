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

    public static RouteIncumbent? TryBuildIncumbent(
        AutomaticRoutePlanningRequest request,
        RoutePreflightResult preflight,
        CancellationToken cancellationToken,
        int? maxExpandedStates = null) {
        if (!preflight.IsValid || request.Tasks.Count == 0) return null;
        ulong fullMask = request.Tasks.Count == 64 ? ulong.MaxValue : (1UL << request.Tasks.Count) - 1;
        var frontier = new[] { RouteSimulationState.CreateInitial(request) };
        int expanded = 0;
        int maxDepth = Math.Max(64, request.Tasks.Count * 4 + request.Warehouses.Count * request.Tasks.Count);
        RouteIncumbent? bestComplete = null;

        for (int depth = 0; depth < maxDepth && frontier.Length > 0; depth++) {
            cancellationToken.ThrowIfCancellationRequested();
            var complete = frontier
                .Where(state => state.CompletedMask == fullMask && state.CurrentRouteSteps.Count == 0)
                .OrderBy(state => Objective(request, state))
                .ThenBy(StableKey, StringComparer.Ordinal)
                .FirstOrDefault();
            if (complete is not null) {
                var candidate = VerifyAndImprove(request, complete, cancellationToken);
                if (candidate?.Plan.Objective is { } candidateObjective
                    && (bestComplete?.Plan.Objective is null
                        || candidateObjective.CompareTo(bestComplete.Plan.Objective.Value) < 0))
                    bestComplete = candidate;
            }

            var candidates = new Dictionary<string, RouteSimulationState>(StringComparer.Ordinal);
            foreach (var state in frontier) {
                if (state.CompletedMask == fullMask && state.CurrentRouteSteps.Count == 0) continue;
                if (++expanded > (maxExpandedStates ?? request.Limits.MaxExpandedStates)) return bestComplete;
                foreach (var successor in Expand(request, state, fullMask)) {
                    string key = SearchKey(successor);
                    if (!candidates.TryGetValue(key, out var existing) || IsBetter(successor, existing))
                        candidates[key] = successor;
                }
            }

            frontier = candidates.Values
                .OrderByDescending(state => BitOperations.PopCount(state.CompletedMask))
                .ThenBy(state => state.FinishedRoutes.Count)
                .ThenByDescending(state => state.CurrentRouteSteps.OfType<BarterStep>().Count())
                .ThenBy(state => state.TotalDistance)
                .ThenBy(state => state.PickupStopCount)
                .ThenBy(state => StableKey(state), StringComparer.Ordinal)
                .Take(BeamWidth)
                .ToArray();
        }
        return bestComplete;
    }

    private static RouteIncumbent? VerifyAndImprove(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        CancellationToken cancellationToken) {
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
