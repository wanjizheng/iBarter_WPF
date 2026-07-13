namespace iBarter.Routing;

public sealed record RoutePreflightResult(
    bool IsValid,
    IReadOnlyList<RouteDiagnostic> Diagnostics,
    IReadOnlyList<ulong> ProducerMasks);

public static class AutomaticRoutePreflight {
    public static RoutePreflightResult Validate(AutomaticRoutePlanningRequest request) {
        var diagnostics = new List<RouteDiagnostic>();
        if (request.Tasks.Count > 64)
            diagnostics.Add(new RouteDiagnostic("too-many-tasks", Detail: request.Tasks.Count.ToString()));
        if (request.TotalLT <= 0 || request.ExtraLT < 0 || request.ExtraLT > request.TotalLT)
            diagnostics.Add(new RouteDiagnostic("invalid-capacity", Detail: $"{request.ExtraLT}/{request.TotalLT}"));
        if (request.Limits.MaxExpandedStates <= 0 || request.Limits.MaxLocalMoves < 0)
            diagnostics.Add(new RouteDiagnostic("invalid-search-limits"));

        foreach (var duplicate in request.Tasks.GroupBy(x => x.RowId, StringComparer.Ordinal).Where(x => x.Count() > 1))
            diagnostics.Add(new RouteDiagnostic("duplicate-row-id", duplicate.Key));
        foreach (var duplicate in request.Warehouses.GroupBy(x => x.WarehouseId, StringComparer.Ordinal).Where(x => x.Count() > 1))
            diagnostics.Add(new RouteDiagnostic("duplicate-warehouse-id", Detail: duplicate.Key));

        for (int i = 0; i < request.Tasks.Count; i++) {
            var task = request.Tasks[i];
            if (string.IsNullOrWhiteSpace(task.RowId) || string.IsNullOrWhiteSpace(task.IslandId))
                diagnostics.Add(new RouteDiagnostic("invalid-task", task.RowId));
            if (!task.Point.IsFinite)
                diagnostics.Add(new RouteDiagnostic("non-finite-coordinate", task.RowId, Detail: task.IslandId));
            ValidateItem(request, diagnostics, task.RowId, task.Item1Id);
            ValidateItem(request, diagnostics, task.RowId, task.Item2Id);
            if (task.InputQuantity <= 0 || task.OutputQuantity <= 0)
                diagnostics.Add(new RouteDiagnostic("invalid-quantity", task.RowId));

            if (request.Items.TryGetValue(task.Item1Id, out var input) &&
                request.Items.TryGetValue(task.Item2Id, out var output) &&
                task.InputQuantity > 0 && task.OutputQuantity > 0) {
                long inputLT = request.ExtraLT + (long)input.UnitWeight * task.InputQuantity;
                long outputLT = request.ExtraLT + (long)output.UnitWeight * task.OutputQuantity;
                if (inputLT > request.TotalLT || outputLT > request.TotalLT)
                    diagnostics.Add(new RouteDiagnostic("task-overweight", task.RowId,
                        inputLT > request.TotalLT ? task.Item1Id : task.Item2Id,
                        Math.Max(inputLT, outputLT).ToString()));
            }
        }

        foreach (var warehouse in request.Warehouses) {
            if (string.IsNullOrWhiteSpace(warehouse.WarehouseId) || string.IsNullOrWhiteSpace(warehouse.IslandId))
                diagnostics.Add(new RouteDiagnostic("invalid-warehouse", Detail: warehouse.WarehouseId));
            if (!warehouse.Point.IsFinite)
                diagnostics.Add(new RouteDiagnostic("non-finite-coordinate", Detail: warehouse.IslandId));
            foreach (var pair in warehouse.Inventory) {
                if (!request.Items.ContainsKey(pair.Key))
                    diagnostics.Add(new RouteDiagnostic("missing-item", ItemId: pair.Key, Detail: warehouse.WarehouseId));
                if (pair.Value < 0)
                    diagnostics.Add(new RouteDiagnostic("negative-stock", ItemId: pair.Key, Detail: warehouse.WarehouseId));
            }
        }

        var producerMasks = BuildProducerMasks(request);
        AddUnreachableDiagnostics(request, diagnostics);
        var frozenDiagnostics = ModelCopies.List(diagnostics
            .OrderBy(x => x.Code, StringComparer.Ordinal)
            .ThenBy(x => x.RowId, StringComparer.Ordinal)
            .ThenBy(x => x.ItemId, StringComparer.Ordinal)
            .ThenBy(x => x.Detail, StringComparer.Ordinal));
        return new RoutePreflightResult(frozenDiagnostics.Count == 0, frozenDiagnostics, producerMasks);
    }

    private static void ValidateItem(
        AutomaticRoutePlanningRequest request,
        List<RouteDiagnostic> diagnostics,
        string rowId,
        string itemId) {
        if (!request.Items.TryGetValue(itemId, out var item)) {
            diagnostics.Add(new RouteDiagnostic("missing-item", rowId, itemId));
            return;
        }
        // Misc rewards such as Crow Coin use level -1 but have zero cargo
        // weight. Route feasibility depends on the canonical unit weight, not
        // on whether the catalog classifies an item as a barter tier.
        if (item.UnitWeight < 0)
            diagnostics.Add(new RouteDiagnostic("invalid-item", rowId, itemId));
    }

    private static IReadOnlyList<ulong> BuildProducerMasks(AutomaticRoutePlanningRequest request) {
        var masks = new ulong[request.Tasks.Count];
        for (int consumer = 0; consumer < request.Tasks.Count; consumer++) {
            ulong mask = 0;
            for (int producer = 0; producer < Math.Min(request.Tasks.Count, 64); producer++) {
                if (request.Tasks[producer].Item2Id == request.Tasks[consumer].Item1Id)
                    mask |= 1UL << producer;
            }
            masks[consumer] = mask;
        }
        return Array.AsReadOnly(masks);
    }

    private static void AddUnreachableDiagnostics(
        AutomaticRoutePlanningRequest request,
        List<RouteDiagnostic> diagnostics) {
        var available = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var warehouse in request.Warehouses)
            foreach (var pair in warehouse.Inventory.Where(x => x.Value > 0))
                available[pair.Key] = available.GetValueOrDefault(pair.Key) + pair.Value;

        var reachable = new bool[request.Tasks.Count];
        bool changed;
        do {
            changed = false;
            for (int i = 0; i < request.Tasks.Count; i++) {
                if (reachable[i]) continue;
                var task = request.Tasks[i];
                if (task.InputQuantity <= 0 || task.OutputQuantity <= 0) continue;
                if (available.GetValueOrDefault(task.Item1Id) < task.InputQuantity) continue;
                reachable[i] = true;
                available[task.Item2Id] = available.GetValueOrDefault(task.Item2Id) + task.OutputQuantity;
                changed = true;
            }
        } while (changed);

        for (int i = 0; i < reachable.Length; i++)
            if (!reachable[i])
                diagnostics.Add(new RouteDiagnostic(
                    "unreachable-input", request.Tasks[i].RowId, request.Tasks[i].Item1Id));
    }
}
