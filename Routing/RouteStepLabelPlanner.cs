namespace iBarter.Routing;

/// <summary>
/// Audit round 6: produces one <see cref="RouteStepMapLabel"/> per
/// real <see cref="RouteStep"/> in the visible routes.  Every label
/// has its own identity (RouteNumber + StepIndex), so two
/// BarterSteps on the same island get two distinct labels with
/// different content.  The BarterStep text is built directly from
/// the step's Consumed / Produced — never from any Planner-barter
/// lookup keyed by IslandId / IslandName.  Warehouse steps
/// (pickup / unload) produce the localized
/// "<c>{岛} · 装货/卸货</c>" text and are flagged
/// <c>IsWarehouseOperation = true</c> so the WPF layer can dedup
/// them by island without flattening real BarterStep labels on the
/// same island.
/// </summary>
public static class RouteStepLabelPlanner {
    /// <summary>
    /// Format used for warehouse steps.  Centralised here so a
    /// future localization change is a one-line edit.
    /// </summary>
    public static string FormatWarehouseText(string islandDisplay, bool isPickup) =>
        isPickup
            ? $"{islandDisplay} · 装货"
            : $"{islandDisplay} · 卸货";

    /// <summary>
    /// Format used for BarterSteps.  Carries the input and output
    /// item names + quantities, exactly as the user sees on the
    /// Planner.  No <c>IslandId</c> derivation — the BarterStep's
    /// own <c>Consumed</c>/<c>Produced</c> are the only sources.
    /// </summary>
    public static string FormatBarterText(
        string item1Display,
        int consumedQuantity,
        string item2Display,
        int producedQuantity) =>
        $"{item1Display} × {consumedQuantity} → {item2Display} × {producedQuantity}";

    /// <summary>
    /// Builds the per-step labels for the visible routes. Returns
    /// one entry per real <see cref="RouteStep"/> with non-empty
    /// <c>IslandId</c>; steps with empty IslandId (degenerate
    /// entries) are skipped because the WPF layer has nowhere to
    /// position them.
    /// </summary>
    /// <param name="plan">Current route plan, or null for "no
    /// plan".</param>
    /// <param name="showAll">True = every route, false = only
    /// <paramref name="selectedRouteNumber"/>.</param>
    /// <param name="selectedRouteNumber">Used when
    /// <paramref name="showAll"/> is false.</param>
    /// <param name="itemDisplayNames">Lookup keyed by ItemId
    /// (e.g. <c>App.listItems</c>) for human-readable item
    /// names.  Tests can pass an empty dictionary to fall back
    /// to the raw ItemId.</param>
    public static IReadOnlyList<RouteStepMapLabel> PlanLabels(
        RoutePlan? plan,
        bool showAll,
        int? selectedRouteNumber,
        IReadOnlyDictionary<string, string> itemDisplayNames,
        IReadOnlyDictionary<string, int>? barterGroupsByRowId = null,
        IReadOnlySet<string>? completedBarterRowIds = null) {
        if (plan is null) return Array.Empty<RouteStepMapLabel>();
        var result = new List<RouteStepMapLabel>();
        foreach (var route in plan.Routes) {
            if (!showAll && route.Number != selectedRouteNumber) continue;
            for (int stepIndex = 0; stepIndex < route.Steps.Count; stepIndex++) {
                var step = route.Steps[stepIndex];
                if (string.IsNullOrEmpty(step.IslandId)) continue;
                // Audit round 7: EnsureAutomaticWarehouseNodes is the
                // single owner of pickup / unload labels (the
                // "伊利亚岛 · 装货" / "伊利亚岛 · 卸货" text). The
                // step-label pipeline renders ONLY BarterStep labels
                // — those need a RouteNumber + StepIndex identity and
                // show the actual trade. Mixing the two would stack
                // a second "装货" text on top of the warehouse
                // marker, which is exactly the v1 bug the audit
                // closed.
                if (step is WarehousePickupStep or WarehouseUnloadStep)
                    continue;
                if (step is BarterStep completedBarter
                    && completedBarterRowIds is not null
                    && RouteTaskIdentity.IsCompleted(completedBarter.RowId, completedBarterRowIds))
                    continue;
                var label = Build(
                    route.Number,
                    stepIndex,
                    step,
                    itemDisplayNames,
                    barterGroupsByRowId);
                if (label is not null) result.Add(label);
            }
        }
        return result;
    }

