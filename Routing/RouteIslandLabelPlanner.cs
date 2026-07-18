namespace iBarter.Routing;

/// <summary>
/// Audit round 4 (regression fix): testable helper that computes the
/// set of island ids the map must render labels for, given the
/// current route plan, mode and selection. Extracted from
/// MapControl.EnsureRouteIslandLabels so the regression has a unit
/// test (the actual WPF Label wiring stays in MapControl.xaml.cs,
/// but the decision is pure and is exercised here).
/// </summary>
public static class RouteIslandLabelPlanner {
    /// <summary>
    /// Returns the unique set of island ids touched by the visible
    /// routes. <c>showAll</c> selects every route; otherwise only
    /// <paramref name="selectedRouteNumber"/> is used. Empty island
    /// ids are skipped. A <c>null</c> plan yields an empty set.
    /// </summary>
    public static IReadOnlyCollection<string> VisibleIslandIds(
        RoutePlan? plan,
        bool showAll,
        int? selectedRouteNumber) {
        if (plan is null) return Array.Empty<string>();
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var route in plan.Routes) {
            if (!showAll && route.Number != selectedRouteNumber) continue;
            foreach (var step in route.Steps) {
                if (!string.IsNullOrEmpty(step.IslandId))
                    result.Add(step.IslandId);
            }
        }
        return result;
    }

    /// <summary>
    /// Filters out islands that already have a warehouse label
    /// (the warehouse node draws "伊利亚岛 · 装货" or "伊利亚岛
    /// · 卸货" via EnsureAutomaticWarehouseNodes, and we must
    /// not stack a duplicate island base label on top).
    /// The caller passes the island ids rendered as warehouses in
    /// the current render snapshot.
    /// </summary>
    public static IReadOnlyCollection<string> ExcludeWarehouseIslands(
        IReadOnlyCollection<string> islandIds,
        IReadOnlySet<string> warehouseIslandIds) {
        var result = new List<string>(islandIds.Count);
        foreach (var id in islandIds) {
            if (warehouseIslandIds.Contains(id)) continue;
            result.Add(id);
        }
        return result;
    }
}