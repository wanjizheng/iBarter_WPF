namespace iBarter.Routing;

/// <summary>
/// Replays a finalized <see cref="PlannedRoute"/> step-by-step through
/// the same <see cref="RouteStateTransition"/> simulator the verifier uses,
/// producing a fresh <see cref="PlannedRoute"/> whose every
/// <see cref="RouteStep.Load"/> matches the live on-board state.
///
/// <para>Audit round 2 (1): the v1 cargo normalizer rewrote Items on
/// pickup/unload steps but kept the original <c>Load.CargoLT</c>/
/// <c>TotalWithExtraLT</c>/<c>PeakTotalLT</c> from the plan it was
/// given. After pruning the 五彩线团 round-trip the items disappeared
/// from disk but every step's LT stayed pinned to the old higher value,
/// so PeakLT no longer reflected reality. This helper closes that gap:
/// after any items change, the entire route is replayed through the
/// state simulator and a new <see cref="PlannedRoute"/> is built with
/// authoritative Load snapshots, InitialLT, CurrentLT and PeakLT.</para>
///
/// <para>If a step is invalid (e.g. its Items would push the load past
/// <c>request.TotalLT</c>), the helper returns <c>null</c> so the caller
/// can reject the modification rather than publishing a half-truth.</para>
/// </summary>
public static class RouteReplay {
    /// <summary>
    /// Replays <paramref name="route"/> through the simulator starting
    /// from a fresh <see cref="RouteSimulationState.CreateInitial"/>.
    /// Returns a new route with recomputed LT, or null when replay fails.
    /// </summary>
    public static PlannedRoute? ReplayRoute(AutomaticRoutePlanningRequest request, PlannedRoute route) {
        if (request is null || route is null) return null;
        var state = RouteSimulationState.CreateInitial(request);
        var newSteps = new List<RouteStep>(route.Steps.Count);
        for (int stepIndex = 0; stepIndex < route.Steps.Count; stepIndex++) {
            var expected = route.Steps[stepIndex];
            RouteTransitionResult result = expected switch {
                WarehousePickupStep pickup => RouteStateTransition.TryPickup(
                    request, state, pickup.WarehouseId, pickup.Items),
                BarterStep barter => ReplayBarter(request, state, barter.RowId,
                    barter.Consumed, barter.Produced),
                WarehouseUnloadStep unload => RouteStateTransition.TryUnload(
                    request, state, unload.WarehouseId, unload.Items,
                    finishRoute: !route.Steps.Skip(stepIndex + 1).OfType<WarehouseUnloadStep>().Any()),
                _ => new RouteTransitionResult(false, state, null,
                    new RouteDiagnostic("replay-unknown-step", Detail: expected.GetType().Name)),
            };
            if (!result.Success || result.Step is null) return null;
            newSteps.Add(result.Step);
            state = result.State;
        }
        int initialLT = newSteps.Count > 0
            ? newSteps[0].Load.TotalWithExtraLT
            : request.ExtraLT;
        int currentLT = newSteps.Count > 0
            ? newSteps[^1].Load.TotalWithExtraLT
            : request.ExtraLT;
        // Audit round 2: the simulator resets CurrentRoutePeakLT to 0
        // when a route finishes (TryUnload's finishRoute branch) so the
        // NEXT route can start fresh. We must capture the peak BEFORE
        // the reset, which lives on the last step's Load.
        int peakLT = newSteps.Count > 0
            ? newSteps.Max(s => s.Load.PeakTotalLT)
            : request.ExtraLT;
        return new PlannedRoute(
            route.Number, route.StartWarehouseId, route.EndWarehouseId,
            newSteps, route.Distance, initialLT, currentLT, peakLT);
    }

    /// <summary>
    /// Replays a BarterStep. If the original task is no longer in
    /// <c>request.Tasks</c> (e.g. it was removed by CK reconciliation)
    /// we re-derive the step's on-board delta directly from the recorded
    /// Consumed/Produced so the route still replays end-to-end. The new
    /// Load snapshot is recomputed from the live on-board inventory.
    /// </summary>
    private static RouteTransitionResult ReplayBarter(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        string rowId,
        RouteItemQuantity consumed,
        RouteItemQuantity produced) {
        int index = -1;
        for (int i = 0; i < request.Tasks.Count; i++)
            if (StringComparer.Ordinal.Equals(request.Tasks[i].RowId, rowId)) { index = i; break; }
        if (index >= 0)
            return RouteStateTransition.TryBarter(request, state, index);

        // Task missing from request — simulate the barter on OnBoard directly.
        var onboard = state.OnBoard.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        int available = onboard.GetValueOrDefault(consumed.ItemId);
        if (available < consumed.Quantity) {
            return new RouteTransitionResult(false, state, null,
                new RouteDiagnostic("replay-barter-shortage",
                    rowId, consumed.ItemId, Detail: consumed.Quantity.ToString()));
        }
        int newAvail = available - consumed.Quantity;
        if (newAvail == 0) onboard.Remove(consumed.ItemId);
        else onboard[consumed.ItemId] = newAvail;
        onboard[produced.ItemId] = onboard.GetValueOrDefault(produced.ItemId) + produced.Quantity;

        long cargoLT = 0;
        foreach (var pair in onboard) {
            if (!request.Items.TryGetValue(pair.Key, out var item)) continue;
            cargoLT += (long)item.UnitWeight * pair.Value;
        }
        int total = checked(request.ExtraLT + (int)cargoLT);
        if (total > request.TotalLT) {
            return new RouteTransitionResult(false, state, null,
                new RouteDiagnostic("replay-barter-overweight",
                    rowId, "", Detail: total.ToString()));
        }
        int peak = Math.Max(state.CurrentRoutePeakLT, total);
        var load = new RouteLoadSnapshot((int)cargoLT, total, peak);
        var step = new BarterStep(rowId, "", consumed, produced, load);
        var next = new RouteSimulationState(
            "", state.CurrentRouteNumber, state.CompletedMask,
            state.VisitedWarehouseIds, onboard, state.WarehouseInventory,
            (int)cargoLT, peak,
            state.CurrentRouteSteps.Append(step), state.FinishedRoutes,
            state.TotalDistance, state.PickupStopCount);
        return new RouteTransitionResult(true, next, step, null);
    }
}