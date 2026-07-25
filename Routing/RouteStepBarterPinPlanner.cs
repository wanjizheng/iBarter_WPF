namespace iBarter.Routing;

/// <summary>
/// Describes the small coloured square rendered at the physical barter stop for
/// one automatic-route <see cref="BarterStep"/>.  It deliberately shares the
/// route-step identity and Planner barter group with <see cref="RouteStepMapLabel"/>
/// so the square and the transaction text cannot drift apart.
/// </summary>
public sealed record RouteStepBarterPin(
    string Identity,
    string IslandId,
    int? BarterGroup,
    string DisplayText);

public static class RouteStepBarterPinPlanner {
    /// <summary>
    /// Produce pins only for real barter operations. Warehouse pickup/unload
    /// labels and any future non-barter step kinds must never create a coloured
    /// trade square.
    /// </summary>
    public static IReadOnlyList<RouteStepBarterPin> Plan(
        IReadOnlyList<RouteStepMapLabel> labels) {
        var result = new List<RouteStepBarterPin>();
        foreach (RouteStepMapLabel label in labels) {
            if (label.IsWarehouseOperation || label.StepKind != RouteStepKind.Barter)
                continue;
            result.Add(new RouteStepBarterPin(
                label.Identity,
                label.IslandId,
                label.BarterGroup,
                label.DisplayText));
        }
        return result;
    }
}
