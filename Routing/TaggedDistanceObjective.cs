namespace iBarter.Routing;

internal static class TaggedDistanceObjective {
    public static int Compare(TaggedTransportRequest request, TaggedTransportState left, TaggedTransportState right) {
        int result = left.Distance.CompareTo(right.Distance);
        if (result != 0) return result;
        result = left.OverloadedDistance.CompareTo(right.OverloadedDistance);
        if (result != 0) return result;
        result = RouteCount(request, left).CompareTo(RouteCount(request, right));
        return result != 0 ? result : left.Steps.Count.CompareTo(right.Steps.Count);
    }
    private static int RouteCount(TaggedTransportRequest request, TaggedTransportState state) => TaggedTransportRoutes.Build(request,
        new("", state.Steps.ToArray(), state.Seconds, state.SailingSeconds, state.Seconds - state.SailingSeconds, state.Distance, "")).Length;
}
