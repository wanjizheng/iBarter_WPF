namespace iBarter.Routing;

/// <summary>
/// Builds the request used to verify route-completion progress. Planner state
/// supplies the current remaining tasks and carried cargo, while the active
/// route's published request remains authoritative for warehouse inventory and
/// cargo capacity.
/// </summary>
public static class RouteProgressRequestBuilder {
    public static AutomaticRoutePlanningRequest MergePublishedExecutionState(
        AutomaticRoutePlanningRequest publishedRequest,
        AutomaticRoutePlanningRequest progressRequest) {
        ArgumentNullException.ThrowIfNull(publishedRequest);
        ArgumentNullException.ThrowIfNull(progressRequest);

        return new AutomaticRoutePlanningRequest(
            progressRequest.Tasks,
            progressRequest.Items,
            publishedRequest.Warehouses,
            publishedRequest.ExtraLT,
            publishedRequest.TotalLT,
            publishedRequest.Limits,
            publishedRequest.ConfigurationVersion,
            progressRequest.InitialOnBoard);
    }
}
