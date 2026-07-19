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
    RoutePlanPublicationFailure? Failure);

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

        RoutePlan preparedCandidate = source is RoutePlanPublicationSource.CompletionProgress
            or RoutePlanPublicationSource.ProgressRestore
            ? ProjectRemainingPlan(request, candidate, completedBarterRowIds ?? EmptyCompleted)
            : candidate;

        if (!StringComparer.Ordinal.Equals(
                preparedCandidate.InputFingerprint,
                RoutePlanFingerprint.Compute(request))) {
            return Failed("publication-fingerprint", source.ToString());
        }

        var normalization = RouteCargoNormalizer.Normalize(request, preparedCandidate);
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
        var replay = RouteReplay.ReplayPlan(request, normalization.Plan);
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

        var verification = RoutePlanVerifier.Verify(request, replay.Plan);
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
                request, verification.VerifiedPlan, out string detail)) {
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
            null);
    }

    private static RoutePlan ProjectRemainingPlan(
        AutomaticRoutePlanningRequest request,
        RoutePlan candidate,
        IReadOnlySet<string> completedBarterRowIds) {
        if (completedBarterRowIds.Count == 0) {
            return new RoutePlan(
                candidate.Status,
                candidate.Routes,
                candidate.Objective,
                candidate.Diagnostics,
                RoutePlanFingerprint.Compute(request));
        }

        var routes = new List<PlannedRoute>();
        foreach (var route in candidate.Routes) {
            var steps = route.Steps
                .Where(step => step is not BarterStep barter
                    || !completedBarterRowIds.Contains(barter.RowId))
                .ToArray();
            if (!steps.OfType<BarterStep>().Any()) continue;
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
        return new RoutePlan(
            candidate.Status,
            routes,
            null,
            candidate.Diagnostics,
            RoutePlanFingerprint.Compute(request));
    }

    private static RoutePlanPublicationResult Failed(string code, string detail) =>
        new(false, null, false, [], new RoutePlanPublicationFailure(code, detail));

    private static readonly IReadOnlySet<string> EmptyCompleted =
        new HashSet<string>(StringComparer.Ordinal);
}
