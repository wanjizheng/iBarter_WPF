namespace iBarter.Routing;

/// <summary>Reuses a tour across search modes only when all physical planning inputs still match.</summary>
public static class RouteIncumbentReuse {
    public static RoutePlan? ForSearchBudget(AutomaticRoutePlanningRequest request,
        AutomaticRoutePlanningRequest? previousRequest, RoutePlan? previousPlan) {
        if (previousRequest is null || previousPlan?.Status is not (RoutePlanStatus.Optimal or RoutePlanStatus.BestKnownWithinLimit)) return null;
        // Persisted fingerprints include search limits. Keep that format unchanged,
        // but compare the new physical inputs with the old limits for warm starts.
        var comparable = new AutomaticRoutePlanningRequest(request.Tasks, request.Items, request.Warehouses,
            request.ExtraLT, request.TotalLT, previousRequest.Limits, request.ConfigurationVersion, request.InitialOnBoard);
        if (RoutePlanFingerprint.Compute(comparable) != previousPlan.InputFingerprint) return null;
        var candidate = new RoutePlan(previousPlan.Status, previousPlan.Routes, previousPlan.Objective,
            previousPlan.Diagnostics, RoutePlanFingerprint.Compute(request));
        var verified = RoutePlanVerifier.Verify(request, candidate);
        return verified.Success ? verified.VerifiedPlan : null;
    }
}
