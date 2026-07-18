using System.Text;

namespace iBarter.Routing;

public static class RoutePlanRestoreCompatibility {
    /// <summary>
    /// Audit round 3: when a persisted <c>automatic-route-plan.json</c>
    /// still carries the legacy (Island, Item1, Item2) business-key
    /// RowIds from before PlannerRowId was introduced, the direct
    /// match against <see cref="AutomaticRoutePlanningRequest.Tasks"/>
    /// will miss. This shim performs a one-shot legacy migration:
    ///   * a saved RowId that already matches a PlannerRowId wins
    ///     immediately (no migration);
    ///   * a saved (Island, Item1, Item2) tuple that matches
    ///     <strong>exactly one</strong> current task is migrated to
    ///     that task's PlannerRowId;
    ///   * a saved tuple that matches multiple current tasks is
    ///     treated as ambiguous and the whole plan is reported
    ///     incompatible, so the user knows to regenerate.
    /// </summary>
    public sealed record LegacyRowIdMigrationResult(
        bool IsCompatible,
        IReadOnlyDictionary<string, string> SavedRowIdToCurrentRowId);

    public static LegacyRowIdMigrationResult TryMigrateLegacyRowIds(
        RoutePlan persistedPlan,
        AutomaticRoutePlanningRequest currentRequest,
        out IReadOnlyDictionary<(string, string, string), List<string>>? ambiguousTuples) {
        ambiguousTuples = null;
        var savedBarters = persistedPlan.Routes
            .SelectMany(route => route.Steps.OfType<BarterStep>())
            .ToArray();
        var currentTasks = currentRequest.Tasks.ToArray();
        if (savedBarters.Length == 0 || currentTasks.Length == 0) {
            return new LegacyRowIdMigrationResult(true, new Dictionary<string, string>());
        }
        var currentByRow = currentTasks.ToDictionary(task => task.RowId, StringComparer.Ordinal);

        // Group current tasks by (Island, Item1Id, Item2Id) to detect
        // ambiguous legacy matches.
        var currentByTuple = new Dictionary<(string, string, string), List<RouteBarterTask>>();
        foreach (var task in currentTasks) {
            var key = (task.IslandId, task.Item1Id, task.Item2Id);
            if (!currentByTuple.TryGetValue(key, out var list)) {
                list = new List<RouteBarterTask>();
                currentByTuple[key] = list;
            }
            list.Add(task);
        }

        var mapping = new Dictionary<string, string>(StringComparer.Ordinal);
        var ambiguous = new Dictionary<(string, string, string), List<string>>();
        foreach (var saved in savedBarters) {
            if (currentByRow.ContainsKey(saved.RowId)) {
                mapping[saved.RowId] = saved.RowId;
                continue;
            }
            // Try legacy tuple match: "{Island}:{Item1}:{Item2}" and
            // "INVALID:{index}" forms (and any string without a Guid
            // prefix) all map through this branch.
            var tuple = ParseLegacyTuple(saved.RowId);
            if (tuple is null) continue;
            if (!currentByTuple.TryGetValue(tuple.Value, out var candidates)) continue;
            if (candidates.Count > 1) {
                ambiguous.TryGetValue(tuple.Value, out var seenList);
                ambiguous[tuple.Value] = (seenList ?? new List<string>())
                    .Concat(candidates.Select(c => c.RowId))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                continue;
            }
            mapping[saved.RowId] = candidates[0].RowId;
        }
        if (ambiguous.Count > 0) {
            ambiguousTuples = ambiguous;
            return new LegacyRowIdMigrationResult(false, mapping);
        }
        return new LegacyRowIdMigrationResult(true, mapping);
    }

    private static (string IslandId, string Item1Id, string Item2Id)? ParseLegacyTuple(string rowId) {
        // Accept "{Island}:{Item1}:{Item2}" or "{index}:{Island}:{Item1}:{Item2}".
        // Reject anything that already looks like a PlannerRowId
        // (starts with "br-").
        if (string.IsNullOrEmpty(rowId) || rowId.StartsWith("br-", StringComparison.Ordinal))
            return null;
        var parts = rowId.Split(':');
        if (parts.Length == 3)
            return (parts[0], parts[1], parts[2]);
        if (parts.Length == 4)
            return (parts[1], parts[2], parts[3]);
        return null;
    }

