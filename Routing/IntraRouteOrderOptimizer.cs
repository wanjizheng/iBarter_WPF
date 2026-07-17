namespace iBarter.Routing;

/// <summary>
/// Replays every finished route with the same pickup/barter action set and
/// searches dependency-feasible orders.  The budget is reset per route so a
/// large early route cannot starve later routes of all local-improvement work.
/// </summary>
public static class IntraRouteOrderOptimizer {
    public static RouteSimulationState Improve(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState initial,
        CancellationToken cancellationToken) {
        if (RouteSearchProfiler.Current is { } p) p.IntraRouteImproveCalls++;
        if (request.Limits.MaxLocalMoves <= 0 || initial.FinishedRoutes.Count == 0) return initial;

        var rebuilt = RouteSimulationState.CreateInitial(request);
        foreach (var route in initial.FinishedRoutes) {
            cancellationToken.ThrowIfCancellationRequested();
            var optimized = OptimizeRoute(request, rebuilt, route, cancellationToken);
            if (optimized is null) return initial;
            rebuilt = optimized;
        }

        var originalPlan = RoutePlanFactory.FromState(
            request, initial, RoutePlanStatus.BestKnownWithinLimit, []);
        var rebuiltPlan = RoutePlanFactory.FromState(
            request, rebuilt, RoutePlanStatus.BestKnownWithinLimit, []);
        if (rebuiltPlan.Objective is null || originalPlan.Objective is null
            || rebuiltPlan.Objective.Value.CompareTo(originalPlan.Objective.Value) >= 0
            || !RoutePlanVerifier.Verify(request, rebuiltPlan).Success)
            return initial;
        return rebuilt;
    }

    private static RouteSimulationState? OptimizeRoute(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState start,
        PlannedRoute route,
        CancellationToken cancellationToken) {
        var actions = route.Steps.Where(step => step is not WarehouseUnloadStep).ToArray();
        if (actions.Length == 0) return ReplayOriginal(request, start, actions);

        var original = ReplayOriginal(request, start, actions);
        if (original is null) return null;
        var best = original;
        var bestActions = actions;
        var bestObjective = RoutePlanFactory.FromState(
            request, best, RoutePlanStatus.BestKnownWithinLimit, []).Objective!.Value;

        if (actions.Length <= 12) {
            var frontier = new Dictionary<SearchKey, RouteSimulationState> {
                [new SearchKey(0, start.CurrentIslandId)] = start,
            };
            int expanded = 0;

            for (int depth = 0; depth < actions.Length && frontier.Count > 0; depth++) {
                var next = new Dictionary<SearchKey, RouteSimulationState>();
                foreach (var entry in frontier
                    .OrderBy(x => x.Value.TotalDistance)
                    .ThenBy(x => x.Key.IslandId, StringComparer.Ordinal)
                    .ThenBy(x => x.Key.Mask)) {
                    for (int actionIndex = 0; actionIndex < actions.Length; actionIndex++) {
                        if ((entry.Key.Mask & (1UL << actionIndex)) != 0) continue;
                        if (++expanded > request.Limits.MaxLocalMoves) break;
                        cancellationToken.ThrowIfCancellationRequested();

                        var transition = Apply(request, entry.Value, actions[actionIndex]);
                        if (!transition.Success) continue;
                        ulong mask = entry.Key.Mask | (1UL << actionIndex);
                        var key = new SearchKey(mask, transition.State.CurrentIslandId);
                        if (!next.TryGetValue(key, out var incumbent)
                            || IsBetterPartial(transition.State, incumbent))
                            next[key] = transition.State;
                    }
                    if (expanded >= request.Limits.MaxLocalMoves) break;
                }
                frontier = next;
            }

            ulong fullMask = (1UL << actions.Length) - 1;
            foreach (var state in frontier
                .Where(x => x.Key.Mask == fullMask)
                .Select(x => x.Value)) {
                var unload = WarehouseUnloadPlanner.TryCompleteRoute(request, state);
                if (!unload.Success) continue;
                var plan = RoutePlanFactory.FromState(
                    request, unload.State, RoutePlanStatus.BestKnownWithinLimit, []);
                if (plan.Objective is null || plan.Objective.Value.CompareTo(bestObjective) >= 0) continue;
                best = unload.State;
                bestObjective = plan.Objective.Value;
                bestActions = state.CurrentRouteSteps.ToArray();
            }
        }

        return ImproveByRelocation(
            request, start, bestActions, best, bestObjective, cancellationToken);
    }

