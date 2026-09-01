namespace iBarter.Routing;

public enum RoutePlanPublicationSource {
    FreshGeneration,
    CompletionProgress,
    PersistedRestore,
    ProgressRestore,
}

public sealed record RoutePlanPublicationFailure(
    string Code,
    string Detail,
    CargoNormalizationDiagnostic? NormalizationFailure = null,
    RouteDiagnostic? VerificationFailure = null);

public sealed record RoutePlanPublicationResult(
    bool Success,
    RoutePlan? Plan,
    bool Changed,
    IReadOnlyList<CargoNormalizationChange> Changes,
    RoutePlanPublicationFailure? Failure) {
    /// <summary>
    /// Immutable full-plan baseline that must remain in memory and on disk.
    /// Completion/progress publication verifies a projected remaining plan,
    /// but that projection must never replace this baseline or an unchecked
    /// Planner row cannot be restored later.
    /// </summary>
    public RoutePlan? RetainedPlan { get; init; }
}

/// <summary>
/// The sole contract for any route plan that may reach the UI or disk:
/// normalize, replay the complete plan, run the complete verifier, then check
/// the redundant-cargo invariant.
/// </summary>
public static class RoutePlanPublication {
    public static RoutePlanPublicationResult PreparePlanForPublication(
        AutomaticRoutePlanningRequest request,
        RoutePlan candidate,
        RoutePlanPublicationSource source,
        IReadOnlySet<string>? completedBarterRowIds = null) {
        if (request is null || candidate is null) {
            return Failed("publication-bad-input", "request or candidate is null");
        }
        if (candidate.Status is not (RoutePlanStatus.Optimal
                or RoutePlanStatus.BestKnownWithinLimit)) {
            return Failed("publication-status", candidate.Status.ToString());
        }

        AutomaticRoutePlanningRequest effectiveRequest = request;
        RoutePlan preparedCandidate;
        if (source is RoutePlanPublicationSource.CompletionProgress
            or RoutePlanPublicationSource.ProgressRestore) {
            var projection = ProjectRemainingPlan(
                request, candidate, completedBarterRowIds ?? EmptyCompleted);
            effectiveRequest = projection.Request;
            preparedCandidate = projection.Plan;
        }
        else {
            preparedCandidate = candidate;
        }

        if (!StringComparer.Ordinal.Equals(
                preparedCandidate.InputFingerprint,
                RoutePlanFingerprint.Compute(effectiveRequest))) {
            return Failed("publication-fingerprint", source.ToString());
        }

        var normalization = RouteCargoNormalizer.Normalize(effectiveRequest, preparedCandidate);
        if (!normalization.Success || normalization.Plan is null) {
            return new RoutePlanPublicationResult(
                false,
                null,
                normalization.Changed,
                normalization.Changes,
                new RoutePlanPublicationFailure(
                    normalization.Failure?.Code ?? "normalization-failed",
                    normalization.Failure?.Detail ?? "normalization returned no plan",
                    normalization.Failure));
        }

        // Replay even when normalization was a no-op. This canonicalizes the
        // objective, distance, LT snapshots and route summaries before the
        // independent verifier compares every field.
        var replay = RouteReplay.ReplayPlan(effectiveRequest, normalization.Plan);
        if (!replay.Success || replay.Plan is null) {
            return new RoutePlanPublicationResult(
                false,
                null,
                normalization.Changed,
                normalization.Changes,
                new RoutePlanPublicationFailure(
                    replay.Failure?.FailureCode ?? "replay-failed",
                    RouteReplay.FormatFailureDetail(replay.Failure)));
        }

        RoutePlan publicationCandidate = replay.Plan;
        bool boundaryChanged = false;
        if (source == RoutePlanPublicationSource.FreshGeneration) {
            WarehouseBoundaryOptimizationResult boundary =
                WarehouseBoundaryOptimizer.Improve(
                    effectiveRequest,
                    publicationCandidate,
                    CancellationToken.None);
            publicationCandidate = boundary.Plan;
            boundaryChanged = boundary.Changed;
        }

        var verification = RoutePlanVerifier.Verify(effectiveRequest, publicationCandidate);
        if (!verification.Success || verification.VerifiedPlan is null) {
            return new RoutePlanPublicationResult(
                false,
                null,
                normalization.Changed || boundaryChanged,
                normalization.Changes,
                new RoutePlanPublicationFailure(
                    verification.Diagnostic?.Code ?? "verification-failed",
                    verification.Diagnostic?.Detail ?? "full verifier returned no plan",
                    VerificationFailure: verification.Diagnostic));
        }

        if (RoutePlanVerifier.TryDetectRouteRedundantCargoRoundTrip(
                effectiveRequest, verification.VerifiedPlan, out string detail)) {
            return new RoutePlanPublicationResult(
                false,
                null,
                normalization.Changed || boundaryChanged,
                normalization.Changes,
                new RoutePlanPublicationFailure(
                    "route-redundant-cargo-roundtrip", detail));
        }

        return new RoutePlanPublicationResult(
            true,
            verification.VerifiedPlan,
            normalization.Changed || boundaryChanged,
            normalization.Changes,
            null) {
            RetainedPlan = source is RoutePlanPublicationSource.CompletionProgress
                    or RoutePlanPublicationSource.ProgressRestore
                ? candidate
                : verification.VerifiedPlan,
        };
    }

