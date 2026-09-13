namespace iBarter.Routing;

public sealed record ManualCargoStepInput(
    string RowId,
    string IslandId,
    string Item1Id,
    int InputQuantity,
    int Item1UnitWeight,
    string Item2Id,
    int OutputQuantity,
    int Item2UnitWeight);

public sealed record ManualCargoProjectedStep(
    ManualCargoStepInput Step,
    RouteLoadSnapshot Load);

public sealed record ManualCargoProjection(
    IReadOnlyList<ManualCargoProjectedStep> Steps,
    int InitialLT,
    int CurrentLT,
    int PeakLT);

/// <summary>
/// Projects a manually ordered barter list without inventing warehouse stops.
/// The first replay finds the minimum external inventory required by that exact
/// order; the second replay produces the load visible after every barter.
/// </summary>
public static class ManualCargoProjector {
    public static ManualCargoProjection Project(
        IReadOnlyList<ManualCargoStepInput> steps,
        int extraLT) {
        if (extraLT < 0) throw new ArgumentOutOfRangeException(nameof(extraLT));

        var weights = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var step in steps) {
            Validate(step);
            AddWeight(weights, step.Item1Id, step.Item1UnitWeight);
            AddWeight(weights, step.Item2Id, step.Item2UnitWeight);
        }

        var required = new Dictionary<string, int>(StringComparer.Ordinal);
        var discoveryInventory = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var step in steps) {
            int available = discoveryInventory.GetValueOrDefault(step.Item1Id);
            if (available < step.InputQuantity) {
                int shortage = step.InputQuantity - available;
                AddQuantity(required, step.Item1Id, shortage);
                AddQuantity(discoveryInventory, step.Item1Id, shortage);
            }
            AddQuantity(discoveryInventory, step.Item1Id, -step.InputQuantity);
            AddQuantity(discoveryInventory, step.Item2Id, step.OutputQuantity);
        }

        var onboard = required.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        int initial = checked(extraLT + CargoLT(onboard, weights));
        int peak = initial;
        var projected = new List<ManualCargoProjectedStep>(steps.Count);
        foreach (var step in steps) {
            int available = onboard.GetValueOrDefault(step.Item1Id);
            if (available < step.InputQuantity)
                throw new InvalidOperationException($"Manual cargo projection is missing '{step.Item1Id}'.");
            AddQuantity(onboard, step.Item1Id, -step.InputQuantity);
            AddQuantity(onboard, step.Item2Id, step.OutputQuantity);
            int cargoLT = CargoLT(onboard, weights);
            int total = checked(extraLT + cargoLT);
            peak = Math.Max(peak, total);
            projected.Add(new ManualCargoProjectedStep(
                step,
                new RouteLoadSnapshot(cargoLT, total, peak)));
        }

        int current = projected.Count == 0 ? extraLT : projected[^1].Load.TotalWithExtraLT;
        return new ManualCargoProjection(projected.ToArray(), initial, current, peak);
    }

    private static void Validate(ManualCargoStepInput step) {
        if (string.IsNullOrWhiteSpace(step.RowId)
            || string.IsNullOrWhiteSpace(step.IslandId)
            || string.IsNullOrWhiteSpace(step.Item1Id)
            || string.IsNullOrWhiteSpace(step.Item2Id)
            || step.InputQuantity <= 0
            || step.OutputQuantity < 0
            || step.Item1UnitWeight < 0
            || step.Item2UnitWeight < 0)
            throw new ArgumentException("Manual cargo step contains invalid item data.", nameof(step));
    }

    private static void AddWeight(Dictionary<string, int> weights, string itemId, int unitWeight) {
        if (weights.TryGetValue(itemId, out int existing) && existing != unitWeight)
            throw new ArgumentException($"Item '{itemId}' has inconsistent unit weights.");
        weights[itemId] = unitWeight;
    }

    private static void AddQuantity(Dictionary<string, int> inventory, string itemId, int delta) {
        int quantity = checked(inventory.GetValueOrDefault(itemId) + delta);
        if (quantity < 0) throw new InvalidOperationException($"Item '{itemId}' became negative.");
        if (quantity == 0) inventory.Remove(itemId);
        else inventory[itemId] = quantity;
    }

    private static int CargoLT(
        IReadOnlyDictionary<string, int> inventory,
        IReadOnlyDictionary<string, int> weights) => checked(inventory.Sum(pair =>
            checked(pair.Value * weights[pair.Key])));
}
