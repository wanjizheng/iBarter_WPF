namespace iBarter.Routing;

public sealed record RouteFocusSegment(string FromIslandId, string ToIslandId);

public static class RouteProgressFilter {
    public static IReadOnlyList<PlannedRoute> RemainingRoutes(
        IEnumerable<PlannedRoute> routes,
        IReadOnlySet<string> completedBarterRowIds) => routes
        .Where(route => route.Steps.OfType<BarterStep>()
            .Any(step => !RouteTaskIdentity.IsCompleted(step.RowId, completedBarterRowIds)))
        .ToArray();

    public static IReadOnlyList<RouteStep> ExcludeCompletedBarters(
        IEnumerable<RouteStep> steps,
        IReadOnlySet<string> completedBarterRowIds) => steps
        .Where(step => step is not BarterStep barter
            || !RouteTaskIdentity.IsCompleted(barter.RowId, completedBarterRowIds))
        .ToArray();

    public static IReadOnlyList<RouteStep> RemainingMapSteps(
        IEnumerable<RouteStep> steps,
        IReadOnlySet<string> completedBarterRowIds) {
        var route = steps.ToArray();
        int completedPrefixEnd = -1;
        for (int index = 0; index < route.Length; index++) {
            if (route[index] is not BarterStep barter) continue;
            if (!RouteTaskIdentity.IsCompleted(barter.RowId, completedBarterRowIds)) break;
            completedPrefixEnd = index;
        }

        var remaining = ExcludeCompletedBarters(
            completedPrefixEnd < 0 ? route : route.Skip(completedPrefixEnd + 1),
            completedBarterRowIds);
        // A pickup/unload shell is not a remaining route. Once every barter
        // has been completed, hide the entire route so a final unload on the
        // same island cannot leave a ghost marker, label, or cargo card.
        return remaining.OfType<BarterStep>().Any()
            ? remaining
            : Array.Empty<RouteStep>();
    }

    public static RouteFocusSegment? FindVisibleBarterSegment(
        IEnumerable<RouteStep> steps,
        IReadOnlySet<string> completedBarterRowIds,
        string rowId) {
        var visible = RemainingMapSteps(steps, completedBarterRowIds);
        int index = visible.ToList().FindIndex(
            step => step is BarterStep barter
                && StringComparer.Ordinal.Equals(barter.RowId, rowId));
        if (index < 0) return null;

        string destination = visible[index].IslandId;
        int previous = index - 1;
        while (previous >= 0
            && StringComparer.Ordinal.Equals(visible[previous].IslandId, destination))
            previous--;
        if (previous >= 0)
            return new RouteFocusSegment(visible[previous].IslandId, destination);

        int next = index + 1;
        while (next < visible.Count
            && StringComparer.Ordinal.Equals(visible[next].IslandId, destination))
            next++;
        return next >= visible.Count
            ? null
            : new RouteFocusSegment(destination, visible[next].IslandId);
    }

    public static string? FindVisibleBarterRowForSegment(
        IEnumerable<RouteStep> steps,
        IReadOnlySet<string> completedBarterRowIds,
        string fromIslandId,
        string toIslandId) {
        var route = steps.ToArray();
        foreach (var barter in RemainingMapSteps(route, completedBarterRowIds)
            .OfType<BarterStep>()) {
            var segment = FindVisibleBarterSegment(
                route, completedBarterRowIds, barter.RowId);
            if (segment is not null
                && StringComparer.Ordinal.Equals(segment.FromIslandId, fromIslandId)
                && StringComparer.Ordinal.Equals(segment.ToIslandId, toIslandId))
                return barter.RowId;
        }
        return null;
    }
}
