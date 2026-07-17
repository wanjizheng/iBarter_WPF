namespace iBarter.Routing;

public static class RoutePlanRestoreCompatibility {
    public static bool IsCompatibleAfterProgress(
        AutomaticRoutePlanningRequest currentRequest,
        RoutePlan persistedPlan,
        IReadOnlySet<string> completedBarterRowIds) {
        if (completedBarterRowIds.Count == 0
            || persistedPlan.Status is not (RoutePlanStatus.Optimal
                or RoutePlanStatus.BestKnownWithinLimit)
            || persistedPlan.Routes.Count == 0)
            return false;

        var savedBarters = persistedPlan.Routes
            .SelectMany(route => route.Steps.OfType<BarterStep>())
            .ToArray();
        if (savedBarters.Length == 0
            || savedBarters.Select(step => step.RowId).Distinct(StringComparer.Ordinal).Count()
                != savedBarters.Length)
            return false;

        var savedByRow = savedBarters.ToDictionary(step => step.RowId, StringComparer.Ordinal);
        var currentTasks = currentRequest.Tasks.ToArray();
        if (currentTasks.Select(task => task.RowId).Distinct(StringComparer.Ordinal).Count()
                != currentTasks.Length
            || currentTasks.Any(task => !savedByRow.ContainsKey(task.RowId)))
            return false;

        var currentByRow = currentTasks.ToDictionary(task => task.RowId, StringComparer.Ordinal);
        if (savedBarters.Any(step => !currentByRow.ContainsKey(step.RowId)
                && !completedBarterRowIds.Contains(step.RowId))
            || !savedBarters.Any(step => completedBarterRowIds.Contains(step.RowId)))
            return false;

        if (!CanReachFirstRemainingBarter(
                currentRequest, persistedPlan, currentByRow, completedBarterRowIds))
            return false;

        foreach (var task in currentTasks) {
            var saved = savedByRow[task.RowId];
            if (!StringComparer.Ordinal.Equals(saved.IslandId, task.IslandId)
                || !StringComparer.Ordinal.Equals(saved.Consumed.ItemId, task.Item1Id)
                || saved.Consumed.Quantity != task.InputQuantity
                || !StringComparer.Ordinal.Equals(saved.Produced.ItemId, task.Item2Id)
                || saved.Produced.Quantity != task.OutputQuantity)
                return false;
        }

        var warehouses = currentRequest.Warehouses.ToDictionary(
            warehouse => warehouse.WarehouseId, StringComparer.Ordinal);
        int expectedRouteNumber = 1;
        foreach (var route in persistedPlan.Routes) {
            if (route.Number != expectedRouteNumber++) return false;
            foreach (var step in route.Steps) {
                if (step.Load.TotalWithExtraLT - step.Load.CargoLT != currentRequest.ExtraLT
                    || step.Load.TotalWithExtraLT > currentRequest.TotalLT)
                    return false;
                if (step is WarehousePickupStep pickup
                    && (!warehouses.TryGetValue(pickup.WarehouseId, out var pickupWarehouse)
                        || !StringComparer.Ordinal.Equals(pickupWarehouse.IslandId, pickup.IslandId)))
                    return false;
                if (step is WarehouseUnloadStep unload
                    && (!warehouses.TryGetValue(unload.WarehouseId, out var unloadWarehouse)
                        || !StringComparer.Ordinal.Equals(unloadWarehouse.IslandId, unload.IslandId)))
                    return false;
            }
        }
        return true;
    }

    private static bool CanReachFirstRemainingBarter(
        AutomaticRoutePlanningRequest request,
        RoutePlan persistedPlan,
        IReadOnlyDictionary<string, RouteBarterTask> currentByRow,
        IReadOnlySet<string> completedBarterRowIds) {
        foreach (var route in persistedPlan.Routes) {
            if (!route.Steps.OfType<BarterStep>().Any(step => currentByRow.ContainsKey(step.RowId)))
                continue;

            var onboard = request.InitialOnBoard.ToDictionary(
                pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            foreach (var step in route.Steps) {
                switch (step) {
                    case WarehousePickupStep pickup:
                        foreach (var item in pickup.Items)
                            onboard[item.ItemId] = checked(onboard.GetValueOrDefault(item.ItemId) + item.Quantity);
                        break;
                    case WarehouseUnloadStep unload:
                        foreach (var item in unload.Items) {
                            int remaining = onboard.GetValueOrDefault(item.ItemId) - item.Quantity;
                            if (remaining > 0) onboard[item.ItemId] = remaining;
                            else onboard.Remove(item.ItemId);
                        }
                        break;
                    case BarterStep barter when currentByRow.ContainsKey(barter.RowId):
                        return onboard.GetValueOrDefault(barter.Consumed.ItemId) >= barter.Consumed.Quantity;
                    case BarterStep barter when completedBarterRowIds.Contains(barter.RowId):
                        int available = onboard.GetValueOrDefault(barter.Consumed.ItemId);
                        if (available < barter.Consumed.Quantity) return false;
                        int afterConsume = available - barter.Consumed.Quantity;
                        if (afterConsume == 0) onboard.Remove(barter.Consumed.ItemId);
                        else onboard[barter.Consumed.ItemId] = afterConsume;
                        onboard[barter.Produced.ItemId] = checked(
                            onboard.GetValueOrDefault(barter.Produced.ItemId) + barter.Produced.Quantity);
                        break;
                }
            }
            return false;
        }
        return true;
    }
}
