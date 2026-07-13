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
