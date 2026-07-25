using System.Windows.Controls;
using System.Windows.Threading;

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
/// <see cref="Grid"/> entries tagged with the live IslandVisual
/// nested type, but the decision — "is this snapshot stale given the
/// active-barter set + warehouse set?" — does not need any WPF
/// type. Tests construct <see cref="MapIslandCleanupSnapshot"/>
/// values directly to exercise this helper without spinning up a
/// real Grid.
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
    /// indices are stable to remove in a single pass.
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
}