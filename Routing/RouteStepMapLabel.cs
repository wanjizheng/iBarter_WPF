namespace iBarter.Routing;

/// <summary>
/// Audit round 6 (regression fix): the v1 label system collapsed every
/// label onto a single <c>HashSet&lt;IslandId&gt;</c>, which meant a
/// BarterStep on an island with two barters showed only the bare
/// island name — no input/output, no quantity, and the second
/// barter's identity was destroyed.  This descriptor carries the
/// minimum data the WPF layer needs to render and identify one
/// real route step:
///   * <see cref="RouteNumber"/> + <see cref="StepIndex"/>: identity
///     inside the plan; survives reorder/ShowAll/manual-mode
///     transitions and is independent of the island or the bar text;
///   * <see cref="StepKind"/>: drives the format and the
///     "no CK authority" contract;
///   * <see cref="IslandId"/>: used for positioning and for the
///     "no longer in the plan" sweep;
///   * <see cref="BarterRowId"/>: only set for <c>Barter</c>
///     steps, lets the WPF layer link the visible label back to the
///     exact <c>BarterStep</c> without an IslandId lookup;
///   * <see cref="DisplayText"/>: the actual rendered text
///     (already formatted, see <see cref="RouteStepLabelPlanner"/>);
///   * <see cref="IsWarehouseOperation"/>: true for the
///     pickup / unload steps so the WPF layer can apply a
///     distinct colour and dedup them by island without flattening
///     the BarterStep labels on the same island.
/// </summary>
public sealed record RouteStepMapLabel(
    int RouteNumber,
    int StepIndex,
    RouteStepKind StepKind,
    string IslandId,
    string? BarterRowId,
    string DisplayText,
    bool IsWarehouseOperation) {
    /// <summary>
    /// Stable identity across reconcile + reposition cycles. The
    /// audit forbids collapsing multiple steps on the same island
    /// into a single <c>HashSet&lt;IslandId&gt;</c> entry — two
    /// BarterSteps at indices 2 and 5 on "Iliya" get two distinct
    /// identities here even though their IslandId matches.
    /// </summary>
    public string Identity => $"r{RouteNumber}s{StepIndex}";
}