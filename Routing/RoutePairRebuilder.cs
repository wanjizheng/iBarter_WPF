namespace iBarter.Routing;

using System.Numerics;

/// <summary>
/// Bounded pairwise destroy/rebuild pass. Two existing routes are pooled and
/// rebuilt as one route; the entire remaining plan is replayed before a
/// candidate may replace the incumbent.
/// </summary>
public static class RoutePairRebuilder {
    // Original 3-arg entry: unchanged from baseline so existing tests see the
    // exact same behavior. Budget-aware overload added below for the shared
    // RouteSearchBudget path.
    public static RouteSimulationState Improve(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState initial,
        CancellationToken cancellationToken) {
        if (RouteSearchProfiler.Current is { } p) p.RoutePairRebuildCalls++;
        if (request.Limits.MaxLocalMoves <= 0 || initial.FinishedRoutes.Count < 2) return initial;

        var current = initial;
        int attempted = 0;
        while (attempted < request.Limits.MaxLocalMoves) {
            var routes = current.FinishedRoutes.ToArray();
            var currentPlan = RoutePlanFactory.FromState(
                request, current, RoutePlanStatus.BestKnownWithinLimit, []);
            RouteSimulationState? best = null;
            RoutePlanObjective bestObjective = currentPlan.Objective!.Value;

            for (int left = 0; left < routes.Length - 1 && attempted < request.Limits.MaxLocalMoves; left++) {
                for (int right = left + 1; right < routes.Length && attempted < request.Limits.MaxLocalMoves; right++) {
                    attempted++;
                    cancellationToken.ThrowIfCancellationRequested();
                    var candidate = TryRebuild(request, routes, left, right);
                    if (candidate is null) continue;
                    var plan = RoutePlanFactory.FromState(
                        request, candidate, RoutePlanStatus.BestKnownWithinLimit, []);
                    if (plan.Objective is null || plan.Objective.Value.CompareTo(bestObjective) >= 0) continue;
                    if (!RoutePlanVerifier.Verify(request, plan).Success) continue;
                    best = candidate;
                    bestObjective = plan.Objective.Value;
                }
            }

            if (best is null) break;
            current = best;
        }
        return current;
    }

    public static RouteSimulationState Improve(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState initial,
        RouteSearchBudget? budget,
        CancellationToken cancellationToken) {
        if (budget is null) return Improve(request, initial, cancellationToken);

        if (RouteSearchProfiler.Current is { } p) p.RoutePairRebuildCalls++;
        if (budget.MaxLocalEvaluations <= 0 || initial.FinishedRoutes.Count < 2) return initial;

        var current = initial;
        while (true) {
            var routes = current.FinishedRoutes.ToArray();
            var currentPlan = RoutePlanFactory.FromState(
                request, current, RoutePlanStatus.BestKnownWithinLimit, []);
            RouteSimulationState? best = null;
            RoutePlanObjective bestObjective = currentPlan.Objective!.Value;
            bool budgetExhausted = false;

            for (int left = 0; left < routes.Length - 1 && !budgetExhausted; left++) {
                for (int right = left + 1; right < routes.Length; right++) {
                    if (budget.TargetTimeExpired) { budgetExhausted = true; break; }
                    if (!budget.TryConsumeLocalEvaluation()) { budgetExhausted = true; break; }
                    cancellationToken.ThrowIfCancellationRequested();
                    var candidate = TryRebuild(request, routes, left, right);
                    if (candidate is null) continue;
                    var plan = RoutePlanFactory.FromState(
                        request, candidate, RoutePlanStatus.BestKnownWithinLimit, []);
                    if (plan.Objective is null || plan.Objective.Value.CompareTo(bestObjective) >= 0) continue;
                    if (!RoutePlanVerifier.Verify(request, plan).Success) continue;
                    best = candidate;
                    bestObjective = plan.Objective.Value;
                }
            }

            if (budgetExhausted) {
                // Exhausted mid-pair-walk: keep the round's best (if any) and stop.
                if (best is not null) current = best;
                break;
            }
            if (best is null) break;
            current = best;
        }
        return current;
    }

