namespace iBarter.Routing;

/// <summary>
/// Moves a warehouse-island barter onto the end of an earlier route when that
/// route is already returning the required input to the same warehouse.
///
/// This is an operational optimization rather than a sailing-distance
/// optimization: assigning the barter to the route before or after the
/// warehouse boundary has identical distance, but doing it before unloading
/// avoids picking the input back up and carrying the final reward through an
/// unrelated route. After the boundary move, the existing pair rebuilder gets
/// one opportunity to reuse the released capacity and reduce distance/routes.
/// </summary>
public static class WarehouseBoundaryOptimizer {
    public static WarehouseBoundaryOptimizationResult Improve(
        AutomaticRoutePlanningRequest request,
        RoutePlan plan,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Routes.Count < 2)
            return new WarehouseBoundaryOptimizationResult(plan, false);
        if (!TryReadSimpleLayouts(request, plan, out _))
            return new WarehouseBoundaryOptimizationResult(plan, false);

        RoutePlan compacted = CompactToFixedPoint(
            request, plan, cancellationToken, out bool compactedChanged);
        if (!compactedChanged)
            return new WarehouseBoundaryOptimizationResult(plan, false);

        RoutePlan result = compacted;
        if (TryReadSimpleLayouts(request, compacted, out var compactedLayouts)
            && TryBuildState(request, compactedLayouts, compacted.Status,
                compacted.Diagnostics, out RouteSimulationState compactedState)) {
            RouteSimulationState paired = RoutePairRebuilder.Improve(
                request, compactedState, cancellationToken);
            RoutePlan pairedPlan = RoutePlanFactory.FromState(
                request, paired, compacted.Status, compacted.Diagnostics);
            if (pairedPlan.Objective is { } pairedObjective
                && compacted.Objective is { } compactedObjective
                && pairedObjective.CompareTo(compactedObjective) < 0) {
                result = CompactToFixedPoint(
                    request, pairedPlan, cancellationToken, out _);
            }
        }

