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

        state = ImproveLocally(request, state, cancellationToken);
        state = RoutePairRebuilder.Improve(request, state, cancellationToken);
        var plan = RoutePlanFactory.FromState(
            request, state, RoutePlanStatus.BestKnownWithinLimit, []);
        return new RouteIncumbent(plan, state);
    }

    private static RouteSimulationState ImproveLocally(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState initial,
        CancellationToken cancellationToken) {
        if (request.Limits.MaxLocalMoves == 0) return initial;

        var current = initial;
        var layout = ExtractLayout(current);
        var currentPlan = RoutePlanFactory.FromState(
            request, current, RoutePlanStatus.BestKnownWithinLimit, []);
        int attempted = 0;

        while (attempted < request.Limits.MaxLocalMoves) {
            RouteSimulationState? bestState = null;
            List<LocalRoute>? bestLayout = null;
            RoutePlanObjective bestObjective = currentPlan.Objective!.Value;

            void Evaluate(List<LocalRoute> candidate) {
                if (attempted >= request.Limits.MaxLocalMoves) return;
                attempted++;
                cancellationToken.ThrowIfCancellationRequested();
                var replayed = ReplayLayout(request, candidate);
                if (replayed is null) return;
                var candidatePlan = RoutePlanFactory.FromState(
                    request, replayed, RoutePlanStatus.BestKnownWithinLimit, []);
                var verification = RoutePlanVerifier.Verify(request, candidatePlan);
                if (!verification.Success || candidatePlan.Objective!.Value.CompareTo(bestObjective) >= 0) return;
                bestState = replayed;
                bestLayout = candidate;
                bestObjective = candidatePlan.Objective.Value;
            }

            // Relocate, then swap, then 2-opt. Every candidate is replayed from the
            // original request, so dependency, per-warehouse stock and LT rules are
            // authoritative rather than approximated by the local-move generator.
            for (int routeIndex = 0; routeIndex < layout.Count && attempted < request.Limits.MaxLocalMoves; routeIndex++) {
                int count = layout[routeIndex].Actions.Count;
                for (int from = 0; from < count && attempted < request.Limits.MaxLocalMoves; from++) {
                    for (int to = 0; to < count && attempted < request.Limits.MaxLocalMoves; to++) {
                        if (from == to) continue;
                        var candidate = CloneLayout(layout);
                        var action = candidate[routeIndex].Actions[from];
                        candidate[routeIndex].Actions.RemoveAt(from);
                        candidate[routeIndex].Actions.Insert(to, action);
                        Evaluate(candidate);
                    }
                }
            }
            for (int routeIndex = 0; routeIndex < layout.Count && attempted < request.Limits.MaxLocalMoves; routeIndex++) {
                int count = layout[routeIndex].Actions.Count;
                for (int left = 0; left < count - 1 && attempted < request.Limits.MaxLocalMoves; left++) {
                    for (int right = left + 1; right < count && attempted < request.Limits.MaxLocalMoves; right++) {
                        var candidate = CloneLayout(layout);
                        (candidate[routeIndex].Actions[left], candidate[routeIndex].Actions[right]) =
                            (candidate[routeIndex].Actions[right], candidate[routeIndex].Actions[left]);
                        Evaluate(candidate);
                    }
                }
            }
            for (int routeIndex = 0; routeIndex < layout.Count && attempted < request.Limits.MaxLocalMoves; routeIndex++) {
                int count = layout[routeIndex].Actions.Count;
                for (int left = 0; left < count - 1 && attempted < request.Limits.MaxLocalMoves; left++) {
                    for (int right = left + 1; right < count && attempted < request.Limits.MaxLocalMoves; right++) {
                        var candidate = CloneLayout(layout);
                        candidate[routeIndex].Actions.Reverse(left, right - left + 1);
                        Evaluate(candidate);
                    }
                }
            }

            if (bestState is null || bestLayout is null) break;
            current = bestState;
            layout = bestLayout;
            currentPlan = RoutePlanFactory.FromState(
                request, current, RoutePlanStatus.BestKnownWithinLimit, []);
        }
        return current;
    }

    private static List<LocalRoute> ExtractLayout(RouteSimulationState state) =>
        state.FinishedRoutes.Select(route => new LocalRoute(
            route.Steps.Where(step => step is not WarehouseUnloadStep).ToList())).ToList();

    private static List<LocalRoute> CloneLayout(IEnumerable<LocalRoute> source) =>
        source.Select(route => new LocalRoute(route.Actions.ToList())).ToList();

    private static RouteSimulationState? ReplayLayout(
        AutomaticRoutePlanningRequest request,
        IReadOnlyList<LocalRoute> layout) {
        var state = RouteSimulationState.CreateInitial(request);
        foreach (var route in layout) {
            foreach (var action in route.Actions) {
                RouteTransitionResult result = action switch {
                    WarehousePickupStep pickup => RouteStateTransition.TryPickup(
                        request, state, pickup.WarehouseId, pickup.Items),
                    BarterStep barter => RouteStateTransition.TryBarter(
                        request, state, FindTaskIndex(request, barter.RowId)),
                    _ => new RouteTransitionResult(false, state, null,
                        new RouteDiagnostic("invalid-local-action")),
                };
                if (!result.Success) return null;
                state = result.State;
            }
            var unload = WarehouseUnloadPlanner.TryCompleteRoute(request, state);
            if (!unload.Success) return null;
            state = unload.State;
        }
        return state;
    }

    private static int FindTaskIndex(AutomaticRoutePlanningRequest request, string rowId) {
        for (int i = 0; i < request.Tasks.Count; i++)
            if (string.Equals(request.Tasks[i].RowId, rowId, StringComparison.Ordinal)) return i;
        return -1;
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

    private sealed record LocalRoute(List<RouteStep> Actions);
}
