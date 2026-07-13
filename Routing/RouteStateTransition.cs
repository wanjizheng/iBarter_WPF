namespace iBarter.Routing;

public sealed record RouteTransitionResult(
    bool Success,
    RouteSimulationState State,
    RouteStep? Step,
    RouteDiagnostic? Diagnostic);

public static class RouteStateTransition {
    public static RouteTransitionResult TryPickup(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        string warehouseId,
        IReadOnlyList<RouteItemQuantity> items) {
        var warehouse = request.Warehouses.FirstOrDefault(x => x.WarehouseId == warehouseId);
        if (warehouse is null || !state.WarehouseInventory.TryGetValue(warehouseId, out var stock))
            return Failure(state, "unknown-warehouse", detail: warehouseId);
        if (state.VisitedWarehouseIds.Contains(warehouseId))
            return Failure(state, "warehouse-revisited", detail: warehouseId);
        if (items.Count == 0 || items.Any(x => x.Quantity <= 0 || !request.Items.ContainsKey(x.ItemId)))
            return Failure(state, "insufficient-stock", detail: warehouseId);

        var requested = items
            .GroupBy(x => x.ItemId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity), StringComparer.Ordinal);
        if (requested.Any(x => !stock.TryGetValue(x.Key, out int quantity) || quantity < x.Value))
            return Failure(state, "insufficient-stock", detail: warehouseId);

        var inventory = CloneWarehouseInventory(state);
        var warehouseStock = inventory[warehouseId];
        var onboard = Clone(state.OnBoard);
        foreach (var pair in requested) {
            SetQuantity(warehouseStock, pair.Key, warehouseStock[pair.Key] - pair.Value);
            SetQuantity(onboard, pair.Key, onboard.GetValueOrDefault(pair.Key) + pair.Value);
        }

        int cargoLT = ComputeCargoLT(request, onboard);
        int total = checked(request.ExtraLT + cargoLT);
        if (total > request.TotalLT)
            return Failure(state, "overweight", detail: total.ToString());

