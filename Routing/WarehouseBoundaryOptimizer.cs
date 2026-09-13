namespace iBarter.Routing;

/// <summary>
/// Optimizes cargo across warehouse-island boundaries: a barter can move onto
/// an earlier producing route, and a weighted terminal reward can be deposited
/// before the current route continues with unrelated work.
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
        if (plan.Routes.Count == 0)
            return new WarehouseBoundaryOptimizationResult(plan, false);

        RoutePlan result = plan;
        bool changed = false;
        if (plan.Routes.Count >= 2 && TryReadSimpleLayouts(request, plan, out _)) {
            RoutePlan compacted = CompactToFixedPoint(
                request, plan, cancellationToken, out bool compactedChanged);
            if (compactedChanged) {
                result = compacted;
                changed = true;
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
            }
        }

        RoutePlan reordered = MoveTerminalBartersToEarlierWarehousePickups(
            request, result, cancellationToken, out bool reorderedChanged);
        RoutePlan terminalUnloaded = UnloadTerminalWarehouseRewards(
            request, reordered, cancellationToken, out bool terminalUnloadChanged);
        RoutePlan pairImproved = terminalUnloaded;
        bool pairChanged = false;
        if (reorderedChanged
            || terminalUnloadChanged
            || HasTerminalWarehouseReleaseCandidate(request, result)) {
            pairImproved = ImprovePairsAfterCapacityRelease(
                request,
                terminalUnloaded,
                cancellationToken,
                out pairChanged);
        }
        return new WarehouseBoundaryOptimizationResult(
            pairImproved,
            changed || reorderedChanged || terminalUnloadChanged || pairChanged);
    }

    private static bool HasTerminalWarehouseReleaseCandidate(
        AutomaticRoutePlanningRequest request,
        RoutePlan plan) {
        if (plan.Routes.Count < 2)
            return false;
        var consumedItemIds = request.Tasks
            .Select(task => task.Item1Id)
            .ToHashSet(StringComparer.Ordinal);
        var uniqueWarehouseIslandIds = request.Warehouses
            .GroupBy(warehouse => warehouse.IslandId, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        return request.Tasks.Any(task =>
            !consumedItemIds.Contains(task.Item2Id)
            && request.Items.TryGetValue(task.Item2Id, out RouteItem? item)
            && item.UnitWeight > 0
            && uniqueWarehouseIslandIds.Contains(task.IslandId));
    }

    /// <summary>
    /// Gives the pair rebuilder one more pass after early terminal unloads have
    /// released capacity. Pair rebuilding itself uses the same early-unload
    /// rule, so it can actually place other-route work into that freed space
    /// instead of modeling the terminal reward as carried to route end.
    /// </summary>
    private static RoutePlan ImprovePairsAfterCapacityRelease(
        AutomaticRoutePlanningRequest request,
        RoutePlan plan,
        CancellationToken cancellationToken,
        out bool changed) {
        changed = false;
        if (plan.Routes.Count < 2
            || request.Limits.MaxLocalMoves <= 0
            || !TryReplayPlanState(request, plan, out RouteSimulationState state)) {
            return plan;
        }

        RouteSimulationState rebuilt = RoutePairRebuilder.Improve(
            request, state, cancellationToken);
        RoutePlan rebuiltPlan = RoutePlanFactory.FromState(
            request, rebuilt, plan.Status, plan.Diagnostics);
        if (rebuiltPlan.Objective is not { } rebuiltObjective
            || plan.Objective is not { } originalObjective
            || rebuiltObjective.CompareTo(originalObjective) >= 0) {
            return plan;
        }

        RoutePlan reordered = MoveTerminalBartersToEarlierWarehousePickups(
            request, rebuiltPlan, cancellationToken, out _);
        RoutePlan candidate = UnloadTerminalWarehouseRewards(
            request, reordered, cancellationToken, out _);
        if (!RoutePlanVerifier.Verify(request, candidate).Success
            || candidate.Objective is not { } candidateObjective
            || candidateObjective.CompareTo(originalObjective) >= 0) {
            return plan;
        }

        changed = true;
        return candidate;
    }

    /// <summary>
    /// If a route has already reached the warehouse island with the required
    /// input on board, perform a later terminal barter during that first visit
    /// instead of carrying the input through unrelated barters and returning.
    /// The candidate is accepted only after it can also deposit the terminal
    /// reward immediately, replay the complete plan, and improve cargo use
    /// without increasing distance, route count, pickup stops, or peak LT.
    /// </summary>
    private static RoutePlan MoveTerminalBartersToEarlierWarehousePickups(
        AutomaticRoutePlanningRequest request,
        RoutePlan plan,
        CancellationToken cancellationToken,
        out bool changed) {
        changed = false;
        RoutePlan current = plan;
        var consumedItemIds = request.Tasks
            .Select(task => task.Item1Id)
            .ToHashSet(StringComparer.Ordinal);
        var warehousesByIsland = request.Warehouses
            .GroupBy(warehouse => warehouse.IslandId, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(
                group => group.Key,
                group => group.Single(),
                StringComparer.Ordinal);

        int guard = Math.Max(1, request.Tasks.Count);
        while (guard-- > 0) {
            cancellationToken.ThrowIfCancellationRequested();
            RoutePlan? best = null;
            BoundaryScore baselineScore = Score(request, current);

            for (int routeIndex = 0;
                routeIndex < current.Routes.Count;
                routeIndex++) {
                PlannedRoute route = current.Routes[routeIndex];
                for (int barterIndex = 0;
                    barterIndex < route.Steps.Count;
                    barterIndex++) {
                    if (route.Steps[barterIndex] is not BarterStep barter
                        || consumedItemIds.Contains(barter.Produced.ItemId)
                        || !request.Items.TryGetValue(
                            barter.Produced.ItemId, out RouteItem? producedItem)
                        || producedItem.UnitWeight <= 0
                        || !warehousesByIsland.TryGetValue(
                            barter.IslandId, out RouteWarehouse? warehouse)) {
                        continue;
                    }

                    int pickupIndex = route.Steps
                        .Take(barterIndex)
                        .Select((step, index) => (step, index))
                        .Where(pair => pair.step is WarehousePickupStep pickup
                            && StringComparer.Ordinal.Equals(
                                pickup.WarehouseId, warehouse.WarehouseId))
                        .Select(pair => pair.index)
                        .DefaultIfEmpty(-1)
                        .First();
                    if (pickupIndex < 0 || pickupIndex >= barterIndex)
                        continue;

                    foreach (int insertionIndex in new[] {
                                 pickupIndex,
                                 pickupIndex + 1,
                             }.Distinct()) {
                        if (insertionIndex >= barterIndex)
                            continue;
                        var reorderedSteps = route.Steps.ToList();
                        reorderedSteps.RemoveAt(barterIndex);
                        reorderedSteps.Insert(insertionIndex, barter);
                        var reorderedRoute = new PlannedRoute(
                            route.Number,
                            route.StartWarehouseId,
                            route.EndWarehouseId,
                            reorderedSteps,
                            route.Distance,
                            route.InitialLT,
                            route.CurrentLT,
                            route.PeakLT);
                        PlannedRoute[] reorderedRoutes = current.Routes.ToArray();
                        reorderedRoutes[routeIndex] = reorderedRoute;
                        var reorderedPlan = new RoutePlan(
                            current.Status,
                            reorderedRoutes,
                            current.Objective,
                            current.Diagnostics,
                            current.InputFingerprint);

                        RoutePlan candidate = UnloadTerminalWarehouseRewards(
                            request,
                            reorderedPlan,
                            cancellationToken,
                            out bool unloaded);
                        if (!unloaded)
                            continue;
                        BoundaryScore candidateScore = Score(request, candidate);
                        if (!IsSafeBoundaryImprovement(
                                baselineScore, candidateScore)
                            || !RoutePlanVerifier.Verify(request, candidate).Success) {
                            continue;
                        }
                        if (best is null
                            || candidateScore.CompareTo(Score(request, best)) < 0) {
                            best = candidate;
                        }
                    }
                }
            }

            if (best is null)
                break;
            current = best;
            changed = true;
        }
        return current;
    }

    /// <summary>
    /// Deposits a weighted terminal reward immediately when its barter happens
    /// on a warehouse island and the route still has work to do. This keeps the
    /// unrelated remainder of the route from carrying a final Level-7-style
    /// reward away from, and then back to, the same warehouse.
    /// </summary>
    private static RoutePlan UnloadTerminalWarehouseRewards(
        AutomaticRoutePlanningRequest request,
        RoutePlan plan,
        CancellationToken cancellationToken,
        out bool changed) {
        changed = false;
        var taskIndexes = request.Tasks
            .Select((task, index) => (task.RowId, index))
            .ToDictionary(pair => pair.RowId, pair => pair.index, StringComparer.Ordinal);

        RouteSimulationState state = RouteSimulationState.CreateInitial(request);
        foreach (PlannedRoute route in plan.Routes) {
            for (int stepIndex = 0; stepIndex < route.Steps.Count; stepIndex++) {
                cancellationToken.ThrowIfCancellationRequested();
                RouteStep step = route.Steps[stepIndex];
                RouteTransitionResult transition;
                switch (step) {
                    case WarehousePickupStep pickup:
                        transition = RouteStateTransition.TryPickup(
                            request, state, pickup.WarehouseId, pickup.Items);
                        break;
                    case BarterStep barter:
                        if (!taskIndexes.TryGetValue(barter.RowId, out int taskIndex)) {
                            changed = false;
                            return plan;
                        }
                        transition = RouteStateTransition.TryBarter(
                            request, state, taskIndex);
                        break;
                    case WarehouseUnloadStep unload:
                        bool hasLaterUnload = route.Steps
                            .Skip(stepIndex + 1)
                            .OfType<WarehouseUnloadStep>()
                            .Any();
                        transition = hasLaterUnload
                            ? RouteStateTransition.TryUnload(
                                request,
                                state,
                                unload.WarehouseId,
                                unload.Items,
                                finishRoute: false)
                            : RouteStateTransition.TryUnload(
                                request, state, unload.WarehouseId);
                        break;
                    default:
                        changed = false;
                        return plan;
                }

                if (!transition.Success) {
                    changed = false;
                    return plan;
                }
                state = transition.State;

                if (step is not BarterStep completed
                    || !route.Steps.Skip(stepIndex + 1).OfType<BarterStep>().Any()
                    || route.Steps.ElementAtOrDefault(stepIndex + 1)
                        is WarehouseUnloadStep nextUnload
                        && nextUnload.Items.Any(item =>
                            StringComparer.Ordinal.Equals(
                                item.ItemId, completed.Produced.ItemId))) {
                    continue;
                }
                if (!taskIndexes.TryGetValue(
                        completed.RowId, out int completedTaskIndex))
                    continue;

                state = UnloadTerminalRewardIfPossible(
                    request,
                    state,
                    completedTaskIndex,
                    hasRemainingWork: true,
                    out bool unloaded);
                changed |= unloaded;
            }
        }

        if (!changed)
            return plan;
        ulong fullMask = request.Tasks.Count == 64
            ? ulong.MaxValue
            : (1UL << request.Tasks.Count) - 1;
        if (state.CompletedMask != fullMask
            || state.CurrentRouteSteps.Count != 0
            || state.OnBoard.Count != 0) {
            changed = false;
            return plan;
        }

        RoutePlan candidate = RoutePlanFactory.FromState(
            request, state, plan.Status, plan.Diagnostics);
        if (!RoutePlanVerifier.Verify(request, candidate).Success) {
            changed = false;
            return plan;
        }
        return candidate;
    }

    internal static RouteSimulationState UnloadTerminalRewardIfPossible(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        int taskIndex,
        bool hasRemainingWork,
        out bool changed) {
        changed = false;
        if (!hasRemainingWork
            || taskIndex < 0
            || taskIndex >= request.Tasks.Count) {
            return state;
        }

        RouteBarterTask task = request.Tasks[taskIndex];
        if (request.Tasks.Any(candidate => StringComparer.Ordinal.Equals(
                candidate.Item1Id, task.Item2Id))
            || !request.Items.TryGetValue(
                task.Item2Id, out RouteItem? producedItem)
            || producedItem.UnitWeight <= 0) {
            return state;
        }

        RouteWarehouse[] matchingWarehouses = request.Warehouses
            .Where(warehouse => StringComparer.Ordinal.Equals(
                warehouse.IslandId, task.IslandId))
            .ToArray();
        if (matchingWarehouses.Length != 1)
            return state;

        int available = state.OnBoard.GetValueOrDefault(task.Item2Id);
        int unloadQuantity = Math.Min(available, task.OutputQuantity);
        if (unloadQuantity <= 0)
            return state;

        RouteTransitionResult partialUnload =
            RouteStateTransition.TryUnload(
                request,
                state,
                matchingWarehouses[0].WarehouseId,
                [new RouteItemQuantity(task.Item2Id, unloadQuantity)],
                finishRoute: false);
        if (!partialUnload.Success)
            return state;

        changed = true;
        return partialUnload.State;
    }

    private static bool TryReplayPlanState(
        AutomaticRoutePlanningRequest request,
        RoutePlan plan,
        out RouteSimulationState state) {
        var taskIndexes = request.Tasks
            .Select((task, index) => (task.RowId, index))
            .ToDictionary(pair => pair.RowId, pair => pair.index, StringComparer.Ordinal);
        state = RouteSimulationState.CreateInitial(request);
        foreach (PlannedRoute route in plan.Routes) {
            for (int stepIndex = 0; stepIndex < route.Steps.Count; stepIndex++) {
                RouteStep step = route.Steps[stepIndex];
                RouteTransitionResult transition;
                switch (step) {
                    case WarehousePickupStep pickup:
                        transition = RouteStateTransition.TryPickup(
                            request, state, pickup.WarehouseId, pickup.Items);
                        break;
                    case BarterStep barter
                        when taskIndexes.TryGetValue(
                            barter.RowId, out int taskIndex):
                        transition = RouteStateTransition.TryBarter(
                            request, state, taskIndex);
                        break;
                    case WarehouseUnloadStep unload:
                        bool finishRoute = !route.Steps
                            .Skip(stepIndex + 1)
                            .OfType<WarehouseUnloadStep>()
                            .Any();
                        transition = RouteStateTransition.TryUnload(
                            request,
                            state,
                            unload.WarehouseId,
                            unload.Items,
                            finishRoute);
                        break;
                    default:
                        return false;
                }
                if (!transition.Success)
                    return false;
                state = transition.State;
            }
        }
        return true;
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
            || candidate.MaxPeakLT > baseline.MaxPeakLT
            || candidate.TotalPostPickupCargoLT
                > baseline.TotalPostPickupCargoLT) {
            return false;
        }
        return candidate.TotalDistance < baseline.TotalDistance - epsilon
            || candidate.RouteCount < baseline.RouteCount
            || candidate.TotalPickupLT < baseline.TotalPickupLT
            || candidate.TotalPostPickupCargoLT
                < baseline.TotalPostPickupCargoLT
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
        long totalPostPickupCargoLT = plan.Routes
            .SelectMany(route => route.Steps)
            .OfType<WarehousePickupStep>()
            .Sum(step => (long)step.Load.CargoLT);
        long totalPeakLT = plan.Routes.Sum(route => (long)route.PeakLT);
        return new BoundaryScore(
            objective.TotalDistance,
            objective.RouteCount,
            objective.PickupStopCount,
            objective.MaxPeakLT,
            pickupLT,
            totalPostPickupCargoLT,
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
        long TotalPostPickupCargoLT,
        long TotalPeakLT,
        string StableTieBreak) : IComparable<BoundaryScore> {
        public int CompareTo(BoundaryScore other) {
            int result = TotalDistance.CompareTo(other.TotalDistance);
            if (result != 0) return result;
            result = RouteCount.CompareTo(other.RouteCount);
            if (result != 0) return result;
            result = TotalPickupLT.CompareTo(other.TotalPickupLT);
            if (result != 0) return result;
            result = TotalPostPickupCargoLT.CompareTo(
                other.TotalPostPickupCargoLT);
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
