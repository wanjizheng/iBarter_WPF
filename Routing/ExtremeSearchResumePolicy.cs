namespace iBarter.Routing;

/// <summary>
/// Decides whether the UI should offer to continue an Extreme search from a
/// plan restored from disk. The fingerprint guard prevents a route saved for
/// different Planner, warehouse, cargo, or search inputs from becoming a seed.
/// </summary>
public static class ExtremeSearchResumePolicy {
    public static bool CanOfferContinuation(
        bool restoredFromDisk,
        RouteOptimizationMode currentPlanMode,
        RoutePlan? currentPlan,
        string requestFingerprint) =>
        restoredFromDisk
        && currentPlanMode is RouteOptimizationMode.Deep or RouteOptimizationMode.Extreme
        && currentPlan?.Status is RoutePlanStatus.Optimal
            or RoutePlanStatus.BestKnownWithinLimit
        && StringComparer.Ordinal.Equals(currentPlan.InputFingerprint, requestFingerprint);
}
