namespace iBarter.Planning;

/// <summary>
/// Immutable snapshot of a single planner row used by <see cref="PlannerAutoPlanningAdapter"/>.
/// Carries enough state that the planner can run from a zero baseline for unfinished
/// rows without ever mutating the live WPF bindings.
/// </summary>
public sealed record PlannerRowSnapshot(
    string RowId,
    bool ExchangeDone,
    int ExistingMultiplier,
    AutoPlanningRoute Route);

/// <summary>
/// Result of an Auto Plan click. <see cref="ApplySet"/> is null when the planner
/// reported a hard failure (e.g. duplicate row ids, invalid request). When non-null,
/// the dictionary contains an entry for every row id passed to
/// <see cref="Calculate"/> — CK rows keep their <see cref="PlannerRowSnapshot.ExistingMultiplier"/>
/// and unfinished rows are replaced from the planner's fresh zero-baseline result.
/// </summary>
public sealed record PlannerApplySet(IReadOnlyDictionary<string, int> Multipliers);

public sealed record PlannerCalculation(
    PlannerApplySet? ApplySet,
    IReadOnlyList<AutoPlanningDiagnostic> Diagnostics,
    // Total parley represented by the resulting Planner: preserved completed
    // exchanges plus newly selected unfinished exchanges.
    int UsedParley);

/// <summary>
/// Non-WPF orchestration boundary between the live <see cref="iBarter.Barter"/>
/// collection and the deterministic <see cref="PlannerAutoPlanner"/> service. The
/// adapter is responsible for:
///   1. Filtering finished (CK) rows out of the planner request and preserving their
///      existing multipliers in the apply set.
///   2. Building the planner's immutable <see cref="AutoPlanningRequest"/> from the
///      snapshot list (never mutating the live rows).
///   3. Translating a planner success into a complete multiplier dictionary the WPF
///      layer can apply in one shot.
/// </summary>
public sealed class PlannerAutoPlanningAdapter {
    private readonly PlannerAutoPlanner _planner = new();

    public PlannerCalculation Calculate(
        IReadOnlyList<PlannerRowSnapshot> rows,
        IReadOnlyDictionary<string, int> inventory,
        AutoPlanningStrategy strategy,
        int lv5Target,
        int lv6Target,
        int budget) {

        if (strategy == AutoPlanningStrategy.ManualSelection)
            return PreserveManualSelection(rows);

        long completedParley = 0;
        foreach (var row in rows.Where(row => row.ExchangeDone && row.ExistingMultiplier > 0)) {
            if (row.Route.Parley < 0)
                return ManualFailure("invalid-completed-parley", row.RowId);
            completedParley = checked(completedParley +
                (long)row.ExistingMultiplier * row.Route.Parley);
        }
        if (completedParley > Int32.MaxValue)
            return ManualFailure("completed-parley-overflow");

        // The daily parley budget covers the whole plan, including exchanges
        // which the player has already completed.  Those rows remain immutable;
        // only the unspent remainder is available for fresh selection.
        int remainingBudget = budget;
        if (budget >= 0 && budget <= 1_000_000) {
            remainingBudget = completedParley >= budget
                ? 0
                : budget - (int)completedParley;
        }

        var unfinished = rows.Where(r => !r.ExchangeDone).ToList();
        var unfinishedRoutes = unfinished.Select(r => r.Route).ToList();
        var availableInventory = BuildAvailableInventoryAfterCompletedExchanges(
            rows, inventory, out var carryOverInventory);

        var request = new AutoPlanningRequest(
            unfinishedRoutes,
            availableInventory,
            strategy,
            lv5Target,
            lv6Target,
            remainingBudget,
            carryOverInventory);

        var result = _planner.Plan(request);

        // Hard failure: surface the diagnostics and let the WPF layer keep every row
        // untouched. Returning ApplySet=null is the atomic-application contract — the
        // UI must not partially apply multipliers when the planner rejects the input.
        if (!result.Success) {
            return new PlannerCalculation(null, result.Diagnostics, result.UsedParley);
        }

        // Build a complete multiplier map: every requested row id (CK or unfinished)
        // gets an entry. CK rows keep their snapshot value; unfinished rows use the
        // planner's fresh zero-baseline result or zero when the plan never touched
        // them.
        var multipliers = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in rows) {
            if (r.ExchangeDone) {
                multipliers[r.RowId] = r.ExistingMultiplier;
            }
            else if (result.Multipliers.TryGetValue(r.RowId, out var m)) {
                multipliers[r.RowId] = m;
            }
            else {
                multipliers[r.RowId] = 0;
            }
        }

