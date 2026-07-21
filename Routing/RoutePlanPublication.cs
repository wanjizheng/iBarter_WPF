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

        var verification = RoutePlanVerifier.Verify(effectiveRequest, replay.Plan);
        if (!verification.Success || verification.VerifiedPlan is null) {
            return new RoutePlanPublicationResult(
                false,
                null,
                normalization.Changed,
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
                normalization.Changed,
                normalization.Changes,
                new RoutePlanPublicationFailure(
                    "route-redundant-cargo-roundtrip", detail));
        }

        return new RoutePlanPublicationResult(
            true,
            verification.VerifiedPlan,
            normalization.Changed,
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

        var routes = new List<PlannedRoute>();
        IReadOnlyDictionary<string, int>? firstRouteCarry = null;
        foreach (var route in candidate.Routes) {
            int firstRemainingBarterIndex = route.Steps
                .Select((step, index) => (step, index))
                .Where(pair => pair.step is BarterStep barter
                    && !RouteTaskIdentity.IsCompleted(barter.RowId, completedBarterRowIds))
                .Select(pair => pair.index)
                .DefaultIfEmpty(-1)
                .First();
            if (firstRemainingBarterIndex < 0) continue;

            bool hasCompletedPrefix = route.Steps
                .Take(firstRemainingBarterIndex)
                .OfType<BarterStep>()
                .Any(barter => RouteTaskIdentity.IsCompleted(barter.RowId, completedBarterRowIds));
            var steps = route.Steps
                .Where(step => step is not BarterStep barter
                    || !RouteTaskIdentity.IsCompleted(barter.RowId, completedBarterRowIds))
                .ToArray();
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

        var effectiveRequest = firstRouteCarry is null
            ? request
            : CopyWithInitialOnBoard(request, firstRouteCarry);
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
