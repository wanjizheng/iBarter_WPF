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

        var unfinished = rows.Where(r => !r.ExchangeDone).ToList();
        var unfinishedRoutes = unfinished.Select(r => r.Route).ToList();

        var request = new AutoPlanningRequest(
            unfinishedRoutes, inventory, strategy, lv5Target, lv6Target, budget);

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

        return new PlannerCalculation(new PlannerApplySet(multipliers), result.Diagnostics, result.UsedParley);
    }
}