    private static RouteSimulationState ImproveByRelocation(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState start,
        IReadOnlyList<RouteStep> initialActions,
        RouteSimulationState initialBest,
        RoutePlanObjective initialObjective,
        CancellationToken cancellationToken) {
        var actions = initialActions.ToList();
        var best = initialBest;
        var bestObjective = initialObjective;
        int evaluated = 0;

        while (evaluated < request.Limits.MaxLocalMoves) {
            RouteSimulationState? roundBest = null;
            RoutePlanObjective? roundObjective = null;
            List<RouteStep>? roundActions = null;

            for (int from = 0; from < actions.Count && evaluated < request.Limits.MaxLocalMoves; from++) {
                for (int to = 0; to < actions.Count && evaluated < request.Limits.MaxLocalMoves; to++) {
                    if (from == to) continue;
                    cancellationToken.ThrowIfCancellationRequested();
                    evaluated++;
                    var candidateActions = actions.ToList();
                    var moved = candidateActions[from];
                    candidateActions.RemoveAt(from);
                    candidateActions.Insert(to, moved);
                    var candidate = ReplayOriginal(request, start, candidateActions);
                    if (candidate is null) continue;
                    var objective = RoutePlanFactory.FromState(
                        request, candidate, RoutePlanStatus.BestKnownWithinLimit, []).Objective!.Value;
                    if (objective.CompareTo(bestObjective) >= 0
                        || roundObjective is { } existing && objective.CompareTo(existing) >= 0)
                        continue;
                    roundBest = candidate;
                    roundObjective = objective;
                    roundActions = candidateActions;
                }
            }

            if (roundBest is null || roundObjective is null || roundActions is null) break;
            best = roundBest;
            bestObjective = roundObjective.Value;
            actions = roundActions;
        }
        return best;
    }

    private static RouteSimulationState? ReplayOriginal(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        IReadOnlyList<RouteStep> actions) {
        foreach (var action in actions) {
            var transition = Apply(request, state, action);
            if (!transition.Success) return null;
            state = transition.State;
        }
        var unload = WarehouseUnloadPlanner.TryCompleteRoute(request, state);
        return unload.Success ? unload.State : null;
    }

    private static RouteTransitionResult Apply(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        RouteStep action) => action switch {
            WarehousePickupStep pickup => RouteStateTransition.TryPickup(
                request, state, pickup.WarehouseId, pickup.Items),
            BarterStep barter => RouteStateTransition.TryBarter(
                request, state, FindTaskIndex(request, barter.RowId)),
            _ => new RouteTransitionResult(
                false, state, null, new RouteDiagnostic("invalid-route-order-action")),
        };

    private static bool IsBetterPartial(RouteSimulationState candidate, RouteSimulationState incumbent) {
        int distance = candidate.TotalDistance.CompareTo(incumbent.TotalDistance);
        if (distance != 0) return distance < 0;
        int peak = candidate.CurrentRoutePeakLT.CompareTo(incumbent.CurrentRoutePeakLT);
        if (peak != 0) return peak < 0;
        return StableKey(candidate).CompareTo(StableKey(incumbent), StringComparison.Ordinal) < 0;
    }

    private static string StableKey(RouteSimulationState state) => string.Join(">",
        state.CurrentRouteSteps.Select(step => step switch {
            WarehousePickupStep pickup => $"P:{pickup.WarehouseId}",
            BarterStep barter => $"B:{barter.RowId}",
            _ => step.IslandId,
        }));

    private static int FindTaskIndex(AutomaticRoutePlanningRequest request, string rowId) {
        for (int i = 0; i < request.Tasks.Count; i++)
            if (StringComparer.Ordinal.Equals(request.Tasks[i].RowId, rowId)) return i;
        return -1;
    }

    private readonly record struct SearchKey(ulong Mask, string IslandId);
}
