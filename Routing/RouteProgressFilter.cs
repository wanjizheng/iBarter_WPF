namespace iBarter.Routing;

public sealed record RouteFocusSegment(string FromIslandId, string ToIslandId);

public static class RouteProgressFilter {
    public static IReadOnlyList<RouteStep> ExcludeCompletedBarters(
        IEnumerable<RouteStep> steps,
        IReadOnlySet<string> completedBarterRowIds) => steps
        .Where(step => step is not BarterStep barter
            || !completedBarterRowIds.Contains(barter.RowId))
        .ToArray();

    public static IReadOnlyList<RouteStep> RemainingMapSteps(
        IEnumerable<RouteStep> steps,
        IReadOnlySet<string> completedBarterRowIds) {
        var route = steps.ToArray();
        int completedPrefixEnd = -1;
        for (int index = 0; index < route.Length; index++) {
            if (route[index] is not BarterStep barter) continue;
            if (!completedBarterRowIds.Contains(barter.RowId)) break;
            completedPrefixEnd = index;
        }

        return ExcludeCompletedBarters(
            completedPrefixEnd < 0 ? route : route.Skip(completedPrefixEnd + 1),
            completedBarterRowIds);
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
}