    private static RouteStepMapLabel? Build(
        int routeNumber,
        int stepIndex,
        RouteStep step,
        IReadOnlyDictionary<string, string> itemDisplayNames,
        IReadOnlyDictionary<string, int>? barterGroupsByRowId) {
        return step switch {
            WarehousePickupStep pickup => new RouteStepMapLabel(
                RouteNumber: routeNumber,
                StepIndex: stepIndex,
                StepKind: RouteStepKind.Pickup,
                IslandId: pickup.IslandId,
                BarterRowId: null,
                DisplayText: FormatWarehouseText(
                    LookupIslandDisplay(pickup.IslandId), isPickup: true),
                IsWarehouseOperation: true),
            WarehouseUnloadStep unload => new RouteStepMapLabel(
                RouteNumber: routeNumber,
                StepIndex: stepIndex,
                StepKind: RouteStepKind.Unload,
                IslandId: unload.IslandId,
                BarterRowId: null,
                DisplayText: FormatWarehouseText(
                    LookupIslandDisplay(unload.IslandId), isPickup: false),
                IsWarehouseOperation: true),
            BarterStep barter => new RouteStepMapLabel(
                RouteNumber: routeNumber,
                StepIndex: stepIndex,
                StepKind: RouteStepKind.Barter,
                IslandId: barter.IslandId,
                BarterRowId: barter.RowId,
                DisplayText: FormatBarterText(
                    itemDisplayNames.TryGetValue(barter.Consumed.ItemId, out var inDisp)
                        ? inDisp : barter.Consumed.ItemId,
                    barter.Consumed.Quantity,
                    itemDisplayNames.TryGetValue(barter.Produced.ItemId, out var outDisp)
                        ? outDisp : barter.Produced.ItemId,
                    barter.Produced.Quantity),
                IsWarehouseOperation: false,
                BarterGroup: barterGroupsByRowId is not null
                    && barterGroupsByRowId.TryGetValue(
                        RouteTaskIdentity.PlannerRowId(barter.RowId), out int group)
                        ? group
                        : null),
            _ => null, // unknown step kind — caller skips.
        };
    }

    /// <summary>
    /// Tests pass a stub map; production callers pass a map built
    /// from the live <c>App.listIslands</c> catalog.  Unknown ids
    /// fall back to the raw id string so the WPF layer still gets
    /// a useful label.
    /// </summary>
    private static string LookupIslandDisplay(string islandId) => islandId;

    /// <summary>
    /// Filters out the pickup / unload labels for an island, used
    /// for the "warehouse dedup" pass so the WPF layer doesn't
    /// stack a route-only label on top of an existing warehouse
    /// label.  <b>Critical:</b> the dedup operates on
    /// <c>IsWarehouseOperation = true</c> only.  Real
    /// <c>Barter</c> steps on the same island are NEVER
    /// deduped, so e.g. Iliya with pickup + barter + unload
    /// still shows the barter text.
    /// </summary>
    public static IReadOnlyList<RouteStepMapLabel> StripWarehouseDuplicates(
        IReadOnlyList<RouteStepMapLabel> labels) {
        var firstWarehouseByIsland = new HashSet<(int Route, int Step)>();
        var seenWarehouse = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<RouteStepMapLabel>(labels.Count);
        foreach (var label in labels) {
            if (label.IsWarehouseOperation
                && !seenWarehouse.Add(label.IslandId)) {
                // Subsequent warehouse label for the same island —
                // skip.  (Iliya with both pickup AND unload gets only
                // one warehouse label; the WPF layer combines them
                // into "Iliya · 装货/卸货" elsewhere.)
                continue;
            }
            result.Add(label);
        }
        return result;
    }
}
