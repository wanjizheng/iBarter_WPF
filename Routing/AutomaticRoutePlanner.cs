using System.Numerics;
using System.Text;

namespace iBarter.Routing;

public sealed class AutomaticRoutePlanner {
    public RoutePlan Plan(
        AutomaticRoutePlanningRequest request,
        CancellationToken cancellationToken = default) {
        string fingerprint = RoutePlanFingerprint.Compute(request);
        RoutePlan? incumbent = null;
        try {
            cancellationToken.ThrowIfCancellationRequested();
            var preflight = AutomaticRoutePreflight.Validate(request);
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

            var heuristicIncumbent = AutomaticRouteHeuristic.TryBuildIncumbent(
                request, preflight, cancellationToken)?.Plan;
            if (heuristicIncumbent is not null) {
                var checkedIncumbent = RoutePlanVerifier.Verify(request, heuristicIncumbent);
                incumbent = checkedIncumbent.Success ? checkedIncumbent.VerifiedPlan : null;
            }

            if (request.Tasks.Count > AutomaticRouteSearchPolicy.ExactTaskLimit) {
                var beamPlan = AutomaticRouteBeamSearch.TryBuildIncumbent(
                    request, preflight, cancellationToken,
                    AutomaticRouteSearchPolicy.BeamStateBudget(request.Tasks.Count, request.Limits.MaxExpandedStates))?.Plan;
                if (beamPlan is not null) {
                    var checkedBeam = RoutePlanVerifier.Verify(request, beamPlan);
                    if (checkedBeam.Success && checkedBeam.VerifiedPlan?.Objective is { } beamObjective
                        && (incumbent?.Objective is null
                            || beamObjective.CompareTo(incumbent.Objective.Value) < 0))
                        incumbent = checkedBeam.VerifiedPlan;
                }
                var diagnostic = new RouteDiagnostic(
                    "exact-search-skipped",
                    Detail: $"{request.Tasks.Count}>{AutomaticRouteSearchPolicy.ExactTaskLimit}");
                if (incumbent is null)
                    return new RoutePlan(RoutePlanStatus.NoFeasibleSolutionWithinLimit,
                        [], null, [diagnostic], fingerprint);
                return new RoutePlan(RoutePlanStatus.BestKnownWithinLimit,
                    incumbent.Routes, incumbent.Objective, [diagnostic], fingerprint);
            }

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
                if (expanded >= request.Limits.MaxExpandedStates) {
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

    // Beam expansion cost rises sharply once exact subset search is skipped.
    // Medium plans retain enough states for inventory-first ordering, while
    // full 20+ task resets use a tighter cap so UI publication stays prompt.
    private const int MediumTaskBeamStateLimit = 5_000;
    private const int LargeTaskBeamThreshold = 20;
    private const int LargeTaskBeamStateLimit = 2_000;

    public static int BeamStateBudget(int taskCount, int requestedBudget) =>
        taskCount >= LargeTaskBeamThreshold
            ? Math.Min(requestedBudget, LargeTaskBeamStateLimit)
            : taskCount > ExactTaskLimit
                ? Math.Min(requestedBudget, MediumTaskBeamStateLimit)
                : requestedBudget;

    public static bool HasExecutableBarter(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        ulong remainingMask) {
        for (int index = 0; index < request.Tasks.Count; index++) {
            if ((remainingMask & (1UL << index)) == 0) continue;
            if (RouteStateTransition.TryBarter(request, state, index).Success)
                return true;
        }
        return false;
    }
}
