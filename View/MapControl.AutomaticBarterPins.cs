using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using iBarter.Routing;
using iBarter.ViewModel;

namespace iBarter.View;

/// <summary>
/// Restores the legacy 10x10 barter-group square for automatic routes without
/// re-enabling the old Planner barter visual pipeline. The old pipeline is
/// intentionally suppressed in AutomaticRoute mode because it can render rows
/// that are not part of the selected route and grant completion authority to
/// the wrong visual. This layer is driven exclusively by real BarterSteps.
/// </summary>
public partial class MapControl {
    private sealed record AutomaticBarterPinVisual(RouteStepBarterPin Pin);
    private bool automaticBarterPinReconcileScheduled;

    /// <summary>
    /// The map's existing resize/pan/route refresh path invalidates this control.
    /// Schedule pin reconciliation after each real render instead of relying on a
    /// second Tick subscription whose initialization order can vary with WPF's
    /// generated partial class. The guard coalesces repeated invalidations into one
    /// visual-tree update.
    /// </summary>
    protected override void OnRender(DrawingContext drawingContext) {
        base.OnRender(drawingContext);
        ScheduleAutomaticBarterPinReconcile();
    }

    private void ScheduleAutomaticBarterPinReconcile() {
        if (automaticBarterPinReconcileScheduled || Dispatcher.HasShutdownStarted)
            return;
        automaticBarterPinReconcileScheduled = true;
        Dispatcher.BeginInvoke(new Action(() => {
            automaticBarterPinReconcileScheduled = false;
            ReconcileAndPositionAutomaticBarterPins();
        }), DispatcherPriority.Render);
    }

    private void ReconcileAndPositionAutomaticBarterPins() {
        if (Grid_MapMain is null || Grid_MapMain.ActualWidth <= 0
            || Grid_MapMain.ActualHeight <= 0) {
            return;
        }

        IReadOnlyList<RouteStepBarterPin> desired = PlanAutomaticBarterPins();
        var desiredByIdentity = desired.ToDictionary(
            pin => pin.Identity, StringComparer.Ordinal);

        // Remove stale pins when a route changes, a barter completes, ShowAll is
        // toggled, or the application returns to manual mode.
        for (int index = Grid_MapMain.Children.Count - 1; index >= 0; index--) {
            if (Grid_MapMain.Children[index] is FrameworkElement element
                && element.Tag is AutomaticBarterPinVisual visual
                && !desiredByIdentity.ContainsKey(visual.Pin.Identity)) {
                Grid_MapMain.Children.RemoveAt(index);
            }
        }

        var existing = Grid_MapMain.Children
            .OfType<Rectangle>()
            .Where(rectangle => rectangle.Tag is AutomaticBarterPinVisual)
            .ToDictionary(
                rectangle => ((AutomaticBarterPinVisual)rectangle.Tag).Pin.Identity,
                StringComparer.Ordinal);

        foreach (RouteStepBarterPin pin in desired) {
            if (!existing.TryGetValue(pin.Identity, out Rectangle? rectangle)) {
                rectangle = CreateAutomaticBarterPin(pin);
                Grid_MapMain.Children.Add(rectangle);
                existing[pin.Identity] = rectangle;
            }
            else if (rectangle.Tag is AutomaticBarterPinVisual current
                     && current.Pin != pin) {
                rectangle.Tag = new AutomaticBarterPinVisual(pin);
            }

            ApplyAutomaticBarterPinColour(rectangle, pin);
        }

        // When more than one barter occurs at the same physical stop, keep the
        // squares in a compact horizontal row centred on the NPC. A single square
        // remains exactly centred on the physical barter destination.
        foreach (IGrouping<string, RouteStepBarterPin> islandGroup in desired
                     .GroupBy(pin => pin.IslandId, StringComparer.Ordinal)) {
            var island = App.listIslands?.FirstOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.IslandsName, islandGroup.Key));
            if (island is null
                || !TryGetIslandCenter(island, out Grid? host, out Point center)
                || host is null) {
                continue;
            }

            RouteStepBarterPin[] pins = islandGroup
                .OrderBy(pin => pin.Identity, StringComparer.Ordinal)
                .ToArray();
            double totalWidth = 10 + Math.Max(0, pins.Length - 1) * 12;
            double startX = center.X - totalWidth / 2;
            for (int occurrence = 0; occurrence < pins.Length; occurrence++) {
                if (!existing.TryGetValue(pins[occurrence].Identity, out Rectangle? pinVisual))
                    continue;
                double left = startX + occurrence * 12;
                double top = center.Y - 5;
                pinVisual.Margin = new Thickness(
                    left,
                    top,
                    Math.Max(0, host.ActualWidth - left - 10),
                    Math.Max(0, host.ActualHeight - top - 10));
                pinVisual.Visibility = Visibility.Visible;
                // Route labels use z=60. Put the square above both the label and
                // the arrow intersection so its group colour remains unmistakable.
                Panel.SetZIndex(pinVisual, 75);
            }
        }
    }

    private IReadOnlyList<RouteStepBarterPin> PlanAutomaticBarterPins() {
        var coordinator = App.myRouteCoordinator;
        if (coordinator?.Mode != CargoMode.AutomaticRoute
            || coordinator.CurrentPlan is not { } plan) {
            return Array.Empty<RouteStepBarterPin>();
        }

        IReadOnlyList<RouteStepMapLabel> labels = RouteStepLabelPlanner.PlanLabels(
            plan,
            coordinator.ShowAllRoutes,
            coordinator.SelectedRouteNumber,
            BuildItemDisplayNameLookup(),
            BuildBarterGroupLookup(),
            coordinator.CompletedBarterRowIds);
        return RouteStepBarterPinPlanner.Plan(labels);
    }

    private Rectangle CreateAutomaticBarterPin(RouteStepBarterPin pin) {
        var rectangle = new Rectangle {
            Name = "AutomaticBarterPin_" + SanitizeAutomaticPinName(pin.Identity),
            Width = 10,
            Height = 10,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Tag = new AutomaticBarterPinVisual(pin),
            SnapsToDevicePixels = true,
        };
        RenderOptions.SetEdgeMode(rectangle, EdgeMode.Aliased);
        Panel.SetZIndex(rectangle, 75);
        ApplyAutomaticBarterPinColour(rectangle, pin);
        return rectangle;
    }

    private static string SanitizeAutomaticPinName(string identity) =>
        new(identity.Select(character => Char.IsLetterOrDigit(character)
            ? character
            : '_').ToArray());

    private static void ApplyAutomaticBarterPinColour(
        Rectangle rectangle,
        RouteStepBarterPin pin) {
        Brush groupBrush = GetBrushForGroup(pin.BarterGroup ?? Int32.MinValue);
        Brush textColour = LightenForMapBg(groupBrush);

        // Fill is exactly the same derived group colour used by the automatic
        // transaction text. A dark outline keeps the 10x10 block distinct where
        // it crosses a same-colour route arrow or label border.
        rectangle.Fill = textColour;
        rectangle.Stroke = Brushes.Black;
        rectangle.StrokeThickness = 1.5;
    }
}