        double distance = DistanceFromCurrent(request, state.CurrentIslandId, warehouse.Point);
        int peak = Math.Max(state.CurrentRoutePeakLT, total);
        var load = new RouteLoadSnapshot(cargoLT, total, peak);
        var normalizedItems = requested.OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new RouteItemQuantity(x.Key, x.Value)).ToArray();
        var step = new WarehousePickupStep(warehouseId, warehouse.IslandId, normalizedItems, load);
        var visited = state.VisitedWarehouseIds.Append(warehouseId);
        return Success(state, step, warehouse.IslandId, state.CompletedMask, visited, onboard,
            inventory, cargoLT, peak, state.CurrentRouteSteps.Append(step), state.FinishedRoutes,
            state.TotalDistance + distance, state.PickupStopCount + 1);
    }

    public static RouteTransitionResult TryBarter(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        int taskIndex) {
        if (taskIndex < 0 || taskIndex >= request.Tasks.Count)
            return Failure(state, "missing-input", detail: taskIndex.ToString());
        ulong bit = 1UL << taskIndex;
        if ((state.CompletedMask & bit) != 0)
            return Failure(state, "already-completed", rowId: request.Tasks[taskIndex].RowId);

        var task = request.Tasks[taskIndex];
        if (!state.OnBoard.TryGetValue(task.Item1Id, out int available) || available < task.InputQuantity)
            return Failure(state, "missing-input", task.RowId, task.Item1Id);

        var onboard = Clone(state.OnBoard);
        SetQuantity(onboard, task.Item1Id, available - task.InputQuantity);
        SetQuantity(onboard, task.Item2Id, onboard.GetValueOrDefault(task.Item2Id) + task.OutputQuantity);
        int cargoLT = ComputeCargoLT(request, onboard);
        int total = checked(request.ExtraLT + cargoLT);
        if (total > request.TotalLT)
            return Failure(state, "overweight", task.RowId, task.Item2Id, total.ToString());

        double distance = DistanceFromCurrent(request, state.CurrentIslandId, task.Point);
        int peak = Math.Max(state.CurrentRoutePeakLT, total);
        var load = new RouteLoadSnapshot(cargoLT, total, peak);
        var step = new BarterStep(
            task.RowId, task.IslandId,
            new RouteItemQuantity(task.Item1Id, task.InputQuantity),
            new RouteItemQuantity(task.Item2Id, task.OutputQuantity), load);
        return Success(state, step, task.IslandId, state.CompletedMask | bit,
            state.VisitedWarehouseIds, onboard, CloneWarehouseInventory(state), cargoLT, peak,
            state.CurrentRouteSteps.Append(step), state.FinishedRoutes,
            state.TotalDistance + distance, state.PickupStopCount);
    }

    public static RouteTransitionResult TryUnload(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        string warehouseId) {
        var warehouse = request.Warehouses.FirstOrDefault(x => x.WarehouseId == warehouseId);
        if (warehouse is null || !state.WarehouseInventory.ContainsKey(warehouseId))
            return Failure(state, "unknown-warehouse", detail: warehouseId);
        if (!state.CurrentRouteSteps.OfType<BarterStep>().Any())
            return Failure(state, "empty-route", detail: warehouseId);

        var inventory = CloneWarehouseInventory(state);
        var target = inventory[warehouseId];
        foreach (var pair in state.OnBoard.Where(x => x.Value > 0))
            SetQuantity(target, pair.Key, target.GetValueOrDefault(pair.Key) + pair.Value);

        double legDistance = DistanceFromCurrent(request, state.CurrentIslandId, warehouse.Point);
        int totalBeforeUnload = checked(request.ExtraLT + state.CargoLT);
        int peak = Math.Max(state.CurrentRoutePeakLT, totalBeforeUnload);
        var unloadedItems = state.OnBoard.Where(x => x.Value > 0)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new RouteItemQuantity(x.Key, x.Value)).ToArray();
        var load = new RouteLoadSnapshot(0, request.ExtraLT, peak);
        var step = new WarehouseUnloadStep(warehouseId, warehouse.IslandId, unloadedItems, load);
        var routeSteps = state.CurrentRouteSteps.Append(step).ToArray();
        var firstPickup = routeSteps.OfType<WarehousePickupStep>().FirstOrDefault();
        int initialLT = firstPickup?.Load.TotalWithExtraLT ?? request.ExtraLT;
        double finishedDistance = state.FinishedRoutes.Sum(x => x.Distance);
        double routeDistance = state.TotalDistance + legDistance - finishedDistance;
        var route = new PlannedRoute(
            state.CurrentRouteNumber,
            firstPickup?.WarehouseId ?? warehouseId,
            warehouseId,
            routeSteps,
            routeDistance,
            initialLT,
            totalBeforeUnload,
            peak);
        var finished = state.FinishedRoutes.Append(route);
        var next = new RouteSimulationState(
            warehouse.IslandId, state.CurrentRouteNumber + 1, state.CompletedMask,
            [], [], ToReadOnlyInventory(inventory), 0, request.ExtraLT, [], finished,
            state.TotalDistance + legDistance, state.PickupStopCount);
        return new RouteTransitionResult(true, next, step, null);
    }

    private static RouteTransitionResult Success(
        RouteSimulationState oldState,
        RouteStep step,
        string currentIslandId,
        ulong completedMask,
        IEnumerable<string> visited,
        Dictionary<string, int> onboard,
        Dictionary<string, Dictionary<string, int>> inventory,
        int cargoLT,
        int peak,
        IEnumerable<RouteStep> steps,
        IEnumerable<PlannedRoute> finished,
        double distance,
        int pickups) {
        var state = new RouteSimulationState(
            currentIslandId, oldState.CurrentRouteNumber, completedMask, visited, onboard,
            ToReadOnlyInventory(inventory), cargoLT, peak, steps, finished, distance, pickups);
        return new RouteTransitionResult(true, state, step, null);
    }

    private static RouteTransitionResult Failure(
        RouteSimulationState state, string code, string rowId = "", string itemId = "", string detail = "") =>
        new(false, state, null, new RouteDiagnostic(code, rowId, itemId, detail));

    private static Dictionary<string, int> Clone(IReadOnlyDictionary<string, int> source) =>
        source.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

    private static Dictionary<string, Dictionary<string, int>> CloneWarehouseInventory(RouteSimulationState state) =>
        state.WarehouseInventory.ToDictionary(
            x => x.Key,
            x => Clone(x.Value),
            StringComparer.Ordinal);

    private static IEnumerable<KeyValuePair<string, IReadOnlyDictionary<string, int>>> ToReadOnlyInventory(
        Dictionary<string, Dictionary<string, int>> source) =>
        source.Select(x => new KeyValuePair<string, IReadOnlyDictionary<string, int>>(x.Key, x.Value));

    private static void SetQuantity(Dictionary<string, int> dictionary, string itemId, int quantity) {
        if (quantity == 0) dictionary.Remove(itemId);
        else dictionary[itemId] = quantity;
    }

    private static int ComputeCargoLT(AutomaticRoutePlanningRequest request, IReadOnlyDictionary<string, int> onboard) {
        long total = 0;
        foreach (var pair in onboard) {
            if (!request.Items.TryGetValue(pair.Key, out var item))
                throw new InvalidOperationException($"Unknown route item '{pair.Key}'.");
            total += (long)item.UnitWeight * pair.Value;
        }
        return checked((int)total);
    }

    private static double DistanceFromCurrent(
        AutomaticRoutePlanningRequest request,
        string currentIslandId,
        RoutePoint destination) {
        if (string.IsNullOrEmpty(currentIslandId)) return 0;
        RoutePoint origin = FindPoint(request, currentIslandId);
        double dx = origin.X - destination.X;
        double dy = origin.Y - destination.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static RoutePoint FindPoint(AutomaticRoutePlanningRequest request, string islandId) {
        var warehouse = request.Warehouses.FirstOrDefault(x => x.IslandId == islandId);
        if (warehouse is not null) return warehouse.Point;
        var task = request.Tasks.FirstOrDefault(x => x.IslandId == islandId);
        if (task is not null) return task.Point;
        throw new InvalidOperationException($"Unknown island '{islandId}'.");
    }
}
