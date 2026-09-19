namespace iBarter.Routing;

internal static class TaggedDistanceObjective {
    public static int Compare(TaggedTransportRequest request, TaggedTransportState left, TaggedTransportState right) {
        int result = left.Distance.CompareTo(right.Distance);
        if (result != 0) return result;
        result = left.OverloadedDistance.CompareTo(right.OverloadedDistance);
        if (result != 0) return result;
        result = RouteCount(request, left).CompareTo(RouteCount(request, right));
        if (result != 0) return result;
        // At equal distance/overloaded legs/trip count, prefer freeing more ship
        // capacity, even when prefilling a character takes one extra transfer.
        result = SailingLoad(request, left).CompareTo(SailingLoad(request, right));
        return result != 0 ? result : left.Steps.Count.CompareTo(right.Steps.Count);
    }
    private static double SailingLoad(TaggedTransportRequest request, TaggedTransportState state) =>
        state.Steps.Where(s => s.Action.Kind == TaggedActionKind.Sail).Sum(s => {
            var a = request.Points[s.Action.From]; var b = request.Points[s.Action.Location];
            return s.ShipLT * Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        });
    private static int RouteCount(TaggedTransportRequest request, TaggedTransportState state) => TaggedTransportRoutes.Build(request,
        new("", state.Steps.ToArray(), state.Seconds, state.SailingSeconds, state.Seconds - state.SailingSeconds, state.Distance, "")).Length;
}
