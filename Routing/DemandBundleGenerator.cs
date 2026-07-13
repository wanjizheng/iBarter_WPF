using System.Numerics;

namespace iBarter.Routing;

public sealed record DemandBundle(
    IReadOnlyList<RouteItemQuantity> Items,
    ulong SupportedTaskMask,
    int TotalCargoLT,
    string StableKey);

public static class DemandBundleGenerator {
    public static IReadOnlyList<DemandBundle> Generate(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        string warehouseId,
        ulong remainingMask) {
        if (!state.WarehouseInventory.TryGetValue(warehouseId, out var warehouseStock)) return [];
        if (BitOperations.PopCount(remainingMask) > AutomaticRouteSearchPolicy.ExactTaskLimit)
            return GenerateBounded(request, state, warehouseStock, remainingMask);

        var initialInventory = state.OnBoard.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        var byMask = new Dictionary<ulong, List<VectorState>> {
            [0] = [new VectorState(0, initialInventory, new Dictionary<string, int>(StringComparer.Ordinal))],
        };
        var queue = new Queue<VectorState>(byMask[0]);

        while (queue.Count > 0) {
            var current = queue.Dequeue();
            for (int taskIndex = 0; taskIndex < request.Tasks.Count && taskIndex < 64; taskIndex++) {
                ulong bit = 1UL << taskIndex;
                if ((remainingMask & bit) == 0 || (current.Mask & bit) != 0) continue;
                var task = request.Tasks[taskIndex];
                var inventory = Clone(current.Inventory);
                var required = Clone(current.Required);
                int available = inventory.GetValueOrDefault(task.Item1Id);
                if (available < task.InputQuantity) {
                    int shortage = task.InputQuantity - available;
                    required[task.Item1Id] = required.GetValueOrDefault(task.Item1Id) + shortage;
                    inventory[task.Item1Id] = available + shortage;
                }
                SetQuantity(inventory, task.Item1Id, inventory[task.Item1Id] - task.InputQuantity);
                SetQuantity(inventory, task.Item2Id,
                    inventory.GetValueOrDefault(task.Item2Id) + task.OutputQuantity);
                var next = new VectorState(current.Mask | bit, inventory, required);
                if (AddIfNonDominated(byMask, next)) queue.Enqueue(next);
            }
        }

        var deduplicated = new Dictionary<string, DemandBundle>(StringComparer.Ordinal);
        foreach (var pair in byMask.Where(x => x.Key != 0)) {
            foreach (var vector in pair.Value) {
                var required = vector.Required.Where(x => x.Value > 0)
                    .OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();
                if (required.Length == 0) continue;
                if (required.Any(x => warehouseStock.GetValueOrDefault(x.Key) < x.Value)) continue;

                long cargoLT = state.CargoLT;
                foreach (var item in required) {
                    if (!request.Items.TryGetValue(item.Key, out var definition)) {
                        cargoLT = long.MaxValue;
                        break;
                    }
                    cargoLT += (long)definition.UnitWeight * item.Value;
                }
                if (cargoLT > int.MaxValue || request.ExtraLT + cargoLT > request.TotalLT) continue;

                string key = string.Join(";", required.Select(x => $"{x.Key}:{x.Value}"));
                var bundle = new DemandBundle(
                    required.Select(x => new RouteItemQuantity(x.Key, x.Value)).ToArray(),
                    pair.Key, (int)cargoLT, key);
                if (!deduplicated.TryGetValue(key, out var existing) ||
                    BitOperations.PopCount(bundle.SupportedTaskMask) > BitOperations.PopCount(existing.SupportedTaskMask) ||
                    (BitOperations.PopCount(bundle.SupportedTaskMask) == BitOperations.PopCount(existing.SupportedTaskMask) &&
                     bundle.SupportedTaskMask < existing.SupportedTaskMask))
                    deduplicated[key] = bundle;
            }
        }

        return deduplicated.Values
            .OrderBy(x => x.TotalCargoLT)
            .ThenByDescending(x => BitOperations.PopCount(x.SupportedTaskMask))
            .ThenBy(x => x.StableKey, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<DemandBundle> GenerateBounded(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        IReadOnlyDictionary<string, int> warehouseStock,
        ulong remainingMask) {
        var taskIndexes = Enumerable.Range(0, request.Tasks.Count)
            .Where(i => (remainingMask & (1UL << i)) != 0)
            .OrderBy(i => request.Tasks[i].RowId, StringComparer.Ordinal)
            .ToArray();
        var deduplicated = new Dictionary<string, DemandBundle>(StringComparer.Ordinal);

        foreach (int seed in taskIndexes) {
            var inventory = Clone(state.OnBoard);
            var required = new Dictionary<string, int>(StringComparer.Ordinal);
            ulong supportedMask = 0;
            var unused = new HashSet<int>(taskIndexes);
            int? next = seed;

            while (next is int taskIndex) {
                if (!TryAppendTask(request, state, warehouseStock, taskIndex,
                        inventory, required, out var nextInventory, out var nextRequired))
                    break;

                inventory = nextInventory;
                required = nextRequired;
                unused.Remove(taskIndex);
                supportedMask |= 1UL << taskIndex;
                AddBoundedBundle(request, state, required, supportedMask, deduplicated);

                next = null;
                Dictionary<string, int>? selectedInventory = null;
                Dictionary<string, int>? selectedRequired = null;
                (int NeedsLoad, int AddedLT, string RowId) selectedRank = default;
                bool hasSelection = false;
                foreach (int candidate in unused.OrderBy(i => request.Tasks[i].RowId, StringComparer.Ordinal)) {
                    int available = inventory.GetValueOrDefault(request.Tasks[candidate].Item1Id);
                    if (!TryAppendTask(request, state, warehouseStock, candidate,
                            inventory, required, out var candidateInventory, out var candidateRequired))
                        continue;
                    int addedLT = RequiredLoadLT(request, candidateRequired) - RequiredLoadLT(request, required);
                    var rank = (
                        available >= request.Tasks[candidate].InputQuantity ? 0 : 1,
                        addedLT,
                        request.Tasks[candidate].RowId);
                    if (hasSelection && CompareRank(rank, selectedRank) >= 0) continue;
                    hasSelection = true;
                    next = candidate;
                    selectedInventory = candidateInventory;
                    selectedRequired = candidateRequired;
                    selectedRank = rank;
                }

                // Reuse the already simulated winning state on the next loop by
                // applying it here and marking the task there. This avoids another
                // O(items) simulation while keeping the loop deterministic.
                if (next is int selected) {
                    inventory = selectedInventory!;
                    required = selectedRequired!;
                    unused.Remove(selected);
                    supportedMask |= 1UL << selected;
                    AddBoundedBundle(request, state, required, supportedMask, deduplicated);
                    next = SelectNextTask(request, state, warehouseStock, unused, inventory, required);
                }
            }
        }

        return deduplicated.Values
            .OrderBy(x => x.TotalCargoLT)
            .ThenByDescending(x => BitOperations.PopCount(x.SupportedTaskMask))
            .ThenBy(x => x.StableKey, StringComparer.Ordinal)
            .ToArray();
    }

    private static int? SelectNextTask(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        IReadOnlyDictionary<string, int> warehouseStock,
        IEnumerable<int> unused,
        IReadOnlyDictionary<string, int> inventory,
        IReadOnlyDictionary<string, int> required) {
        int? selected = null;
        (int NeedsLoad, int AddedLT, string RowId) selectedRank = default;
        foreach (int candidate in unused.OrderBy(i => request.Tasks[i].RowId, StringComparer.Ordinal)) {
            int available = inventory.GetValueOrDefault(request.Tasks[candidate].Item1Id);
            if (!TryAppendTask(request, state, warehouseStock, candidate,
                    inventory, required, out _, out var candidateRequired))
                continue;
            var rank = (
                available >= request.Tasks[candidate].InputQuantity ? 0 : 1,
                RequiredLoadLT(request, candidateRequired) - RequiredLoadLT(request, required),
                request.Tasks[candidate].RowId);
            if (selected is not null && CompareRank(rank, selectedRank) >= 0) continue;
            selected = candidate;
            selectedRank = rank;
        }
        return selected;
    }

    private static int CompareRank(
        (int NeedsLoad, int AddedLT, string RowId) left,
        (int NeedsLoad, int AddedLT, string RowId) right) {
        int result = left.NeedsLoad.CompareTo(right.NeedsLoad);
        if (result != 0) return result;
        result = left.AddedLT.CompareTo(right.AddedLT);
        return result != 0 ? result : StringComparer.Ordinal.Compare(left.RowId, right.RowId);
    }

    private static bool TryAppendTask(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        IReadOnlyDictionary<string, int> warehouseStock,
        int taskIndex,
        IReadOnlyDictionary<string, int> inventorySource,
        IReadOnlyDictionary<string, int> requiredSource,
        out Dictionary<string, int> inventory,
        out Dictionary<string, int> required) {
        inventory = Clone(inventorySource);
        required = Clone(requiredSource);
        var task = request.Tasks[taskIndex];
        int available = inventory.GetValueOrDefault(task.Item1Id);
        if (available < task.InputQuantity) {
            int shortage = task.InputQuantity - available;
            int totalRequired = required.GetValueOrDefault(task.Item1Id) + shortage;
            if (warehouseStock.GetValueOrDefault(task.Item1Id) < totalRequired) return false;
            required[task.Item1Id] = totalRequired;
            inventory[task.Item1Id] = available + shortage;
            long pickupTotal = request.ExtraLT + state.CargoLT + RequiredLoadLT(request, required);
            if (pickupTotal > request.TotalLT) return false;
        }

        SetQuantity(inventory, task.Item1Id, inventory[task.Item1Id] - task.InputQuantity);
        SetQuantity(inventory, task.Item2Id,
            inventory.GetValueOrDefault(task.Item2Id) + task.OutputQuantity);
        long afterBarter = request.ExtraLT + InventoryLT(request, inventory);
        return afterBarter <= request.TotalLT;
    }

    private static void AddBoundedBundle(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        IReadOnlyDictionary<string, int> required,
        ulong supportedMask,
        Dictionary<string, DemandBundle> deduplicated) {
        var items = required.Where(x => x.Value > 0)
            .OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();
        if (items.Length == 0) return;
        int cargoLT = checked(state.CargoLT + RequiredLoadLT(request, required));
        string key = string.Join(";", items.Select(x => $"{x.Key}:{x.Value}"));
        var bundle = new DemandBundle(
            items.Select(x => new RouteItemQuantity(x.Key, x.Value)).ToArray(),
            supportedMask, cargoLT, key);
        if (!deduplicated.TryGetValue(key, out var existing) ||
            BitOperations.PopCount(bundle.SupportedTaskMask) > BitOperations.PopCount(existing.SupportedTaskMask) ||
            (BitOperations.PopCount(bundle.SupportedTaskMask) == BitOperations.PopCount(existing.SupportedTaskMask) &&
             bundle.SupportedTaskMask < existing.SupportedTaskMask))
            deduplicated[key] = bundle;
    }

    private static int RequiredLoadLT(
        AutomaticRoutePlanningRequest request,
        IReadOnlyDictionary<string, int> required) => checked(required.Sum(pair =>
            request.Items[pair.Key].UnitWeight * pair.Value));

    private static int InventoryLT(
        AutomaticRoutePlanningRequest request,
        IReadOnlyDictionary<string, int> inventory) => checked(inventory.Sum(pair =>
            request.Items[pair.Key].UnitWeight * pair.Value));

    private static bool AddIfNonDominated(
        Dictionary<ulong, List<VectorState>> byMask,
        VectorState candidate) {
        if (!byMask.TryGetValue(candidate.Mask, out var states)) {
            byMask[candidate.Mask] = [candidate];
            return true;
        }
        if (states.Any(existing => Dominates(existing.Required, candidate.Required))) return false;
        states.RemoveAll(existing => Dominates(candidate.Required, existing.Required));
        states.Add(candidate);
        return true;
    }

    private static bool Dominates(
        IReadOnlyDictionary<string, int> left,
        IReadOnlyDictionary<string, int> right) {
        foreach (string key in left.Keys.Concat(right.Keys).Distinct(StringComparer.Ordinal))
            if (left.GetValueOrDefault(key) > right.GetValueOrDefault(key)) return false;
        return true;
    }

    private static Dictionary<string, int> Clone(IReadOnlyDictionary<string, int> source) =>
        source.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

    private static void SetQuantity(Dictionary<string, int> values, string key, int quantity) {
        if (quantity == 0) values.Remove(key);
        else values[key] = quantity;
    }

    private sealed record VectorState(
        ulong Mask,
        Dictionary<string, int> Inventory,
        Dictionary<string, int> Required);
}
