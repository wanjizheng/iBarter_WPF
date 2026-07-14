namespace iBarter.Routing;

using iBarter.Navigation;

public sealed record WarehouseUnloadInstruction(
    string WarehouseId,
    IReadOnlyList<RouteItemQuantity> Items,
    bool FinishRoute);

/// <summary>
/// Chooses deterministic, item-aware unload stops from the request's initial
/// StorageManager snapshot. Existing item homes win; otherwise the nearest
/// warehouse is used as a fallback.
/// </summary>
public static class WarehouseUnloadPlanner {
    public static IReadOnlyList<WarehouseUnloadInstruction> BuildInstructions(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state) {
        var weightedCargo = state.OnBoard
            .Where(x => x.Value > 0
                && request.Items.TryGetValue(x.Key, out var item)
                && item.UnitWeight > 0)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToArray();

        if (weightedCargo.Length == 0) {
            var nearest = OrderWarehousesByDistance(request, state.CurrentIslandId, request.Warehouses).First();
            return [new WarehouseUnloadInstruction(nearest.WarehouseId, [], true)];
        }

        var assigned = new Dictionary<string, List<RouteItemQuantity>>(StringComparer.Ordinal);
        foreach (var cargo in weightedCargo) {
            var preferred = request.Warehouses
                .Where(x => x.Inventory.TryGetValue(cargo.Key, out int initial) && initial > 0)
                .ToArray();
            var candidates = preferred.Length > 0 ? preferred : request.Warehouses.ToArray();
            var target = OrderWarehousesByDistance(request, state.CurrentIslandId, candidates).First();
            if (!assigned.TryGetValue(target.WarehouseId, out var items)) {
                items = [];
                assigned[target.WarehouseId] = items;
            }
            items.Add(new RouteItemQuantity(cargo.Key, cargo.Value));
        }

        var remaining = assigned.Keys.ToHashSet(StringComparer.Ordinal);
        var ordered = new List<WarehouseUnloadInstruction>();
        string current = state.CurrentIslandId;
        while (remaining.Count > 0) {
            var next = OrderWarehousesByDistance(
                    request,
                    current,
                    request.Warehouses.Where(x => remaining.Contains(x.WarehouseId)))
                .First();
            ordered.Add(new WarehouseUnloadInstruction(
                next.WarehouseId,
                assigned[next.WarehouseId].OrderBy(x => x.ItemId, StringComparer.Ordinal).ToArray(),
                false));
            remaining.Remove(next.WarehouseId);
            current = next.IslandId;
        }

        ordered[^1] = ordered[^1] with { FinishRoute = true };
        return ordered;
    }

    public static RouteTransitionResult TryCompleteRoute(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state) {
        RouteTransitionResult? last = null;
        foreach (var instruction in BuildInstructions(request, state)) {
            last = RouteStateTransition.TryUnload(
                request, state, instruction.WarehouseId, instruction.Items, instruction.FinishRoute);
            if (!last.Success) return last;
            state = last.State;
        }
        return last ?? new RouteTransitionResult(false, state, null, new RouteDiagnostic("no-warehouse"));
    }

    private static IEnumerable<RouteWarehouse> OrderWarehousesByDistance(
        AutomaticRoutePlanningRequest request,
        string currentIslandId,
        IEnumerable<RouteWarehouse> warehouses) {
        RoutePoint origin = FindPoint(request, currentIslandId);
        return warehouses
            .OrderBy(x => ShippingCorridorGraph.Distance(
                currentIslandId,
                new NavigationPoint(origin.X, origin.Y),
                x.IslandId,
                new NavigationPoint(x.Point.X, x.Point.Y)))
            .ThenBy(x => x.WarehouseId, StringComparer.Ordinal);
    }

    private static RoutePoint FindPoint(AutomaticRoutePlanningRequest request, string islandId) {
        var warehouse = request.Warehouses.FirstOrDefault(x => x.IslandId == islandId);
        if (warehouse is not null) return warehouse.Point;
        var task = request.Tasks.FirstOrDefault(x => x.IslandId == islandId);
        if (task is not null) return task.Point;
        throw new InvalidOperationException($"Unknown island '{islandId}'.");
    }
}
