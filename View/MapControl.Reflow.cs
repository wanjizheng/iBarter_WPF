namespace iBarter.View;

/// <summary>
/// Pure snapshot of the fields MapIslandVisualCleanup looks at to
/// decide whether a tracked grid is stale. WPF-free so the test
/// project (which links this file but does NOT link the WPF root
/// MapControl) can pin the decision logic without instantiating
/// any Grid.
/// </summary>
public readonly record struct MapIslandCleanupSnapshot(
    bool IsTemp,
    bool IsWarehouse,
    string IslandId);

/// <summary>
/// Coalescing flag for the map's "everything must reposition" pass.
///
/// Every pure-geometry event on the map (window resize, dock split,
/// HD tile refresh, zoom, pan, timer tick, Loaded) calls
/// <see cref="TryBegin"/>. The first caller wins; subsequent callers
/// during the same composition cycle are dropped, because the
/// BeginInvoke callback reads the latest
/// <c>ActualWidth</c>/<c>ActualHeight</c> when it actually runs. The
/// callback calls <see cref="End"/> in a <c>finally</c> so a thrown
/// pass still re-arms the pipeline.
///
/// Pure state, no WPF dependency. Unit-testable in isolation.
/// </summary>
internal sealed class MapOverlayReflowPipeline {
    private bool _pending;

    public bool IsPending => _pending;

    /// <summary>
    /// Attempts to claim the next reflow slot. Returns <c>true</c>
    /// if the caller is the first to call since the previous
    /// <see cref="End"/>; <c>false</c> if a reflow is already
    /// queued.
    /// </summary>
    public bool TryBegin() {
        if (_pending) return false;
        _pending = true;
        return true;
    }

    /// <summary>
    /// Releases the slot so the next caller can claim it. Always
    /// call from a <c>finally</c> block.
    /// </summary>
    public void End() {
        _pending = false;
    }
}

/// <summary>
/// Pure-function helper used by the reflow pipeline to project a
/// normalized [0,1] map coordinate onto a host rectangle of the
/// given pixel size. Extracted so resize scaling can be pinned by a
/// unit test without instantiating WPF elements.
///
/// <para>This is the single source of truth for the resize math:
/// the production static-map path in
/// <c>View/MapControl.HdMap.cs:TryGetIslandCenter</c> also calls
/// this helper, so the unit test pins the value the user actually
/// sees when resizing a non-HD window.</para>
/// </summary>
internal static class MapProjectionHelper {
    public static (double X, double Y) ProjectNormalized(
        double normalizedX,
        double normalizedY,
        double hostWidth,
        double hostHeight) {
        return (
            normalizedX * hostWidth,
            normalizedY * hostHeight);
    }
}

/// <summary>
/// Pure decision logic for the stale-visual cleanup pass. The full
/// cleanup pass (in <c>IslandsButtonRearrange</c>) operates on
/// <see cref="System.Windows.Controls.Grid"/> entries tagged with
/// the live IslandVisual nested type, but the decision — "is this
/// snapshot stale given the active-barter set + warehouse set?" —
/// does not need any WPF type. Tests construct
/// <see cref="MapIslandCleanupSnapshot"/> values directly to
/// exercise this helper without spinning up a real Grid.
///
/// <para><b>Index contract:</b> <see cref="CollectStaleIndices"/>
/// walks the tracked list backward and appends each stale index in
/// the order it finds them. The returned list is therefore in
/// <b>strictly descending</b> order (largest first), so the caller
/// can <c>foreach</c> over the returned indices and remove each
/// entry without any index-shifting hazards. Reversing this order
/// in the caller is a contract violation — the v1 bug we are
/// remediating.</para>
/// </summary>
internal static class MapIslandVisualCleanup {

    /// <summary>
    /// Predicate contract: given a snapshot, should the cleanup
    /// pass drop it? Temp snapshots must always return false.
    /// </summary>
    public delegate bool ShouldRemoveSnapshot(MapIslandCleanupSnapshot snapshot);

    /// <summary>
    /// Returns the indices (in <paramref name="tracked"/>) of the
    /// entries that should be removed. Walk is backward so the
    /// returned list is strictly descending (largest first). The
    /// caller iterates the returned list in order; each removal
    /// shifts still-pending higher indices left by one, but never
    /// touches the smaller indices we have not processed yet, so
    /// the iteration stays valid.
    /// </summary>
    public static List<int> CollectStaleIndices<T>(
        IReadOnlyList<T> tracked,
        Func<T, MapIslandCleanupSnapshot> getSnapshot,
        ShouldRemoveSnapshot shouldRemove) {
        var result = new List<int>();
        for (int i = tracked.Count - 1; i >= 0; i--) {
            T item = tracked[i];
            MapIslandCleanupSnapshot snap = getSnapshot(item);
            if (snap.IsTemp) continue; // position anchors stay
            if (!shouldRemove(snap)) continue;
            result.Add(i);
        }
        return result;
    }

    /// <summary>
    /// Removes each index in <paramref name="descendingIndices"/>
    /// from <paramref name="items"/>, in order. The contract is
    /// that the caller has produced <paramref name="descendingIndices"/>
    /// from <see cref="CollectStaleIndices"/> (or an equivalent
    /// descending list); this method does NOT defensively sort or
    /// reverse — doing so was the bug the v1 code introduced.
    ///
    /// <para>Pass the indices in their original order (descending).
    /// Each removal is preceded by the optional
    /// <paramref name="beforeRemove"/> callback so the caller can
    /// detach the corresponding item from its visual-tree parent
    /// (or run any other side effect) BEFORE the list mutation
    /// changes the index mapping for subsequent entries.</para>
    ///
    /// <para>Throws <see cref="ArgumentOutOfRangeException"/> if
    /// any index is negative or out of range — these are pre-
    /// validated in a single pass BEFORE any mutation, so the list
    /// stays untouched on a validation failure. Throws
    /// <see cref="ArgumentException"/> if the list is not strictly
    /// descending — same pre-validation guarantee.</para>
    /// </summary>
    public static void RemoveAtDescendingIndices<T>(
        IList<T> items,
        IReadOnlyList<int> descendingIndices,
        Action<T>? beforeRemove = null) {
        // Pre-validate every index before touching the list. This
        // means a contract violation leaves the items untouched
        // and is impossible to mistake for a "successful" removal
        // that just happened to crash mid-way.
        int previousIndex = int.MaxValue;
        for (int k = 0; k < descendingIndices.Count; k++) {
            int index = descendingIndices[k];
            if (index < 0 || index >= items.Count) {
                throw new ArgumentOutOfRangeException(
                    nameof(descendingIndices),
                    $"descendingIndices[{k}]={index} is out of range [0, {items.Count}).");
            }
            if (index >= previousIndex) {
                throw new ArgumentException(
                    $"descendingIndices must be strictly descending; " +
                    $"got {index} at position {k} after previous {previousIndex}.",
                    nameof(descendingIndices));
            }
            previousIndex = index;
        }
        // All indices validated — now actually remove, descending.
        for (int k = 0; k < descendingIndices.Count; k++) {
            int index = descendingIndices[k];
            T item = items[index];
            beforeRemove?.Invoke(item);
            items.RemoveAt(index);
        }
    }
}