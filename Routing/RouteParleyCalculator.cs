namespace iBarter.Routing;

/// <summary>
/// Computes the remaining parley cost for a planner state.
///
/// <para>Bug 4 root cause: the legacy <c>UpdateParley</c> summed every row whose
/// <c>ExchangeQuantity &gt; 0</c>, including rows whose <c>ExchangeDone</c> was
/// already true. A ticked CK therefore left the displayed number unchanged,
/// giving the impression that completion didn't update the budget.</para>
///
/// <para>The authoritative formula below is derived purely from the current
/// Planner state (no UI caches, no incremental <c>displayedParley -= completedRow.Parley</c>).
/// Every entry point that mutates a barter — Planner CK, map double-click,
/// Planner reload, Auto Plan, multiplier edit — must call this once and assign
/// the result back to the bound label.</para>
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
    /// Computes the authoritative remaining parley cost.
    /// <code>
    /// remaining = Σ rows where !ExchangeDone
    ///              × max(0, ExchangeQuantity - alreadyCompletedTimes)
    ///              × singleExchangeCost(row)
    /// </code>
    /// The <paramref name="completedTimes"/> lookup lets the planner account
    /// for repeated exchanges whose ExchangeQuantity counts further work
    /// beyond a single CK tick (currently always 0, but reserved for future
    /// partial-completion UX). Rows with <c>ExchangeQuantity &lt;= 0</c> are
    /// ignored because the user explicitly disabled them in the planner grid.
    /// </summary>
    public static long CalculateRemaining(
        IEnumerable<IRouteParleyRow> rows,
        IReadOnlyDictionary<string, int>? completedTimes = null) {
        if (rows is null) return 0;
        long total = 0;
        foreach (var row in rows) {
            if (row is null) continue;
            if (row.ExchangeDone) continue;
            if (row.ExchangeQuantity <= 0) continue;
            int doneTimes = completedTimes?.TryGetValue(row.RowId, out var c) == true ? c : 0;
            int remainingTimes = Math.Max(0, row.ExchangeQuantity - doneTimes);
            if (remainingTimes == 0) continue;
            total += (long)SingleExchangeCost(row) * remainingTimes;
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