namespace iBarter.Routing;

/// <summary>
/// Builds the interaction identity for an automatic-route step label.
/// The visual wrapper keeps <see cref="RouteStepMapLabel"/> in its Tag
/// for positioning, while this node is placed in DataContext for the
/// existing map click handlers.  Only a real BarterStep with a stable
/// PlannerRowId is allowed to complete a Planner row.
/// </summary>
public static class RouteStepLabelInteraction {
    public static RouteMapNode CreateNode(RouteStepMapLabel label) {
        bool canMarkDone = label.StepKind == RouteStepKind.Barter
            && !label.IsWarehouseOperation
            && !string.IsNullOrWhiteSpace(label.BarterRowId);
        return new RouteMapNode(
            label.RouteNumber,
            label.StepIndex,
            label.StepKind,
            label.IslandId,
            label.BarterRowId,
            canMarkDone,
            label.DisplayText,
            label.DisplayText);
    }
}
