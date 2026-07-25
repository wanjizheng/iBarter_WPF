using System.Windows.Controls;
using System.Windows.Threading;

namespace iBarter.View;

/// <summary>
/// WPF-only partial of <see cref="MapControl"/> that composes the
/// three sub-passes (traditional markers, route-step labels,
/// warehouse labels) into a single deferred reflow. Kept separate
/// from <c>MapControl.Reflow.cs</c> so the pure helpers (pipeline,
/// projection math, cleanup decision) can be linked into the
/// non-WPF test project without pulling in the full WPF root.
///
/// This file is intentionally NOT included in the test project's
/// &lt;Compile Include&gt; list — the methods here need a live
/// <see cref="UserControl"/>, and that requires the full WPF
/// rendering stack which the test project does not reference.
/// </summary>
public partial class MapControl {

    private readonly MapOverlayReflowPipeline _overlayReflow = new();

    /// <summary>
    /// Pure snapshot builder for the cleanup pass. Lives here so
    /// the snapshot contract is co-located with
    /// <see cref="MapIslandVisualCleanup"/>. Returns a sentinel
    /// "stays put" snapshot when the grid has no IslandVisual tag
    /// (defensive: such grids are never stale).
    /// </summary>
    internal MapIslandCleanupSnapshot BuildCleanupSnapshot(
        Grid grid,
        iBarter.Routing.RouteRenderSnapshot renderSnapshot) {
        if (grid.Tag is not IslandVisual visual) {
            // No descriptor → treat as a Temp-style anchor: never
            // removed by the cleanup pass.
            return new MapIslandCleanupSnapshot(
                IsTemp: true,
                IsWarehouse: false,
                IslandId: string.Empty);
        }
        bool isVisibleWarehouse = renderSnapshot.WarehouseIslandIds
            .Contains(visual.Islands.IslandsName);
        return new MapIslandCleanupSnapshot(
            IsTemp: visual.IsTemp,
            IsWarehouse: isVisibleWarehouse,
            IslandId: visual.Islands.IslandsName);
    }

    /// <summary>
    /// Unified entry point for every "pure geometry changed" event:
    /// window resize, dock split, HD tile refresh, zoom, pan,
    /// timer tick, Loaded. Coalesces a burst of resize events into
    /// ONE deferred pass that reads the latest
    /// <c>Grid_MapMain.ActualWidth</c> / <c>ActualHeight</c> after
    /// the layout pass that delivered them has completed.
    ///
    /// The three sub-passes run in order: traditional markers
    /// first (so their bounds are measured), then route-step
    /// labels (which stack by occurrence offset), then warehouse
    /// labels (whose placement depends on the lowest route-step
    /// label on the same island having already been laid out).
    ///
    /// <see cref="DispatcherPriority.Loaded"/> was chosen over
    /// <see cref="DispatcherPriority.Render"/> because Loaded
    /// fires after the new layout pass completes AND after any
    /// LayoutUpdated notifications for the same composition cycle
    /// have been processed, which gives the read of
    /// <c>Grid_MapMain.ActualWidth</c> a stable value even when
    /// the resize is the last in a burst of SizeChanged events.
    /// </summary>
    internal void ScheduleMapOverlayReflow() {
        if (!_overlayReflow.TryBegin()) {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() => {
            try {
                if (!IsLoaded
                    || Grid_MapMain.ActualWidth <= 0
                    || Grid_MapMain.ActualHeight <= 0) {
                    return;
                }

                IslandsButtonRearrange();
                RepositionRouteStepLabels();
                RepositionAutomaticWarehouseLabels();
            }
            finally {
                _overlayReflow.End();
            }
        }), DispatcherPriority.Loaded);
    }
}