        int totalUsedParley = checked((int)completedParley + result.UsedParley);
        return new PlannerCalculation(
            new PlannerApplySet(multipliers),
            result.Diagnostics,
            totalUsedParley);
    }

    /// <summary>
    /// Completed rows are excluded from future route selection, but their net
    /// result is physically present in the ship's cargo. Preserve that result
    /// in the planning inventory so the next unfinished exchange can consume
    /// it. Positive completed balance is also returned separately as carry-over
    /// cargo: it is spendable before LV5/LV6 warehouse reserve is considered.
    /// </summary>
    private static IReadOnlyDictionary<string, int> BuildAvailableInventoryAfterCompletedExchanges(
        IReadOnlyList<PlannerRowSnapshot> rows,
        IReadOnlyDictionary<string, int> inventory,
        out IReadOnlyDictionary<string, int> carryOverInventory) {
        var available = new Dictionary<string, int>(inventory, StringComparer.Ordinal);
        var completedBalance = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var row in rows.Where(row => row.ExchangeDone && row.ExistingMultiplier > 0)) {
            AddBalance(
                completedBalance,
                row.Route.Item1Id,
                -(long)row.ExistingMultiplier * row.Route.Item1Number);
            AddBalance(
                completedBalance,
                row.Route.Item2Id,
                (long)row.ExistingMultiplier * row.Route.Item2Number);
        }

        var carried = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (itemId, change) in completedBalance) {
            long existing = available.TryGetValue(itemId, out int value) ? value : 0;
            available[itemId] = ClampToInventoryRange(existing + change);
            if (change > 0) carried[itemId] = ClampToInventoryRange(change);
        }

        carryOverInventory = carried;
        return available;
    }

    private static void AddBalance(
        Dictionary<string, long> balance,
        string itemId,
        long amount) {
        if (string.IsNullOrWhiteSpace(itemId) || amount == 0) return;
        balance[itemId] = balance.TryGetValue(itemId, out long current)
            ? checked(current + amount)
            : amount;
    }

    private static int ClampToInventoryRange(long value) =>
        value <= 0 ? 0 : value >= Int32.MaxValue ? Int32.MaxValue : (int)value;

    private static PlannerCalculation PreserveManualSelection(
        IReadOnlyList<PlannerRowSnapshot> rows) {
        var multipliers = new Dictionary<string, int>(StringComparer.Ordinal);
        long usedParley = 0;
        foreach (var row in rows) {
            if (!multipliers.TryAdd(row.RowId, row.ExistingMultiplier))
                return ManualFailure("duplicate-row-id", row.RowId);
            if (row.ExistingMultiplier < 0)
                return ManualFailure("manual-negative-multiplier", row.RowId);
            if (!row.ExchangeDone && row.ExistingMultiplier > 0)
                usedParley += (long)row.ExistingMultiplier * row.Route.Parley;
        }
        if (usedParley > Int32.MaxValue)
            return ManualFailure("manual-parley-overflow");
        return new PlannerCalculation(
            new PlannerApplySet(multipliers),
            [],
            (int)usedParley);
    }

    private static PlannerCalculation ManualFailure(string code, string rowId = "") =>
        new(null, [new AutoPlanningDiagnostic(code, rowId)], 0);
}
