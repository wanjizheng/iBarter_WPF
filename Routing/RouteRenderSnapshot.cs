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
    public IReadOnlySet<string> BarterIslandIds { get; }
    public IReadOnlySet<string> WarehouseIslandIds { get; }
    public IReadOnlySet<string> HighlightedIslandIds { get; }

    public RouteRenderSnapshot(
        bool isManual,
        bool showAll,
        IReadOnlyList<RouteRenderPath> paths,
        IEnumerable<string>? barterIslandIds = null,
        IEnumerable<string>? warehouseIslandIds = null) {
        IsManual = isManual;
        ShowAll = showAll;
        Paths = ModelCopies.List(paths);
        BarterIslandIds = new HashSet<string>(barterIslandIds ?? [], StringComparer.Ordinal);
        WarehouseIslandIds = new HashSet<string>(warehouseIslandIds ?? [], StringComparer.Ordinal);
        HighlightedIslandIds = new HashSet<string>(
            BarterIslandIds.Concat(WarehouseIslandIds).Concat(Paths.SelectMany(path => path.IslandIds)),
            StringComparer.Ordinal);
    }
}

public static class RouteRenderSnapshotFactory {
    public static RouteRenderSnapshot CreateAutomatic(
        RoutePlan plan,
        int? selectedRouteNumber,
        bool showAll,
        IReadOnlySet<string>? completedBarterRowIds = null) {
        var completed = completedBarterRowIds ?? new HashSet<string>(StringComparer.Ordinal);
        var routes = (showAll
            ? plan.Routes
            : plan.Routes.Where(x => x.Number == selectedRouteNumber))
            .Select(route => new {
                Route = route,
                Steps = RouteProgressFilter.ExcludeCompletedBarters(route.Steps, completed),
            }).ToArray();
        var paths = routes.OrderBy(x => x.Route.Number).Select(route => new RouteRenderPath(
            route.Route.Number,
            Math.Max(0, route.Route.Number - 1),
            CollapseAdjacent(route.Steps.Select(x => x.IslandId)))).ToArray();
        return new RouteRenderSnapshot(
            false,
            showAll,
            paths,
            routes.SelectMany(route => route.Steps.OfType<BarterStep>()).Select(step => step.IslandId),
            routes.SelectMany(route => route.Steps)
                .Where(step => step is WarehousePickupStep or WarehouseUnloadStep)
                .Select(step => step.IslandId));
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