        return new WarehouseBoundaryOptimizationResult(result, true);
    }

    private static RoutePlan CompactToFixedPoint(
        AutomaticRoutePlanningRequest request,
        RoutePlan initial,
        CancellationToken cancellationToken,
        out bool changed) {
        RoutePlan current = initial;
        changed = false;
        int guard = Math.Max(1, request.Tasks.Count);
        while (guard-- > 0) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryReadSimpleLayouts(request, current, out var layouts))
                break;

            RoutePlan? best = null;
            BoundaryScore bestScore = Score(request, current);
            for (int targetRoute = 0; targetRoute < layouts.Count - 1; targetRoute++) {
                WarehouseUnloadStep unload = current.Routes[targetRoute].Steps
                    .OfType<WarehouseUnloadStep>()
                    .Single();
                RouteWarehouse? warehouse = request.Warehouses.FirstOrDefault(
                    candidate => StringComparer.Ordinal.Equals(
                        candidate.WarehouseId, unload.WarehouseId));
                if (warehouse is null) continue;

                IReadOnlyDictionary<string, int> arriving = unload.Items
                    .GroupBy(item => item.ItemId, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Sum(item => item.Quantity),
                        StringComparer.Ordinal);
                for (int sourceRoute = targetRoute + 1;
                    sourceRoute < layouts.Count;
                    sourceRoute++) {
                    for (int sourcePosition = 0;
                        sourcePosition < layouts[sourceRoute].TaskIndexes.Count;
                        sourcePosition++) {
                        int taskIndex = layouts[sourceRoute].TaskIndexes[sourcePosition];
                        RouteBarterTask task = request.Tasks[taskIndex];
                        if (!StringComparer.Ordinal.Equals(
                                task.IslandId, warehouse.IslandId)
                            || arriving.GetValueOrDefault(task.Item1Id)
                                < task.InputQuantity) {
                            continue;
                        }

                        List<RouteLayout> candidateLayouts = Clone(layouts);
                        candidateLayouts[targetRoute].TaskIndexes.Add(taskIndex);
                        candidateLayouts[sourceRoute].TaskIndexes.RemoveAt(sourcePosition);
                        if (candidateLayouts[sourceRoute].TaskIndexes.Count == 0)
                            candidateLayouts.RemoveAt(sourceRoute);

                        if (!TryBuildState(
                                request,
                                candidateLayouts,
                                current.Status,
                                current.Diagnostics,
                                out RouteSimulationState candidateState)) {
                            continue;
                        }
                        RoutePlan candidate = RoutePlanFactory.FromState(
                            request,
                            candidateState,
                            current.Status,
                            current.Diagnostics);
                        BoundaryScore candidateScore = Score(request, candidate);
                        if (!IsSafeBoundaryImprovement(bestScore, candidateScore))
                            continue;
                        if (!RoutePlanVerifier.Verify(request, candidate).Success)
                            continue;
                        if (best is null
                            || candidateScore.CompareTo(Score(request, best)) < 0) {
                            best = candidate;
                        }
                    }
                }
            }

            if (best is null) break;
            current = best;
            changed = true;
        }
        return current;
    }

    private static bool IsSafeBoundaryImprovement(
        BoundaryScore baseline,
        BoundaryScore candidate) {
        const double epsilon = 0.000001;
        if (candidate.TotalDistance > baseline.TotalDistance + epsilon
            || candidate.RouteCount > baseline.RouteCount
            || candidate.PickupStopCount > baseline.PickupStopCount
            || candidate.MaxPeakLT > baseline.MaxPeakLT) {
            return false;
        }
        return candidate.TotalDistance < baseline.TotalDistance - epsilon
            || candidate.RouteCount < baseline.RouteCount
            || candidate.TotalPickupLT < baseline.TotalPickupLT
            || candidate.TotalPeakLT < baseline.TotalPeakLT;
    }

    private static bool TryReadSimpleLayouts(
        AutomaticRoutePlanningRequest request,
        RoutePlan plan,
        out List<RouteLayout> layouts) {
        var taskIndexes = request.Tasks
            .Select((task, index) => (task.RowId, index))
            .ToDictionary(pair => pair.RowId, pair => pair.index, StringComparer.Ordinal);
        layouts = [];
        foreach (PlannedRoute route in plan.Routes) {
            WarehousePickupStep[] pickups =
                route.Steps.OfType<WarehousePickupStep>().ToArray();
            WarehouseUnloadStep[] unloads =
                route.Steps.OfType<WarehouseUnloadStep>().ToArray();
            if (pickups.Length > 1
                || unloads.Length != 1
                || route.Steps[^1] is not WarehouseUnloadStep
                || pickups.Length == 1 && route.Steps[0] is not WarehousePickupStep) {
                layouts = [];
                return false;
            }

            var indexes = new List<int>();
            foreach (BarterStep barter in route.Steps.OfType<BarterStep>()) {
                if (!taskIndexes.TryGetValue(barter.RowId, out int index)) {
                    layouts = [];
                    return false;
                }
                indexes.Add(index);
            }
            if (indexes.Count == 0) {
                layouts = [];
                return false;
            }
            layouts.Add(new RouteLayout(
                pickups.FirstOrDefault()?.WarehouseId ?? route.StartWarehouseId,
                unloads[0].WarehouseId,
                indexes));
        }
        return true;
    }

    private static bool TryBuildState(
        AutomaticRoutePlanningRequest request,
        IReadOnlyList<RouteLayout> layouts,
        RoutePlanStatus status,
        IReadOnlyList<RouteDiagnostic> diagnostics,
        out RouteSimulationState state) {
        state = RouteSimulationState.CreateInitial(request);
        foreach (RouteLayout layout in layouts) {
            IReadOnlyDictionary<string, int> required =
                RequiredAtRouteStart(request, layout.TaskIndexes);
            IReadOnlyDictionary<string, int> onBoard = state.OnBoard;
            RouteItemQuantity[] pickupItems = required
                .Select(pair => new RouteItemQuantity(
                    pair.Key,
                    Math.Max(0, pair.Value - onBoard.GetValueOrDefault(pair.Key))))
                .Where(item => item.Quantity > 0)
                .OrderBy(item => item.ItemId, StringComparer.Ordinal)
                .ToArray();
            if (pickupItems.Length > 0) {
                RouteTransitionResult pickup = RouteStateTransition.TryPickup(
                    request, state, layout.StartWarehouseId, pickupItems);
                if (!pickup.Success) return false;
                state = pickup.State;
            }

            foreach (int taskIndex in layout.TaskIndexes) {
                RouteTransitionResult barter =
                    RouteStateTransition.TryBarter(request, state, taskIndex);
                if (!barter.Success) return false;
                state = barter.State;
            }

            RouteTransitionResult unload =
                RouteStateTransition.TryUnload(request, state, layout.EndWarehouseId);
            if (!unload.Success) return false;
            state = unload.State;
        }

        ulong fullMask = request.Tasks.Count == 64
            ? ulong.MaxValue
            : (1UL << request.Tasks.Count) - 1;
        if (state.CompletedMask != fullMask
            || state.CurrentRouteSteps.Count != 0
            || state.OnBoard.Count != 0) {
            return false;
        }

        RoutePlan rebuilt = RoutePlanFactory.FromState(
            request, state, status, diagnostics);
        return RoutePlanVerifier.Verify(request, rebuilt).Success;
    }

    private static IReadOnlyDictionary<string, int> RequiredAtRouteStart(
        AutomaticRoutePlanningRequest request,
        IReadOnlyList<int> taskIndexes) {
        var balance = new Dictionary<string, int>(StringComparer.Ordinal);
        var required = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (int taskIndex in taskIndexes) {
            RouteBarterTask task = request.Tasks[taskIndex];
            int available = balance.GetValueOrDefault(task.Item1Id);
            if (available < task.InputQuantity) {
                int deficit = task.InputQuantity - available;
                required[task.Item1Id] =
                    checked(required.GetValueOrDefault(task.Item1Id) + deficit);
                available += deficit;
            }
            SetQuantity(balance, task.Item1Id, available - task.InputQuantity);
            SetQuantity(
                balance,
                task.Item2Id,
                checked(balance.GetValueOrDefault(task.Item2Id)
                    + task.OutputQuantity));
        }
        return required;
    }

    private static BoundaryScore Score(
        AutomaticRoutePlanningRequest request,
        RoutePlan plan) {
        RoutePlanObjective objective = plan.Objective
            ?? throw new InvalidOperationException("Route plan has no objective.");
        long pickupLT = plan.Routes
            .SelectMany(route => route.Steps)
            .OfType<WarehousePickupStep>()
            .SelectMany(step => step.Items)
            .Sum(item => checked(
                (long)request.Items[item.ItemId].UnitWeight * item.Quantity));
        long totalPeakLT = plan.Routes.Sum(route => (long)route.PeakLT);
        return new BoundaryScore(
            objective.TotalDistance,
            objective.RouteCount,
            objective.PickupStopCount,
            objective.MaxPeakLT,
            pickupLT,
            totalPeakLT,
            objective.StableTieBreak);
    }

    private static List<RouteLayout> Clone(
        IReadOnlyList<RouteLayout> layouts) => layouts
        .Select(layout => new RouteLayout(
            layout.StartWarehouseId,
            layout.EndWarehouseId,
            [.. layout.TaskIndexes]))
        .ToList();

    private static void SetQuantity(
        Dictionary<string, int> inventory,
        string itemId,
        int quantity) {
        if (quantity == 0) inventory.Remove(itemId);
        else inventory[itemId] = quantity;
    }

    private sealed record RouteLayout(
        string StartWarehouseId,
        string EndWarehouseId,
        List<int> TaskIndexes);

    private readonly record struct BoundaryScore(
        double TotalDistance,
        int RouteCount,
        int PickupStopCount,
        int MaxPeakLT,
        long TotalPickupLT,
        long TotalPeakLT,
        string StableTieBreak) : IComparable<BoundaryScore> {
        public int CompareTo(BoundaryScore other) {
            int result = TotalDistance.CompareTo(other.TotalDistance);
            if (result != 0) return result;
            result = RouteCount.CompareTo(other.RouteCount);
            if (result != 0) return result;
            result = TotalPickupLT.CompareTo(other.TotalPickupLT);
            if (result != 0) return result;
            result = PickupStopCount.CompareTo(other.PickupStopCount);
            if (result != 0) return result;
            result = MaxPeakLT.CompareTo(other.MaxPeakLT);
            if (result != 0) return result;
            result = TotalPeakLT.CompareTo(other.TotalPeakLT);
            if (result != 0) return result;
            return StringComparer.Ordinal.Compare(StableTieBreak, other.StableTieBreak);
        }
    }
}

public sealed record WarehouseBoundaryOptimizationResult(
    RoutePlan Plan,
    bool Changed);
