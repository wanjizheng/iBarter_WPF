namespace iBarter.Routing;

public sealed record RouteFocusSegment(string FromIslandId, string ToIslandId);

public static class RouteProgressFilter {
    public static IReadOnlyList<RouteStep> ExcludeCompletedBarters(
        IEnumerable<RouteStep> steps,
        IReadOnlySet<string> completedBarterRowIds) => steps
        .Where(step => step is not BarterStep barter
            || !completedBarterRowIds.Contains(barter.RowId))
        .ToArray();

    public static RouteFocusSegment? FindVisibleBarterSegment(
        IEnumerable<RouteStep> steps,
        IReadOnlySet<string> completedBarterRowIds,
        string rowId) {
        var visible = ExcludeCompletedBarters(steps, completedBarterRowIds);
        int index = visible.ToList().FindIndex(
            step => step is BarterStep barter
                && StringComparer.Ordinal.Equals(barter.RowId, rowId));
        if (index < 0) return null;

        string destination = visible[index].IslandId;
        int previous = index - 1;
        while (previous >= 0
            && StringComparer.Ordinal.Equals(visible[previous].IslandId, destination))
            previous--;
        return previous < 0
            ? null
            : new RouteFocusSegment(visible[previous].IslandId, destination);
    }
}
