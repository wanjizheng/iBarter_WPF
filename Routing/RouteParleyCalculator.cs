namespace iBarter.Routing;

/// <summary>
/// Computes the remaining parley cost for a planner state.
///
/// <para>Bug 4 root cause: the legacy <c>UpdateParley</c> summed every row whose
/// <c>ExchangeQuantity &gt; 0</c>, including rows whose <c>ExchangeDone</c>
/// was already true. A ticked CK therefore left the displayed number unchanged,
/// giving the impression that completion didn't update the budget.</para>
///
/// <para>Audit round 2 (6): the v1 calculator additionally took a
/// <c>completedTimes</c> lookup that subtracted from <c>ExchangeQuantity</c>.
/// That was wrong on two counts: <c>ExchangeQuantity</c> already represents
/// the REMAINING planned executions of a row (it is clamped by
/// <c>IslandRemaining</c> and overwritten by the Auto Plan
/// <c>Multipliers</c> map); and there is no separate persistent
/// <c>CompletedExecutions</c> field on the model — UI counters and
/// persistence keys are only <c>ExchangeDone</c> + <c>ExchangeQuantity</c>.
/// Mixing a UI-cached <c>completedTimes</c> into the authoritative formula
/// reintroduced the drift this class was supposed to eliminate.</para>
///
/// <para>The contract is now a strict <c>Σ</c> over rows where
/// <c>!ExchangeDone</c>.  Every entry point that mutates a barter
/// (Planner CK, map double-click, Planner reload, Auto Plan, multiplier
/// edit) MUST call this once and assign the result back to the bound label.
/// Repeated calls with the same state are idempotent — same input, same output.</para>
/// </summary>
public static class RouteParleyCalculator {
    /// <summary>
    /// Parley cost of a single exchange of <paramref name="row"/>.
    /// Mirrors the formula used by <c>GetEffectiveParley</c> in
    /// PlannerControl.xaml.cs so the displayed number and the planner's
    /// budget constraint stay byte-for-byte aligned.
    /// </summary>
    public static int SingleExchangeCost(IRouteParleyRow row) {
        if (row is null) return 0;
        return row.Parley;
    }

    /// <summary>
    /// Authoritative remaining parley cost.
    /// <code>
    /// remaining = Σ rows where !ExchangeDone
    ///              × ExchangeQuantity            (already represents
    ///                                            the remaining planned
    ///                                            executions; do NOT
    ///                                            subtract a counter)
    ///              × singleExchangeCost(row)
    /// </code>
    /// Rows with <c>ExchangeQuantity &lt;= 0</c> are ignored because the user
    /// explicitly disabled them in the planner grid.
    /// </summary>
    public static long CalculateRemaining(IEnumerable<IRouteParleyRow> rows) {
        if (rows is null) return 0;
        long total = 0;
        foreach (var row in rows) {
            if (row is null) continue;
            if (row.ExchangeDone) continue;
            if (row.ExchangeQuantity <= 0) continue;
            total += (long)SingleExchangeCost(row) * row.ExchangeQuantity;
        }
        return total;
    }
}

/// <summary>
/// Read-only view of a Planner row that <see cref="RouteParleyCalculator"/>
/// needs to compute remaining parley. The Planner's <c>Barter</c> class
/// implements this implicitly via its existing properties.
/// </summary>
public interface IRouteParleyRow {
    string RowId { get; }
    int Parley { get; }
    int ExchangeQuantity { get; }
    bool ExchangeDone { get; }
}