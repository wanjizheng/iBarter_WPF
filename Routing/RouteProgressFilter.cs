namespace iBarter.Routing;

public static class RouteProgressFilter {
    public static IReadOnlyList<RouteStep> ExcludeCompletedBarters(
        IEnumerable<RouteStep> steps,
        IReadOnlySet<string> completedBarterRowIds) => steps
        .Where(step => step is not BarterStep barter
            || !completedBarterRowIds.Contains(barter.RowId))
        .ToArray();
}