    public static bool IsCompatibleAfterProgress(
        AutomaticRoutePlanningRequest currentRequest,
        RoutePlan persistedPlan,
        IReadOnlySet<string> completedBarterRowIds) {
        if (completedBarterRowIds.Count == 0
            || persistedPlan.Status is not (RoutePlanStatus.Optimal
                or RoutePlanStatus.BestKnownWithinLimit)
            || persistedPlan.Routes.Count == 0)
            return false;

        // First try the strict direct match.
        if (IsCompatibleDirect(currentRequest, persistedPlan, completedBarterRowIds))
            return true;

        // Fall back to the legacy migration. A unique (Island, Item1,
        // Item2) match for every saved RowId succeeds; an ambiguous
        // match fails the whole restore so the user is told to
        // regenerate.
        var migration = TryMigrateLegacyRowIds(persistedPlan, currentRequest, out var ambiguous);
        if (ambiguous is { Count: > 0 }) return false;
        if (migration.IsCompatible)
            return IsCompatibleWithMapping(currentRequest, persistedPlan,
                completedBarterRowIds, migration.SavedRowIdToCurrentRowId);
        return false;
    }

    private static bool IsCompatibleDirect(
        AutomaticRoutePlanningRequest currentRequest,
        RoutePlan persistedPlan,
        IReadOnlySet<string> completedBarterRowIds) {
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

        return IsCompatibleBody(currentRequest, persistedPlan, currentByRow, completedBarterRowIds,
            identity: step => step.RowId);
    }

    private static bool IsCompatibleWithMapping(
        AutomaticRoutePlanningRequest currentRequest,
        RoutePlan persistedPlan,
        IReadOnlySet<string> completedBarterRowIds,
        IReadOnlyDictionary<string, string> savedToCurrent) {
        var savedBarters = persistedPlan.Routes
            .SelectMany(route => route.Steps.OfType<BarterStep>())
            .ToArray();
        if (savedBarters.Length == 0) return false;
        var currentTasks = currentRequest.Tasks.ToArray();
        var currentByRow = currentTasks.ToDictionary(task => task.RowId, StringComparer.Ordinal);
        foreach (var saved in savedBarters) {
            if (!savedToCurrent.TryGetValue(saved.RowId, out var mapped))
                return false;
            if (!currentByRow.ContainsKey(mapped)
                && !completedBarterRowIds.Contains(mapped))
                return false;
        }
        if (!savedBarters.Any(step => completedBarterRowIds.Contains(
                savedToCurrent.GetValueOrDefault(step.RowId, ""))))
            return false;
        return IsCompatibleBody(currentRequest, persistedPlan, currentByRow, completedBarterRowIds,
            savedToCurrent);
    }

    private static bool IsCompatibleBody(
        AutomaticRoutePlanningRequest currentRequest,
        RoutePlan persistedPlan,
        IReadOnlyDictionary<string, RouteBarterTask> currentByRow,
        IReadOnlySet<string> completedBarterRowIds,
        IReadOnlyDictionary<string, string>? savedToCurrent = null,
        Func<BarterStep, string>? identity = null) {
        identity ??= step => step.RowId;
        if (!CanReachFirstRemainingBarter(currentRequest, persistedPlan, currentByRow,
                completedBarterRowIds, savedToCurrent, identity))
            return false;
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
        IReadOnlySet<string> completedBarterRowIds,
        IReadOnlyDictionary<string, string>? savedToCurrent = null,
        Func<BarterStep, string>? identity = null) {
        identity ??= step => step.RowId;
        foreach (var route in persistedPlan.Routes) {
            if (!route.Steps.OfType<BarterStep>().Any(step => {
                    var key = identity(step);
                    return currentByRow.ContainsKey(savedToCurrent?.GetValueOrDefault(key, key) ?? key);
                }))
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
                    case BarterStep barter: {
                        var stepKey = identity(barter);
                        var mappedKey = savedToCurrent?.GetValueOrDefault(stepKey, stepKey) ?? stepKey;
                        if (currentByRow.ContainsKey(mappedKey))
                            return onboard.GetValueOrDefault(barter.Consumed.ItemId) >= barter.Consumed.Quantity;
                        if (completedBarterRowIds.Contains(mappedKey)) {
                            int available = onboard.GetValueOrDefault(barter.Consumed.ItemId);
                            if (available < barter.Consumed.Quantity) return false;
                            int afterConsume = available - barter.Consumed.Quantity;
                            if (afterConsume == 0) onboard.Remove(barter.Consumed.ItemId);
                            else onboard[barter.Consumed.ItemId] = afterConsume;
                            onboard[barter.Produced.ItemId] = checked(
                                onboard.GetValueOrDefault(barter.Produced.ItemId) + barter.Produced.Quantity);
                        }
                        break;
                    }
                }
            }
            return false;
        }
        return true;
    }
}