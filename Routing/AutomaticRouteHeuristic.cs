using System.Numerics;

namespace iBarter.Routing;

public sealed record RouteIncumbent(RoutePlan Plan, RouteSimulationState FinalState);

public static class AutomaticRouteHeuristic {
    public static RouteIncumbent? TryBuildIncumbent(
        AutomaticRoutePlanningRequest request,
        RoutePreflightResult preflight,
        CancellationToken cancellationToken) {
        if (!preflight.IsValid || request.Tasks.Count == 0) return null;
        ulong fullMask = request.Tasks.Count == 64 ? ulong.MaxValue : (1UL << request.Tasks.Count) - 1;
        var state = RouteSimulationState.CreateInitial(request);
        int guard = 0;

        while (state.CompletedMask != fullMask || state.CurrentRouteSteps.Count > 0) {
            cancellationToken.ThrowIfCancellationRequested();
            if (++guard > Math.Max(1_000, request.Tasks.Count * request.Warehouses.Count * 100)) return null;

            if (state.CompletedMask == fullMask) {
                var unload = NearestUnload(request, state);
                if (unload is null) return null;
                state = unload.State;
                continue;
            }

            var barter = BestExecutableBarter(request, state);
            if (barter is not null) {
                state = barter.State;
                continue;
            }

            ulong remaining = fullMask & ~state.CompletedMask;
            var pickup = BestPickup(request, state, remaining);
            if (pickup is not null) {
                state = pickup.State;
                continue;
            }

            if (state.CurrentRouteSteps.OfType<BarterStep>().Any()) {
                var unload = NearestUnload(request, state);
                if (unload is null) return null;
                state = unload.State;
                continue;
            }
            return null;
        }

        state = IntraRouteOrderOptimizer.Improve(request, state, cancellationToken);
        state = RoutePairRebuilder.Improve(request, state, cancellationToken);
        state = IntraRouteOrderOptimizer.Improve(request, state, cancellationToken);
        var plan = RoutePlanFactory.FromState(
            request, state, RoutePlanStatus.BestKnownWithinLimit, []);
        return new RouteIncumbent(plan, state);
    }


    private static RouteTransitionResult? BestExecutableBarter(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state) {
        return Enumerable.Range(0, request.Tasks.Count)
            .Where(i => (state.CompletedMask & (1UL << i)) == 0)
            .Select(i => (Index: i, Result: RouteStateTransition.TryBarter(request, state, i)))
            .Where(x => x.Result.Success)
            .OrderBy(x => x.Result.State.TotalDistance - state.TotalDistance)
            .ThenBy(x => request.Tasks[x.Index].RowId, StringComparer.Ordinal)
            .Select(x => x.Result)
            .FirstOrDefault();
    }

    private static RouteTransitionResult? BestPickup(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        ulong remainingMask) {
        var candidates = new List<PickupCandidate>();
        foreach (var warehouse in request.Warehouses.OrderBy(x => x.WarehouseId, StringComparer.Ordinal)) {
            if (state.VisitedWarehouseIds.Contains(warehouse.WarehouseId)) continue;
            foreach (var bundle in DemandBundleGenerator.Generate(request, state, warehouse.WarehouseId, remainingMask)) {
                var result = RouteStateTransition.TryPickup(request, state, warehouse.WarehouseId, bundle.Items);
                if (!result.Success) continue;
                if (!AutomaticRouteSearchPolicy.HasExecutableBarter(
                        request, result.State, remainingMask)) continue;
                candidates.Add(new PickupCandidate(
                    result.State.TotalDistance - state.TotalDistance,
                    BitOperations.PopCount(bundle.SupportedTaskMask),
                    bundle.TotalCargoLT,
                    warehouse.WarehouseId,
                    bundle.StableKey,
                    result));
            }
        }
        return candidates
            .OrderBy(x => x.Distance)
            .ThenByDescending(x => x.SupportedTasks)
            .ThenBy(x => x.TotalCargoLT)
            .ThenBy(x => x.WarehouseId, StringComparer.Ordinal)
            .ThenBy(x => x.StableKey, StringComparer.Ordinal)
            .Select(x => x.Result)
            .FirstOrDefault();
    }

    private static RouteTransitionResult? NearestUnload(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state) {
        var result = WarehouseUnloadPlanner.TryCompleteRoute(request, state);
        return result.Success ? result : null;
    }

    private sealed record PickupCandidate(
        double Distance,
        int SupportedTasks,
        int TotalCargoLT,
        string WarehouseId,
        string StableKey,
        RouteTransitionResult Result);
}
