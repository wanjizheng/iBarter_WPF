namespace iBarter.Routing;

/// <summary>
/// Structured result of replaying a <see cref="PlannedRoute"/> through
/// the simulator. Carries the rebuilt route on success, or a precise
/// failure code, step index, step kind, and cargo context on failure so
/// callers can diagnose why normalization silently dropped back to the
/// original plan in prior rounds.
/// </summary>
public sealed record RouteReplayResult(
    bool Success,
    PlannedRoute? Route,
    int RouteNumber,
    string? FailureCode,
    int? FailureStepIndex,
    string? FailureStepKind,
    string? FailureDetail,
    IReadOnlyList<string>? OnBoardAtFailure,
    string? FailedWarehouse,
    string? RequestedItemId,
    int? WarehouseStockQty,
    int? TotalLT,
    int? ExtraLT);

public sealed record RoutePlanReplayResult(
    bool Success,
    RoutePlan? Plan,
    RouteReplayResult? Failure);

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
/// <para>Audit round 3 (replay failure transparency): round-trip cargo
/// drops in the normalizer previously failed at replay and the helper
/// returned <c>null</c>, hiding the actual transition failure. The
/// helper now returns a structured <see cref="RouteReplayResult"/> with
/// the step index, simulator diagnostic code, on-board inventory at the
/// failure, and the warehouse stock that caused stock-related failures.</para>
/// </summary>
public static class RouteReplay {
    /// <summary>
    /// Replays <paramref name="route"/> through the simulator starting
    /// from a fresh <see cref="RouteSimulationState.CreateInitial"/>.
    /// Returns a structured result with the rebuilt route on success
    /// or a precise failure description on failure.
    /// </summary>
    public static RouteReplayResult ReplayRoute(AutomaticRoutePlanningRequest request, PlannedRoute route) {
        if (request is null || route is null) {
            return new RouteReplayResult(false, null,
                route?.Number ?? 0,
                "replay-bad-input", null, null, "request or route null", null,
                null, null, null, null, null);
        }
        if (route.Steps.Count == 0) {
            return new RouteReplayResult(false, null,
                route.Number,
                "replay-empty-route", null, null, "route has no steps", null,
                null, null, null, null, null);
        }
        var core = ReplayRouteCore(request, route, RouteSimulationState.CreateInitial(request));
        return core.Result;
    }

    /// <summary>
    /// Replays every route against one continuous simulation state. This is
    /// required for normalization because later routes may depend on warehouse
    /// inventory and completed-task state produced by earlier routes.
    /// </summary>
    public static RoutePlanReplayResult ReplayPlan(
        AutomaticRoutePlanningRequest request,
        RoutePlan plan) {
        if (request is null || plan is null) {
            return new RoutePlanReplayResult(false, null,
                new RouteReplayResult(false, null, 0,
                    "replay-bad-input", null, null, "request or plan null", null,
                    null, null, null, null, null));
        }

        var state = RouteSimulationState.CreateInitial(request);
        foreach (var route in plan.Routes) {
            var core = ReplayRouteCore(request, route, state);
            if (!core.Result.Success)
                return new RoutePlanReplayResult(false, null, core.Result);
            state = core.State;
        }

        var rebuilt = RoutePlanFactory.FromState(
            request, state, plan.Status, plan.Diagnostics);
        return new RoutePlanReplayResult(true, rebuilt, null);
    }

    public static string FormatFailureDetail(RouteReplayResult? failure) {
        if (failure is null) return "replay returned no failure context";
        return $"route={failure.RouteNumber} step={failure.FailureStepIndex} " +
            $"kind={failure.FailureStepKind} code={failure.FailureCode} " +
            $"cargo=[{string.Join(",", failure.OnBoardAtFailure ?? [])}] " +
            $"warehouse={failure.FailedWarehouse} stock={failure.WarehouseStockQty} " +
            $"maxLT={failure.TotalLT} extraLT={failure.ExtraLT} " +
            $"item={failure.RequestedItemId} detail={failure.FailureDetail}";
    }

