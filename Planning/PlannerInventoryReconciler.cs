namespace iBarter.Planning;

public sealed record PlannerWarehouseInventory(
    string ItemId,
    int Velia,
    int Iliya,
    int Epheria,
    int Ancado) {
    public int Total => checked(Velia + Iliya + Epheria + Ancado);
}

public sealed record PlannerInventoryExchange(
    string RowId,
    string Item1Id,
    int Item1Number,
    string Item2Id,
    int Item2Number,
    int ExchangeQuantity);

public sealed record PlannerInventoryError(string Code, string ItemId, string RowId = "");

public sealed record PlannerInventoryReconciliation(
    bool Success,
    IReadOnlyDictionary<string, PlannerWarehouseInventory> Inventory,
    IReadOnlyList<PlannerInventoryError> Errors);

/// <summary>
/// Applies a complete Planner run to storage as one transaction. Quantities
/// are netted by stable ItemID before any warehouse is mutated.
/// </summary>
public sealed class PlannerInventoryReconciler {
    public PlannerInventoryReconciliation Reconcile(
        IEnumerable<PlannerWarehouseInventory> currentInventory,
        IEnumerable<PlannerInventoryExchange> exchanges,
        int defaultWarehouseIndex,
        ISet<string>? ignoredOutputItemIds = null,
        ISet<string>? ignoredInputItemIds = null) {
        ArgumentNullException.ThrowIfNull(currentInventory);
        ArgumentNullException.ThrowIfNull(exchanges);

        if (defaultWarehouseIndex is < 0 or > 3) {
            defaultWarehouseIndex = 0;
        }

        ignoredOutputItemIds ??= new HashSet<string>(StringComparer.Ordinal);
        ignoredInputItemIds ??= new HashSet<string>(StringComparer.Ordinal);
        var errors = new List<PlannerInventoryError>();
        var inventory = new Dictionary<string, PlannerWarehouseInventory>(StringComparer.Ordinal);

        foreach (var item in currentInventory) {
            if (string.IsNullOrWhiteSpace(item.ItemId)
                || item.Velia < 0 || item.Iliya < 0 || item.Epheria < 0 || item.Ancado < 0) {
                errors.Add(new PlannerInventoryError("INVALID_INVENTORY", item.ItemId));
                continue;
            }
            if (!inventory.TryAdd(item.ItemId, item)) {
                errors.Add(new PlannerInventoryError("DUPLICATE_ITEM", item.ItemId));
            }
        }

        var deltas = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var exchange in exchanges) {
            if (exchange.ExchangeQuantity < 0 || exchange.Item1Number <= 0 || exchange.Item2Number <= 0
                || string.IsNullOrWhiteSpace(exchange.Item1Id) || string.IsNullOrWhiteSpace(exchange.Item2Id)) {
                errors.Add(new PlannerInventoryError("INVALID_EXCHANGE", exchange.Item1Id, exchange.RowId));
                continue;
            }
            if (exchange.ExchangeQuantity == 0) {
                continue;
            }

            try {
                if (!ignoredInputItemIds.Contains(exchange.Item1Id)) {
                    AddDelta(deltas, exchange.Item1Id,
                        checked(-(long)exchange.ExchangeQuantity * exchange.Item1Number));
                }
                if (!ignoredOutputItemIds.Contains(exchange.Item2Id)) {
                    AddDelta(deltas, exchange.Item2Id,
                        checked((long)exchange.ExchangeQuantity * exchange.Item2Number));
                }
            }
            catch (OverflowException) {
                errors.Add(new PlannerInventoryError("QUANTITY_OVERFLOW", exchange.Item1Id, exchange.RowId));
            }
        }

        if (errors.Count > 0) {
            return Failure(inventory, errors);
        }

        var result = inventory.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var (itemId, delta) in deltas.OrderBy(pair => pair.Key, StringComparer.Ordinal)) {
            if (!result.TryGetValue(itemId, out var item)) {
                errors.Add(new PlannerInventoryError("MISSING_STORAGE_ITEM", itemId));
                continue;
            }

            long targetTotal = (long)item.Total + delta;
            if (targetTotal < 0) {
                errors.Add(new PlannerInventoryError("INSUFFICIENT_STOCK", itemId));
                continue;
            }
            if (targetTotal > int.MaxValue) {
                errors.Add(new PlannerInventoryError("QUANTITY_OVERFLOW", itemId));
                continue;
            }

            result[itemId] = delta < 0
                ? Deduct(item, checked((int)-delta))
                : Add(item, checked((int)delta), defaultWarehouseIndex);
        }

        return errors.Count == 0
            ? new PlannerInventoryReconciliation(true, result, Array.Empty<PlannerInventoryError>())
            : Failure(inventory, errors);
    }

    private static void AddDelta(IDictionary<string, long> deltas, string itemId, long delta) {
        deltas.TryGetValue(itemId, out long current);
        deltas[itemId] = checked(current + delta);
    }

    private static PlannerWarehouseInventory Deduct(PlannerWarehouseInventory item, int amount) {
        int[] quantities = [item.Velia, item.Iliya, item.Epheria, item.Ancado];
        int remaining = amount;
        while (remaining > 0) {
            int warehouse = Enumerable.Range(0, quantities.Length)
                .OrderByDescending(index => quantities[index])
                .ThenBy(index => index)
                .First();
            int taken = Math.Min(remaining, quantities[warehouse]);
            quantities[warehouse] -= taken;
            remaining -= taken;
        }
        return new PlannerWarehouseInventory(item.ItemId, quantities[0], quantities[1], quantities[2], quantities[3]);
    }

    private static PlannerWarehouseInventory Add(PlannerWarehouseInventory item, int amount, int defaultWarehouseIndex) {
        if (amount == 0) {
            return item;
        }
        int[] quantities = [item.Velia, item.Iliya, item.Epheria, item.Ancado];
        int target = quantities.Max() > 0
            ? Enumerable.Range(0, quantities.Length)
                .OrderByDescending(index => quantities[index])
                .ThenBy(index => index)
                .First()
            : defaultWarehouseIndex;
        quantities[target] = checked(quantities[target] + amount);
        return new PlannerWarehouseInventory(item.ItemId, quantities[0], quantities[1], quantities[2], quantities[3]);
    }

    private static PlannerInventoryReconciliation Failure(
        IReadOnlyDictionary<string, PlannerWarehouseInventory> original,
        IReadOnlyList<PlannerInventoryError> errors) =>
        new(false,
            original.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            errors.ToArray());
}