    private static ProgressProjection ProjectRemainingPlan(
        AutomaticRoutePlanningRequest request,
        RoutePlan candidate,
        IReadOnlySet<string> completedBarterRowIds) {
        if (completedBarterRowIds.Count == 0) {
            return new ProgressProjection(request, new RoutePlan(
                candidate.Status,
                candidate.Routes,
                candidate.Objective,
                candidate.Diagnostics,
                RoutePlanFingerprint.Compute(request)));
        }

        var remainingTasks = request.Tasks
            .Where(task => !RouteTaskIdentity.IsCompleted(
                task.RowId, completedBarterRowIds))
            .ToArray();

        var routes = new List<PlannedRoute>();
        var projectedWarehouseInventory = request.Warehouses.ToDictionary(
            warehouse => warehouse.WarehouseId,
            warehouse => new Dictionary<string, int>(
                warehouse.Inventory, StringComparer.Ordinal),
            StringComparer.Ordinal);
        IReadOnlyDictionary<string, int>? firstRouteCarry = null;
        foreach (var route in candidate.Routes) {
            int firstRemainingBarterIndex = route.Steps
                .Select((step, index) => (step, index))
                .Where(pair => pair.step is BarterStep barter
                    && !RouteTaskIdentity.IsCompleted(barter.RowId, completedBarterRowIds))
                .Select(pair => pair.index)
                .DefaultIfEmpty(-1)
                .First();
            if (firstRemainingBarterIndex < 0) {
                // A fully completed route disappears from the remaining-plan
                // projection, but its warehouse effects have already happened.
                // Later routes may pick up an item produced and unloaded by
                // this route, so carry those pickup/unload deltas forward.
                if (route.Steps.OfType<BarterStep>().Any()) {
                    ApplyCompletedRouteWarehouseEffects(
                        route.Steps, projectedWarehouseInventory);
                }
                continue;
            }

            bool hasCompletedPrefix = route.Steps
                .Take(firstRemainingBarterIndex)
                .OfType<BarterStep>()
                .Any(barter => RouteTaskIdentity.IsCompleted(barter.RowId, completedBarterRowIds));
            var steps = route.Steps
                .Where(step => step is not BarterStep barter
                    || !RouteTaskIdentity.IsCompleted(barter.RowId, completedBarterRowIds))
                .ToArray();
            steps = RemapRemainingTaskIdentities(steps, remainingTasks);
            if (routes.Count == 0 && hasCompletedPrefix) {
                // The ship has already executed the pickup and completed
                // barter prefix. Derive only the missing handoff cargo from
                // the filtered route. Unfinished pickup cargo remains in the
                // route and the normalizer removes the part used solely by
                // completed steps. This avoids double-counting items that
                // the caller already supplied in InitialOnBoard.
                firstRouteCarry = RequiredInitialCargo(steps);
            }
            routes.Add(new PlannedRoute(
                routes.Count + 1,
                route.StartWarehouseId,
                route.EndWarehouseId,
                steps,
                route.Distance,
                0,
                0,
                0));
        }

        // Planner CK removes an entire Planner row from the live request, but a
        // capacity-split map completion intentionally leaves the parent row
        // active until its final segment is done. Remove exact completed task
        // ids here so replay/verifier compare the projection against the true
        // remaining work instead of requiring the completed segment again.
        var remainingTaskRequest = CopyWithTasks(
            request,
            remainingTasks);
        var warehouseAdjustedRequest = CopyWithWarehouseInventory(
            remainingTaskRequest, projectedWarehouseInventory);
        var effectiveRequest = firstRouteCarry is null
            ? warehouseAdjustedRequest
            : CopyWithInitialOnBoard(warehouseAdjustedRequest, firstRouteCarry);
        return new ProgressProjection(effectiveRequest, new RoutePlan(
            candidate.Status,
            routes,
            null,
            candidate.Diagnostics,
            RoutePlanFingerprint.Compute(effectiveRequest)));
    }

