using System.Collections.Generic;

namespace iBarter.Routing;

/// <summary>
/// Audit round 6: testable reconcile + offset rule for
/// <see cref="RouteStepMapLabel"/>.
///
/// <para>The audit mandates that identity reconcile and geometry
/// reposition are two distinct operations:</para>
///   * <see cref="Reconcile"/>: add / remove wrappers based on
///     (RouteNumber, StepIndex) identity. Never touches
///     positions.
///   * <see cref="ComputeOffsetForStep"/>: pure function that
///     decides where one label sits relative to the island
///     centre. Stable across reconcile cycles.
/// </para>
///
/// <para>The WPF layer drives both: <c>EnsureRouteStepLabels</c>
/// calls <see cref="Reconcile"/> to add/remove wrappers, and a
/// separate <c>RepositionRouteStepLabels</c> walks all existing
/// wrappers and updates their <c>Margin</c>. The two are
/// deliberately decoupled so a viewport-only change (zoom/pan/
/// HD camera move) does not re-create wrappers and lose
/// identity, and a route-plan change does not depend on
/// layout-pass timing.</para>
/// </summary>
public static class RouteStepLabelRenderer {
    /// <summary>
    /// Outcome of a rebuild attempt. The WPF layer records the
    /// non-Built result so a later SizeChanged / Render callback
    /// can retry without re-deriving the input set.
    /// </summary>
    public enum RebuildOutcome {
        /// <summary>Every requested label was created.</summary>
        Built,
        /// <summary>
        /// At least one label was suppressed because the host has
        /// no size yet. The WPF layer should schedule a retry at
        /// <c>DispatcherPriority.Render</c>.
        /// </summary>
        Deferred,
        /// <summary>
        /// Nothing to do (no visible labels, no plan, or every
        /// label already exists).
        /// </summary>
        Empty,
    }

    /// <summary>
    /// Stable per-step key used by both Reconcile and the WPF
    /// layer's <c>existing</c> set. Mirrors
    /// <see cref="RouteStepMapLabel.Identity"/>.
    /// </summary>
    public static string IdentityOf(RouteStepMapLabel label) => label.Identity;

    /// <summary>
    /// Decide which labels must be created and which must be
    /// removed. Identity is (RouteNumber, StepIndex); the WPF
    /// layer stores the descriptor on the wrapper's <c>Tag</c> and
    /// passes the existing identities back in.
    /// </summary>
    /// <param name="planned">Desired labels, in plan order.</param>
    /// <param name="existing">Currently rendered label
    /// identities (mirrors the wrapper tags).</param>
    /// <param name="hostWidth">Map host's <c>ActualWidth</c>;
    /// 0 means "not laid out yet".</param>
    /// <param name="hostHeight">Map host's
    /// <c>ActualHeight</c>.</param>
    /// <param name="toAdd">New wrappers to create (in plan
    /// order so the WPF layer can place them deterministically).
    /// </param>
    /// <param name="toRemove">Existing identities that no longer
    /// appear in the plan.</param>
    public static RebuildOutcome Reconcile(
        IReadOnlyList<RouteStepMapLabel> planned,
        IReadOnlySet<string> existing,
        double hostWidth,
        double hostHeight,
        out IReadOnlyList<RouteStepMapLabel> toAdd,
        out IReadOnlyList<string> toRemove) {
        toAdd = Array.Empty<RouteStepMapLabel>();
        toRemove = Array.Empty<string>();
        if (planned.Count == 0) {
            toRemove = existing.ToArray();
            return existing.Count == 0 ? RebuildOutcome.Empty : RebuildOutcome.Built;
        }
        if (hostWidth <= 0 || hostHeight <= 0) {
            return RebuildOutcome.Deferred;
        }
        var plannedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var label in planned) plannedIds.Add(label.Identity);
        var toRemoveList = new List<string>();
        foreach (var id in existing) {
            if (!plannedIds.Contains(id)) toRemoveList.Add(id);
        }
        var toAddList = new List<RouteStepMapLabel>();
        foreach (var label in planned) {
            if (!existing.Contains(label.Identity)) toAddList.Add(label);
        }
        toRemove = toRemoveList;
        toAdd = toAddList;
        if (toAdd.Count == 0 && toRemove.Count == 0)
            return RebuildOutcome.Empty;
        return RebuildOutcome.Built;
    }

    /// <summary>
    /// Audit round 6: same-island multiple steps must produce
    /// distinct, non-overlapping labels. The rule is a fixed
    /// vertical offset per <c>StepIndex</c> within the island —
    /// deterministic, so re-running on the same plan always
    /// yields the same offsets (the visual tree doesn't
    /// jitter on reconcile).
    /// </summary>
    /// <param name="occurrenceOnIsland">0 for the first step on
    /// that island, 1 for the second, etc. The WPF layer
    /// computes this by counting existing labels on the same
    /// island that have a smaller StepIndex.</param>
    /// <param name="hostHeight">Map host height; not consumed by
    /// the offset itself but kept for symmetry with
    /// <see cref="RouteStepLabelRenderer"/> callers that
    /// pass host dimensions.</param>
    public static double ComputeVerticalOffset(int occurrenceOnIsland, double hostHeight) {
        // The first label sits 5 px below the island centre; each
        // subsequent label is 18 px lower.  This keeps everything
        // within the island overlay host even when the host is
        // short, and the deterministic spacing means reconcile
        // cycles do not visibly rearrange.
        const double baseOffset = 5.0;
        const double lineHeight = 18.0;
        return baseOffset + occurrenceOnIsland * lineHeight;
    }

    /// <summary>
    /// Computes the horizontal "stagger" applied to a label
    /// relative to the island centre. Different step kinds can
    /// share the same island (pickup + barter + unload), so we
    /// keep horizontal alignment identical and rely on
    /// <see cref="ComputeVerticalOffset"/> for separation. The
    /// helper exists so the WPF layer's positioning code is
    /// symmetric and future offsets (e.g. per StepKind) have one
    /// place to live.
    /// </summary>
    public static double ComputeHorizontalStagger(
        RouteStepKind stepKind, int occurrenceOnIsland) => 0.0;
}