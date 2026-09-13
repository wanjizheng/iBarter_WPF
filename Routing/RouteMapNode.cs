namespace iBarter.Routing;

/// <summary>
/// Identifies a route step's role in the planning pipeline so the map
/// can render and authorize double-click without consulting the
/// grid's <c>Name</c> suffix as its primary source of truth.
///
/// <para>Audit round 2 (3): the v1 handler used
/// <c>clickedGrid.Name.EndsWith("Warehouse" or "Temp")</c> as its
/// authorization condition. That works only as long as every
/// non-barter grid is named with that suffix — a fragile coupling
/// between XAML naming convention and double-click semantics.
/// This record carries the step's identity on the visual element's
/// Tag/DataContext so the handler can pattern-match on StepKind
/// and BarterRowId directly, regardless of how the WPF tree is
/// laid out.</para>
/// </summary>
public sealed record RouteMapNode(
    int RouteNumber,
    int StepIndex,
    RouteStepKind StepKind,
    string IslandId,
    string? BarterRowId,
    bool CanMarkDone,
    string DisplayTitle,
    string DisplayDetail) {
    /// <summary>
    /// The only condition under which the map may complete a barter
    /// via double-click. Pickup and unload nodes MUST return false.
    /// </summary>
    public bool AuthorizesCompletion =>
        StepKind == RouteStepKind.Barter
        && !string.IsNullOrWhiteSpace(BarterRowId)
        && CanMarkDone;
}

public enum RouteStepKind {
    Pickup,
    Barter,
    Unload,
}