    private static RouteSimulationState? TryRebuild(
        AutomaticRoutePlanningRequest request,
        IReadOnlyList<PlannedRoute> routes,
        int left,
        int right) {
        var pooledIndexes = routes[left].Steps.OfType<BarterStep>()
            .Concat(routes[right].Steps.OfType<BarterStep>())
            .Select(x => FindTaskIndex(request, x.RowId))
            .ToArray();
        if (pooledIndexes.Length == 0 || pooledIndexes.Any(x => x < 0)) return null;

        var state = RouteSimulationState.CreateInitial(request);
        for (int routeIndex = 0; routeIndex < routes.Count; routeIndex++) {
            if (routeIndex == left) {
                var rebuilt = BuildCombinedRoute(request, state, pooledIndexes);
                if (rebuilt is null) return null;
                state = rebuilt;
                continue;
            }
            if (routeIndex == right) continue;
            var replayed = ReplayRoute(request, state, routes[routeIndex]);
            if (replayed is null) return null;
            state = replayed;
        }
        return state;
    }

    private static RouteSimulationState? BuildCombinedRoute(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState start,
        IReadOnlyList<int> taskIndexes) {
        ulong poolMask = taskIndexes.Aggregate(0UL, (mask, index) => mask | 1UL << index);
        var state = start;
        int guard = Math.Max(100, taskIndexes.Count * request.Warehouses.Count * 20);

        while ((state.CompletedMask & poolMask) != poolMask && guard-- > 0) {
            ulong remaining = poolMask & ~state.CompletedMask;
            var executable = taskIndexes
                .Where(index => (remaining & (1UL << index)) != 0)
                .Select(index => (Index: index, Result: RouteStateTransition.TryBarter(request, state, index)))
                .Where(x => x.Result.Success)
                .OrderBy(x => WeightDelta(request, request.Tasks[x.Index]))
                .ThenBy(x => x.Result.State.TotalDistance - state.TotalDistance)
                .ThenBy(x => request.Tasks[x.Index].RowId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (executable.Result is not null) {
                state = executable.Result.State;
                continue;
            }

            var pickups = new List<(int Supported, double Distance, int CargoLT, string WarehouseId,
                string StableKey, RouteTransitionResult Result)>();
            foreach (var warehouse in request.Warehouses.OrderBy(x => x.WarehouseId, StringComparer.Ordinal)) {
                if (state.VisitedWarehouseIds.Contains(warehouse.WarehouseId)) continue;
                foreach (var bundle in DemandBundleGenerator.Generate(request, state, warehouse.WarehouseId, remaining)) {
                    var result = RouteStateTransition.TryPickup(request, state, warehouse.WarehouseId, bundle.Items);
                    if (!result.Success) continue;
                    pickups.Add((
                        BitOperations.PopCount(bundle.SupportedTaskMask & remaining),
                        result.State.TotalDistance - state.TotalDistance,
                        bundle.TotalCargoLT,
                        warehouse.WarehouseId,
                        bundle.StableKey,
                        result));
                }
            }
            var pickup = pickups
                .OrderByDescending(x => x.Supported)
                .ThenBy(x => x.Distance)
                .ThenBy(x => x.CargoLT)
                .ThenBy(x => x.WarehouseId, StringComparer.Ordinal)
                .ThenBy(x => x.StableKey, StringComparer.Ordinal)
                .FirstOrDefault();
            if (pickup.Result is null) return null;
            state = pickup.Result.State;
        }

        if ((state.CompletedMask & poolMask) != poolMask) return null;
        var unload = WarehouseUnloadPlanner.TryCompleteRoute(request, state);
        return unload.Success ? unload.State : null;
    }

    private static RouteSimulationState? ReplayRoute(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        PlannedRoute route) {
        foreach (var action in route.Steps.Where(x => x is not WarehouseUnloadStep)) {
            RouteTransitionResult result = action switch {
                WarehousePickupStep pickup => RouteStateTransition.TryPickup(
                    request, state, pickup.WarehouseId, pickup.Items),
                BarterStep barter => RouteStateTransition.TryBarter(
                    request, state, FindTaskIndex(request, barter.RowId)),
                _ => new RouteTransitionResult(false, state, null, new RouteDiagnostic("invalid-pair-action")),
            };
            if (!result.Success) return null;
            state = result.State;
        }
        var unload = WarehouseUnloadPlanner.TryCompleteRoute(request, state);
        return unload.Success ? unload.State : null;
    }

    private static long WeightDelta(AutomaticRoutePlanningRequest request, RouteBarterTask task) =>
        (long)request.Items[task.Item2Id].UnitWeight * task.OutputQuantity
        - (long)request.Items[task.Item1Id].UnitWeight * task.InputQuantity;

    private static int FindTaskIndex(AutomaticRoutePlanningRequest request, string rowId) {
        for (int i = 0; i < request.Tasks.Count; i++)
            if (StringComparer.Ordinal.Equals(request.Tasks[i].RowId, rowId)) return i;
        return -1;
    }
}
