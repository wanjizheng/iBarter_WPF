namespace iBarter.Routing;

/// <summary>
/// Audit round 5 (regression fix): the v1 island-label renderer
/// silently dropped every label the first time it ran because
/// <c>TryGetIslandCenter</c> returns false when
/// <c>Grid_MapMain.ActualWidth == 0</c> or
/// <c>ActualHeight == 0</c> (i.e. the map has not been laid out
/// yet) — and the subsequent <c>IslandsButtonRearrange</c> only
/// repositions existing visuals, it never re-invokes the label
/// builder. The fix splits the decision into a pure helper that
/// the WPF layer can drive from any lifecycle event without
/// re-implementing the math each time.
/// </summary>
public static class RouteIslandLabelRenderer {
    /// <summary>
    /// Outcome of a rebuild attempt. The WPF layer records the
    /// non-Built result so a later SizeChanged / Render callback
    /// can retry without re-deriving the input set.
    /// </summary>
    public enum RebuildOutcome {
        /// <summary>Every requested label was created.</summary>
        Built,
        /// <summary>
        /// At least one island was suppressed because the host has
        /// no size yet. The WPF layer should schedule a retry at
        /// <c>DispatcherPriority.Render</c>.
        /// </summary>
        Deferred,
        /// <summary>
        /// Nothing to do (no visible islands, no plan, or every
        /// island already has a warehouse label).
        /// </summary>
        Empty,
    }

    /// <summary>
    /// Computes the set of island ids the map must render an
    /// island base label for, deduplicated and stripped of any
    /// island that already carries a warehouse label. The
    /// authoritative source for warehouse ids is
    /// <see cref="RouteRenderSnapshot.WarehouseIslandIds"/> — the
    /// previous version's name-suffix scan of
    /// <c>Grid_MapMain.Children</c> was unreliable because
    /// warehouse labels live inside <c>GridContainer_*Warehouse</c>,
    /// not as direct children.
    /// </summary>
    public static IReadOnlyCollection<string> PlanLabels(
        IReadOnlyCollection<string> visibleIslands,
        IReadOnlySet<string> warehouseIslandIds) =>
        RouteIslandLabelPlanner.ExcludeWarehouseIslands(visibleIslands, warehouseIslandIds);

    /// <summary>
    /// Drives the WPF rebuild. The caller passes the desired set
    /// of label ids (from <see cref="PlanLabels"/>) plus the
    /// current visible label ids (carried in
    /// <c>RouteIslandLabelTag.IslandId</c> on existing wrappers).
    /// The function returns the labels to add and the ids to remove
    /// so the WPF layer can reconcile.
    /// </summary>
    /// <param name="planned">Desired label ids, e.g. from
    /// <see cref="PlanLabels"/>.</param>
    /// <param name="existing">Label ids currently in the visual
    /// tree (deduplicated).</param>
    /// <param name="hostWidth">Map host's <c>ActualWidth</c>; a
    /// value &lt;= 0 means "not laid out yet".</param>
    /// <param name="hostHeight">Map host's <c>ActualHeight</c>.</param>
    /// <param name="toAdd">Labels that must be created.</param>
    /// <param name="toRemove">Labels that must be removed
    /// (ids no longer in the planned set).</param>
    public static RebuildOutcome Reconcile(
        IReadOnlyCollection<string> planned,
        IReadOnlySet<string> existing,
        double hostWidth,
        double hostHeight,
        out IReadOnlyList<string> toAdd,
        out IReadOnlyList<string> toRemove) {
        toAdd = Array.Empty<string>();
        toRemove = Array.Empty<string>();
        if (planned.Count == 0) {
            // No planned labels: drop every existing wrapper.
            toRemove = existing.ToArray();
            return existing.Count == 0 ? RebuildOutcome.Empty : RebuildOutcome.Built;
        }
        if (hostWidth <= 0 || hostHeight <= 0) {
            // Map not laid out yet. Defer; keep existing labels
            // untouched so the next SizeChanged callback can retry.
            return RebuildOutcome.Deferred;
        }

        var plannedSet = new HashSet<string>(planned, StringComparer.Ordinal);
        var toRemoveList = new List<string>();
        foreach (var id in existing) {
            if (!plannedSet.Contains(id)) toRemoveList.Add(id);
        }
        var toAddList = new List<string>();
        foreach (var id in planned) {
            if (!existing.Contains(id)) toAddList.Add(id);
        }
        toRemove = toRemoveList;
        toAdd = toAddList;
        // Idempotent path: nothing to add or remove. Returning Empty
        // lets the WPF layer skip the (no-op) visual tree work
        // without firing AddRouteIslandLabels / RemoveRouteIslandLabels.
        if (toAdd.Count == 0 && toRemove.Count == 0)
            return RebuildOutcome.Empty;
        return RebuildOutcome.Built;
    }
}