    private static IReadOnlyDictionary<string, int> RequiredInitialCargo(
        IReadOnlyList<RouteStep> steps) {
        var balance = new Dictionary<string, int>(StringComparer.Ordinal);
        var minimum = new Dictionary<string, int>(StringComparer.Ordinal);

        void Apply(string itemId, int delta) {
            int next = checked(balance.GetValueOrDefault(itemId) + delta);
            balance[itemId] = next;
            minimum[itemId] = Math.Min(minimum.GetValueOrDefault(itemId), next);
        }

        foreach (var step in steps) {
            switch (step) {
                case WarehousePickupStep pickup:
                    foreach (var item in pickup.Items) Apply(item.ItemId, item.Quantity);
                    break;
                case BarterStep barter:
                    Apply(barter.Consumed.ItemId, -barter.Consumed.Quantity);
                    Apply(barter.Produced.ItemId, barter.Produced.Quantity);
                    break;
                case WarehouseUnloadStep unload:
                    foreach (var item in unload.Items) Apply(item.ItemId, -item.Quantity);
                    break;
            }
        }

        return minimum
            .Where(pair => pair.Value < 0)
            .ToDictionary(pair => pair.Key, pair => -pair.Value, StringComparer.Ordinal);
    }

    private static void ApplyCompletedRouteWarehouseEffects(
        IReadOnlyList<RouteStep> steps,
        IDictionary<string, Dictionary<string, int>> warehouseInventory) {
        foreach (var step in steps) {
            switch (step) {
                case WarehousePickupStep pickup
                    when warehouseInventory.TryGetValue(
                        pickup.WarehouseId, out var pickupStock):
                    foreach (var item in pickup.Items) {
                        pickupStock[item.ItemId] = checked(
                            pickupStock.GetValueOrDefault(item.ItemId) - item.Quantity);
                    }
                    break;
                case WarehouseUnloadStep unload
                    when warehouseInventory.TryGetValue(
                        unload.WarehouseId, out var unloadStock):
                    foreach (var item in unload.Items) {
                        unloadStock[item.ItemId] = checked(
                            unloadStock.GetValueOrDefault(item.ItemId) + item.Quantity);
                    }
                    break;
            }
        }
    }

    private static AutomaticRoutePlanningRequest CopyWithWarehouseInventory(
        AutomaticRoutePlanningRequest request,
        IReadOnlyDictionary<string, Dictionary<string, int>> warehouseInventory) => new(
            request.Tasks,
            request.Items,
            request.Warehouses.Select(warehouse => new RouteWarehouse(
                warehouse.WarehouseId,
                warehouse.IslandId,
                warehouse.Point,
                warehouseInventory.TryGetValue(warehouse.WarehouseId, out var inventory)
                    ? inventory
                    : warehouse.Inventory)).ToArray(),
            request.ExtraLT,
            request.TotalLT,
            request.Limits,
            request.ConfigurationVersion,
            request.InitialOnBoard);

    private static AutomaticRoutePlanningRequest CopyWithTasks(
        AutomaticRoutePlanningRequest request,
        IReadOnlyList<RouteBarterTask> tasks) => new(
            tasks,
            request.Items,
            request.Warehouses,
            request.ExtraLT,
            request.TotalLT,
            request.Limits,
            request.ConfigurationVersion,
            request.InitialOnBoard);

    private static RouteStep[] RemapRemainingTaskIdentities(
        IReadOnlyList<RouteStep> steps,
        IReadOnlyList<RouteBarterTask> remainingTasks) => steps
            .Select(step => {
                if (step is not BarterStep barter) return step;
                var exact = remainingTasks.FirstOrDefault(task =>
                    StringComparer.Ordinal.Equals(task.RowId, barter.RowId));
                if (exact is not null) return step;

                string plannerRowId = RouteTaskIdentity.PlannerRowId(barter.RowId);
                var candidates = remainingTasks.Where(task =>
                        StringComparer.Ordinal.Equals(
                            RouteTaskIdentity.PlannerRowId(task.RowId), plannerRowId)
                        && StringComparer.Ordinal.Equals(task.IslandId, barter.IslandId)
                        && StringComparer.Ordinal.Equals(
                            task.Item1Id, barter.Consumed.ItemId)
                        && task.InputQuantity == barter.Consumed.Quantity
                        && StringComparer.Ordinal.Equals(
                            task.Item2Id, barter.Produced.ItemId)
                        && task.OutputQuantity == barter.Produced.Quantity)
                    .ToArray();
                return candidates.Length == 1
                    ? new BarterStep(
                        candidates[0].RowId,
                        barter.IslandId,
                        barter.Consumed,
                        barter.Produced,
                        barter.Load)
                    : step;
            })
            .ToArray();

    private static AutomaticRoutePlanningRequest CopyWithInitialOnBoard(
        AutomaticRoutePlanningRequest request,
        IReadOnlyDictionary<string, int> initialOnBoard) => new(
            request.Tasks,
            request.Items,
            request.Warehouses,
            request.ExtraLT,
            request.TotalLT,
            request.Limits,
            request.ConfigurationVersion,
            initialOnBoard);

    private sealed record ProgressProjection(
        AutomaticRoutePlanningRequest Request,
        RoutePlan Plan);

    private static RoutePlanPublicationResult Failed(string code, string detail) =>
        new(false, null, false, [], new RoutePlanPublicationFailure(code, detail));

    private static readonly IReadOnlySet<string> EmptyCompleted =
        new HashSet<string>(StringComparer.Ordinal);
}
