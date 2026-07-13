namespace iBarter.Routing;

public sealed class RouteRenderPath {
    public int RouteNumber { get; }
    public int ColorIndex { get; }
    public IReadOnlyList<string> IslandIds { get; }

    public RouteRenderPath(int routeNumber, int colorIndex, IReadOnlyList<string> islandIds) {
        RouteNumber = routeNumber;
        ColorIndex = colorIndex;
        IslandIds = ModelCopies.List(islandIds);
    }
}

public sealed class RouteRenderSnapshot {
    public bool IsManual { get; }
    public bool ShowAll { get; }
    public IReadOnlyList<RouteRenderPath> Paths { get; }

    public RouteRenderSnapshot(bool isManual, bool showAll, IReadOnlyList<RouteRenderPath> paths) {
        IsManual = isManual;
        ShowAll = showAll;
        Paths = ModelCopies.List(paths);
    }
}

public static class RouteRenderSnapshotFactory {
    public static RouteRenderSnapshot CreateAutomatic(
        RoutePlan plan,
        int? selectedRouteNumber,
        bool showAll) {
        IEnumerable<PlannedRoute> routes = showAll
            ? plan.Routes
            : plan.Routes.Where(x => x.Number == selectedRouteNumber);
        var paths = routes.OrderBy(x => x.Number).Select(route => new RouteRenderPath(
            route.Number,
            Math.Max(0, route.Number - 1),
            CollapseAdjacent(route.Steps.Select(x => x.IslandId)))).ToArray();
        return new RouteRenderSnapshot(false, showAll, paths);
    }

    public static RouteRenderSnapshot CreateManual(IReadOnlyList<string> islandIds) =>
        new(true, false, [new RouteRenderPath(0, 0, CollapseAdjacent(islandIds))]);

    private static IReadOnlyList<string> CollapseAdjacent(IEnumerable<string> islandIds) {
        var result = new List<string>();
        foreach (string islandId in islandIds.Where(x => !string.IsNullOrWhiteSpace(x)))
            if (result.Count == 0 || !StringComparer.Ordinal.Equals(result[^1], islandId))
                result.Add(islandId);
        return result;
    }
}