    private static ReplayRouteCoreResult ReplayRouteCore(
        AutomaticRoutePlanningRequest request,
        PlannedRoute route,
        RouteSimulationState initialState) {
        if (route.Steps.Count == 0) {
            var empty = new RouteReplayResult(false, null, route.Number,
                "replay-empty-route", null, null, "route has no steps", null,
                null, null, null, request.TotalLT, request.ExtraLT);
            return new ReplayRouteCoreResult(empty, initialState);
        }
        var state = initialState;
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
            if (!result.Success || result.Step is null) {
                return new ReplayRouteCoreResult(
                    FailureResult(result, state, route.Number, stepIndex, expected, request),
                    state);
            }
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
        var rebuilt = new PlannedRoute(
            route.Number, route.StartWarehouseId, route.EndWarehouseId,
            newSteps, route.Distance, initialLT, currentLT, peakLT);
        return new ReplayRouteCoreResult(
            new RouteReplayResult(true, rebuilt, route.Number,
                null, null, null, null, null,
                null, null, null, null, null),
            state);
    }

    private static RouteReplayResult FailureResult(
        RouteTransitionResult result,
        RouteSimulationState state,
        int routeNumber,
        int stepIndex,
        RouteStep expected,
        AutomaticRoutePlanningRequest request) {
        // Extract warehouse stock for the failed warehouse, if applicable.
        string? failedWarehouse = expected switch {
            WarehousePickupStep p => p.WarehouseId,
            WarehouseUnloadStep u => u.WarehouseId,
            _ => null,
        };
        string? requestedItemId = expected switch {
            WarehousePickupStep p => FindFailedPickupItem(request, state, p),
            WarehouseUnloadStep u => FindFailedUnloadItem(request, state, u),
            BarterStep b => b.Consumed.ItemId,
            _ => null,
        };
        int? stockQty = null;
        if (failedWarehouse is not null && requestedItemId is not null
            && state.WarehouseInventory.TryGetValue(failedWarehouse, out var stock)
            && stock.TryGetValue(requestedItemId, out var sq)) {
            stockQty = sq;
        }
        var onBoard = state.OnBoard
            .Where(x => x.Value > 0)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => $"{x.Key}={x.Value}")
            .ToArray();
        string parameters = expected switch {
            WarehousePickupStep pickup =>
                $"pickup={pickup.WarehouseId}[{FormatItems(pickup.Items)}]",
            WarehouseUnloadStep unload =>
                $"unload={unload.WarehouseId}[{FormatItems(unload.Items)}]",
            BarterStep barter =>
                $"barter={barter.RowId} consume={barter.Consumed.ItemId}x{barter.Consumed.Quantity} " +
                $"produce={barter.Produced.ItemId}x{barter.Produced.Quantity}",
            _ => expected.GetType().Name,
        };
        string transitionDetail = result.Diagnostic?.Detail ?? "";
        return new RouteReplayResult(false, null,
            routeNumber,
            result.Diagnostic?.Code ?? "replay-failure",
            stepIndex,
            expected.GetType().Name,
            $"{parameters} transitionDetail={transitionDetail}",
            onBoard,
            failedWarehouse,
            requestedItemId,
            stockQty,
            request.TotalLT,
            request.ExtraLT);
    }

    private static string? FindFailedPickupItem(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        WarehousePickupStep pickup) {
        var invalid = pickup.Items.FirstOrDefault(item =>
            item.Quantity <= 0 || !request.Items.ContainsKey(item.ItemId));
        if (invalid is not null) return invalid.ItemId;
        if (!state.WarehouseInventory.TryGetValue(pickup.WarehouseId, out var stock))
            return pickup.Items.FirstOrDefault()?.ItemId;
        return pickup.Items
            .GroupBy(item => item.ItemId, StringComparer.Ordinal)
            .Select(group => new RouteItemQuantity(group.Key, group.Sum(item => item.Quantity)))
            .FirstOrDefault(item => stock.GetValueOrDefault(item.ItemId) < item.Quantity)
            ?.ItemId
            ?? pickup.Items.FirstOrDefault()?.ItemId;
    }

    private static string? FindFailedUnloadItem(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        WarehouseUnloadStep unload) => unload.Items
        .GroupBy(item => item.ItemId, StringComparer.Ordinal)
        .Select(group => new RouteItemQuantity(group.Key, group.Sum(item => item.Quantity)))
        .FirstOrDefault(item => item.Quantity <= 0
            || !request.Items.ContainsKey(item.ItemId)
            || state.OnBoard.GetValueOrDefault(item.ItemId) < item.Quantity)
        ?.ItemId
        ?? unload.Items.FirstOrDefault()?.ItemId;

    private static string FormatItems(IEnumerable<RouteItemQuantity> items) =>
        string.Join(",", items.Select(item => $"{item.ItemId}x{item.Quantity}"));

    private sealed record ReplayRouteCoreResult(
        RouteReplayResult Result,
        RouteSimulationState State);

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
