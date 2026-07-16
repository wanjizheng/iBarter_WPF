using Syncfusion.Windows.Controls.PivotGrid;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Media.Effects;
using iBarter.Localization;
using iBarter.ViewModel;
using iBarter.Navigation;
using iBarter.Routing;
using static iBarter.EnumLists;
using Grid = System.Windows.Controls.Grid;
using RowColumnIndex = Syncfusion.UI.Xaml.ScrollAxis.RowColumnIndex;


namespace iBarter.View {
    /// <summary>
    /// Interaction logic for MapControl.xaml
    /// </summary>
    public partial class MapControl : UserControl {
        // Highest barter item LV to render on the map. 7 keeps every catalog tier visible
        // after the LV6/LV7 extension; lower this if you want to hide high-tier pins.
        private const int MAX_MAP_LV = 7;
        private bool IsDesignMode => DesignerProperties.GetIsInDesignMode(this);

        public List<Grid> listGrid_Islands = new List<Grid>();
        private List<Label> listLabels = null;
        private List<Grid> listImages = null;
        private List<Line> listLines = null;
        public DispatcherTimer myTimer = new DispatcherTimer();
        private readonly MapViewportState viewportState = new MapViewportState();
        private bool isPanningMap;
        private Point lastPanPoint;

        // Holds direct references to an island's visual parts (image block,
        // label, connector line) plus its Islands model and whether it is a
        // "Temp" placeholder (an island with no active barter, kept on the
        // map permanently as a position marker). Stashing this on the
        // container Grid's Tag lets IslandsButtonRearrange reposition every
        // island - Temp or not - on every resize without re-deriving names
        // from Grid.Name substrings/suffixes, which was fragile and is what
        // previously left Temp placeholders "stuck" after a window resize.
        private class IslandVisual {
            public Islands Islands;
            public bool IsTemp;
            public bool IsWarehouse;
            public Grid Host;
            public Grid ImageGrid;
            public Label Label;
            public Line Line;
            public Rectangle Rectangle;
            public Brush BaseBackground;
            public Brush BaseBorderBrush;
            public Thickness BaseBorderThickness;
            public FontWeight BaseFontWeight;
            public double BaseFontSize;
            public Brush BaseRectangleStroke;
            public double BaseRectangleStrokeThickness;
            public bool? LastMeasuredHighlight;
            public string LastMeasuredContent = String.Empty;
            public double LastMeasuredFontSize;
            public Size LabelPlacementSize;
        }

        private bool routeDisplaySubscribed;

        public MapControl() {
            InitializeComponent();
            if (IsDesignMode) return;
            //InitTempGrid();

            myTimer.Interval = TimeSpan.FromMilliseconds(100);
            myTimer.Tick += TimerOnTick;
            // DockingManager unloads the control when its tab is hidden,
            // so restart the timer on Loaded too - otherwise the auto-
            // layout (IslandsButtonRearrange) and overlap-avoidance
            // (AdjustLabels) stop firing after the user switches tabs.
            this.Loaded += (_, _) => {
                if (myTimer != null && !myTimer.IsEnabled) myTimer.Start();
                TryInitializeHdMap();
                if (!routeDisplaySubscribed && App.myRouteCoordinator != null) {
                    App.myRouteCoordinator.RouteDisplayChanged += RouteCoordinator_RouteDisplayChanged;
                    routeDisplaySubscribed = true;
                }
                Dispatcher.BeginInvoke(
                    new Action(CenterFocusedSegmentIfNeeded),
                    DispatcherPriority.Render);
            };
            this.Unloaded += (_, _) => {
                if (myTimer != null && myTimer.IsEnabled) myTimer.Stop();
            };
            // Also re-trigger on Grid_MapMain.SizeChanged. Defer via
            // Dispatcher.BeginInvoke at Render priority so the new layout
            // pass completes first - ActualWidth/Height are still stale at
            // the SizeChanged callback point and would otherwise position
            // islands at 0,0 (i.e. top-left = centre of a tiny map). The
            // timer keeps running as a fallback.
            Grid_MapMain.SizeChanged += (_, _) =>
                Dispatcher.BeginInvoke(new Action(IslandsButtonRearrange),
                    DispatcherPriority.Render);
            MapViewport.SizeChanged += (_, _) => {
                if (hdMapEnabled) RefreshHdMap();
                CenterFocusedSegmentIfNeeded();
            };
            myTimer.Start();
        }

        private void RouteCoordinator_RouteDisplayChanged(object? sender, EventArgs e) {
            Dispatcher.BeginInvoke(new Action(() => {
                IslandsButtonInitialisation();
                CenterFocusedSegmentIfNeeded();
            }), DispatcherPriority.Render);
        }

        private void TimerOnTick(object? sender, EventArgs e) {
            if (!hdMapEnabled && !hdMapInitializationAttempted)
                TryInitializeHdMap();
            IslandsButtonRearrange();
            InvalidateVisual();
        }

        private void MapViewport_MouseWheel(object sender, MouseWheelEventArgs e) {
            if (e.Delta == 0) return;
            Point position = e.GetPosition(MapViewport);
            if (hdMapEnabled && mainHdCamera is not null && hdMapConfiguration is not null) {
                mainHdCamera.ZoomAt(
                    position.X,
                    position.Y,
                    MapViewport.ActualWidth,
                    MapViewport.ActualHeight,
                    mainHdCamera.Zoom + (e.Delta > 0 ? 0.25 : -0.25),
                    hdMapConfiguration.TileSize);
            }
            else {
                viewportState.ZoomAt(position, e.Delta > 0 ? 1.15 : 1 / 1.15);
            }
            ApplyMapViewportState();
            e.Handled = true;
        }

        private void MapViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            if (!IsBlankMapArea(e.OriginalSource)) return;
            isPanningMap = true;
            lastPanPoint = e.GetPosition(MapViewport);
            MapViewport.CaptureMouse();
            e.Handled = true;
        }

        private void MapViewport_MouseMove(object sender, MouseEventArgs e) {
            if (!isPanningMap || e.LeftButton != MouseButtonState.Pressed) return;
            Point current = e.GetPosition(MapViewport);
            Vector delta = current - lastPanPoint;
            if (hdMapEnabled && mainHdCamera is not null && hdMapConfiguration is not null)
                mainHdCamera.PanByScreen(delta.X, delta.Y, hdMapConfiguration.TileSize);
            else
                viewportState.PanBy(delta);
            lastPanPoint = current;
            ApplyMapViewportState();
        }

        private void MapViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
            if (!isPanningMap) return;
            isPanningMap = false;
            MapViewport.ReleaseMouseCapture();
            e.Handled = true;
        }

        private bool IsBlankMapArea(object? originalSource) {
            return ReferenceEquals(originalSource, MapViewport)
                || ReferenceEquals(originalSource, MainViewportContent)
                || ReferenceEquals(originalSource, Grid_MapMain)
                || ReferenceEquals(originalSource, MainTileLayer)
                || ReferenceEquals(originalSource, Image_BackgroundMap);
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e) {
            ZoomMapAtViewportCenter(1.15);
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e) {
            ZoomMapAtViewportCenter(1 / 1.15);
        }

        private void ResetMapViewport(object sender, RoutedEventArgs e) {
            if (hdMapEnabled && mainHdCamera is not null && hdMapConfiguration is not null) {
                var region = hdMapConfiguration.MainRegion;
                mainHdCamera.Reset(
                    iBarter.Mapping.WebMercatorProjection.ToNormalized(region.DefaultCenter),
                    region.DefaultZoom);
            }
            else {
                viewportState.Reset();
            }
            ApplyMapViewportState();
        }

        private void ZoomMapAtViewportCenter(double multiplier) {
            if (hdMapEnabled && mainHdCamera is not null && hdMapConfiguration is not null) {
                mainHdCamera.ZoomAt(
                    MapViewport.ActualWidth / 2,
                    MapViewport.ActualHeight / 2,
                    MapViewport.ActualWidth,
                    MapViewport.ActualHeight,
                    mainHdCamera.Zoom + (multiplier > 1 ? 0.25 : -0.25),
                    hdMapConfiguration.TileSize);
            }
            else {
                viewportState.ZoomAt(
                    new Point(MapViewport.ActualWidth / 2, MapViewport.ActualHeight / 2), multiplier);
            }
            ApplyMapViewportState();
        }

        private void ApplyMapViewportState() {
            if (hdMapEnabled) {
                MapScaleTransform.ScaleX = 1;
                MapScaleTransform.ScaleY = 1;
                MapTranslateTransform.X = 0;
                MapTranslateTransform.Y = 0;
                RefreshHdMap();
                return;
            }
            MapScaleTransform.ScaleX = viewportState.Scale;
            MapScaleTransform.ScaleY = viewportState.Scale;
            MapTranslateTransform.X = viewportState.OffsetX;
            MapTranslateTransform.Y = viewportState.OffsetY;
        }

        private double GetLabelScreenFontSize(bool highlighted) {
            if (hdMapEnabled) return 13 + (highlighted ? 2 : 0);
            double requested = 13 * Math.Sqrt(viewportState.Scale) + (highlighted ? 2 : 0);
            return Math.Clamp(requested, 11, 16);
        }

        public void InitTempGrid() {
            Islands myIsland = new Islands(Island.Ancient, 1000, 10);
            Barter myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Cox_Pirate, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Marine, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Rickun, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Cholace, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Haran, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Carrack, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Unfinished, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Lantinia, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Pakio, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Wandering, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Crow, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Halmad, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Kashuma, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Derko, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Hakoven, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            // Top-left / top-right "default displayed" islands added with the LV6/LV7 batch
            // (Top < 0.21, Left < 0.10 for top-left; Top < 0.21, Left > 0.95 for top-right).
            // These were registered in EnumLists + Islands.csv but never wired into
            // InitTempGrid, so they stayed hidden even though they live in the same map
            // corners as the existing default islands (Carrack, Hakoven, ...).
            myIsland = new Islands(Island.Dallae, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Haemo, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Arehaza, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);
        }


        public void IslandsButtonRearrange() {
            // Grid_Ajir.Margin = new Thickness(0.6325 * Grid_MapMain.ActualWidth, 0.55333 * Grid_MapMain.ActualHeight,
            //     0.355 * Grid_MapMain.ActualWidth, 0.42444 * Grid_MapMain.ActualHeight);
            // Grid_Albresser.Margin = new Thickness(0.30375 * Grid_MapMain.ActualWidth,
            //     0.786666667 * Grid_MapMain.ActualHeight, 0.68375 * Grid_MapMain.ActualWidth,
            //     0.191111111 * Grid_MapMain.ActualHeight);
            InvalidateVisual();
            listLabels = new List<Label>();
            listImages = new List<Grid>();
            listLines = new List<Line>();
            var renderSnapshot = CurrentRenderSnapshot();

            for (int i = listGrid_Islands.Count - 1; i >= 0; i--) {
                Grid grid = listGrid_Islands[i];
                IslandVisual visual = grid.Tag as IslandVisual;
                // Where(...) returns a non-null IEnumerable, so the legacy check
                // never fired and stale pins accumulated. Use Any() instead.
                bool hasActiveBarter = visual != null && App.myPVM.BarterCollection.Any(b =>
                    b.ExchangeDone == false &&
                    b.ExchangeQuantity > 0 &&
                    b.IsLandName == visual.Islands.IslandsName);
                bool isVisibleWarehouse = visual != null
                    && renderSnapshot.WarehouseIslandIds.Contains(visual.Islands.IslandsName);
                if (!hasActiveBarter && !isVisibleWarehouse) {
                    // Skip temp placeholders - they live on the map
                    // permanently as position markers and must stay in
                    // listGrid_Islands so the rearrange loop repositions
                    // them on every map resize.
                    if (visual != null && visual.IsTemp) continue;
                    listGrid_Islands.Remove(grid);
                }
            }

            foreach (Grid grid in listGrid_Islands) {
                // Use the IslandVisual stashed on Tag at construction time
                // instead of re-deriving names from Grid.Name substrings -
                // that string-matching (including the "Temp" suffix used
                // for placeholder islands) was fragile and could silently
                // fail to find the image/label, leaving Temp placeholders
                // stuck at their construction-time position after a resize.
                IslandVisual visual = grid.Tag as IslandVisual;
                if (visual == null) continue;

                Islands myIslands = visual.Islands;
                Grid Grid_Image = visual.ImageGrid;
                Label myLabel = visual.Label;
                if (!visual.IsTemp && visual.Line != null) {
                    listLines.Add(visual.Line);
                }

                if (myIslands != null && myLabel != null && Grid_Image != null) {
                    bool routeHighlight = renderSnapshot.HighlightedIslandIds.Contains(
                        myIslands.IslandsName);
                    if (routeHighlight) {
                        Brush highlightBrush = ResolveHighlightBrush(renderSnapshot, myIslands.IslandsName);
                        myLabel.FontWeight = FontWeights.ExtraBold;
                        myLabel.FontSize = GetLabelScreenFontSize(true)
                            / (hdMapEnabled ? 1 : viewportState.Scale);
                        myLabel.BorderBrush = highlightBrush;
                        myLabel.BorderThickness = new Thickness(2);
                        myLabel.Background = new SolidColorBrush(Color.FromArgb(155, 5, 22, 30));
                        visual.Rectangle.Stroke = highlightBrush;
                        visual.Rectangle.StrokeThickness = 3;
                    }
                    else {
                        myLabel.FontWeight = visual.BaseFontWeight;
                        myLabel.FontSize = GetLabelScreenFontSize(false)
                            / (hdMapEnabled ? 1 : viewportState.Scale);
                        myLabel.BorderBrush = visual.BaseBorderBrush;
                        myLabel.BorderThickness = visual.BaseBorderThickness;
                        myLabel.Background = visual.BaseBackground;
                        visual.Rectangle.Stroke = visual.BaseRectangleStroke;
                        visual.Rectangle.StrokeThickness = visual.BaseRectangleStrokeThickness;
                    }
                    string labelContent = myLabel.Content?.ToString() ?? String.Empty;
                    if (visual.LastMeasuredHighlight != routeHighlight
                        || visual.LastMeasuredContent != labelContent
                        || Math.Abs(visual.LastMeasuredFontSize - myLabel.FontSize) > 0.01
                        || visual.LabelPlacementSize.Width <= 0
                        || visual.LabelPlacementSize.Height <= 0) {
                        visual.LabelPlacementSize = MeasureLabelForPlacement(myLabel);
                        visual.LastMeasuredHighlight = routeHighlight;
                        visual.LastMeasuredContent = labelContent;
                        visual.LastMeasuredFontSize = myLabel.FontSize;
                    }
                    Size labelSize = visual.LabelPlacementSize;

                    // Use the same projected centre as DrawRouteOverlay.
                    // Resetting this from raw IslandsThickness on every timer
                    // tick made inset pins jump back to their old positions
                    // while dashed route endpoints stayed projected.
                    if (!TryGetIslandCenter(myIslands, out Grid? host, out Point center)
                        || host is null) {
                        grid.Visibility = Visibility.Collapsed;
                        continue;
                    }
                    grid.Visibility = Visibility.Visible;
                    visual.Host = host;
                    Grid_Image.Margin = new Thickness(
                        center.X - 5,
                        center.Y - 5,
                        host.ActualWidth - center.X - 5,
                        host.ActualHeight - center.Y - 5);

                    // Place the label BELOW the island block by default,
                    // but flip it ABOVE when the island sits too close
                    // to the map's bottom edge - otherwise the label gets
                    // clipped below the map (IslandsThickness.Bottom is
                    // the gap from the island bottom to the map bottom;
                    // a small value means the label would overflow).
                    double labelTop;
                    if (host.ActualHeight - center.Y < Math.Max(40, host.ActualHeight * 0.08)) {
                        // flip: position label above the island block
                        // (label baseline = top - label height)
                        labelTop = Grid_Image.Margin.Top - labelSize.Height;
                    }
                    else {
                        // default: position label below the island block
                        labelTop = Grid_Image.Margin.Top + Grid_Image.ActualHeight;
                    }
                    myLabel.Margin = new Thickness(
                        Grid_Image.Margin.Left - labelSize.Width / 2,
                        labelTop,
                        Grid_Image.Margin.Right - labelSize.Width,
                        Grid_Image.Margin.Bottom - labelSize.Height);


                    NewMargin(myLabel, host);
                    if (myLabel.Content != "") {
                        listLabels.Add(myLabel);
                        //AdjustLabels(listLabels);
                        listImages.Add(Grid_Image);
                    }
                    else {
                        // Empty label (island not in any active CargoDetails
                        // barter) - collapse it entirely so no empty rectangle
                        // shows on the map. Setting Visibility = Collapsed
                        // removes the label from layout and rendering.
                        myLabel.Visibility = Visibility.Collapsed;
                    }
                }
            }

            foreach (var hostGroup in listGrid_Islands
                .Select(grid => grid.Tag as IslandVisual)
                .Where(visual => visual is not null
                    && visual.Label.Visibility == Visibility.Visible)
                .GroupBy(visual => visual!.Host))
                AdjustLabels(hostGroup.Select(visual => visual!.Label).ToList());
            InvalidateVisual();
            foreach (IslandVisual visual in listGrid_Islands
                .Select(grid => grid.Tag as IslandVisual)
                .Where(visual => visual?.Line is not null)!) {
                var relativePointImage = GetPosition(visual.ImageGrid, visual.Host);
                var relativePointLabel = GetPosition(visual.Label, visual.Host);
                visual.Line.X1 = relativePointImage.X + 5;
                visual.Line.X2 = visual.Line.X1;
                visual.Line.Y1 = relativePointImage.Y + 5;
                visual.Line.Y2 = relativePointLabel.Y;
            }

            InvalidateVisual();

            // Redraw the barter-route overlay (dashed gold lines connecting
            // Cox_Pirate to each cargo island in order). This runs on every
            // IslandsButtonRearrange cycle (timer tick + SizeChanged +
            // docking resize) so the lines track the map's current size
            // and pixel positions. Implemented as a full remove-then-add
            // pass over our tagged lines - the alternative (diffing old
            // vs new route and patching X1/Y1/X2/Y2 in place) saves a few
            // allocations per tick but adds state-tracking complexity
            // that bites whenever IslandsButtonInitialisation wipes the
            // child list (e.g. after a map middle-click). Lines are
            // lightweight enough that 10-20 of them per tick is free.
            DrawRouteOverlay();
        }

        // ----- Route overlay (dashed lines between islands in route order) -----

        // Tag we slap on every route line we add, so DrawRouteOverlay can
        // tell our overlay lines apart from the per-island connector
        // lines the rest of this file creates (those go inside each
        // GridContainer_* and have a Line_<island> name; ours are added
        // directly to Grid_MapMain). Tag-based discrimination avoids
        // having to subclass Line or maintain a parallel list of routes.
        private sealed record RouteOverlayTag(
            int? RouteNumber,
            string? FromIslandId,
            string? ToIslandId);
        private static readonly RouteOverlayTag ROUTE_OVERLAY_ONLY = new(null, null, null);
        private readonly HashSet<string> routeResolutionDiagnostics = new(StringComparer.Ordinal);
        private static readonly Brush[] AutomaticRouteBrushes = [
            Brushes.DeepSkyBlue,
            Brushes.Orange,
            Brushes.LimeGreen,
            Brushes.Violet,
            Brushes.Tomato,
            Brushes.Cyan,
            Brushes.Yellow,
            Brushes.HotPink,
        ];
        private static readonly double[][] AutomaticDashPatterns = [
            [4, 2],
            [8, 3],
            [2, 2],
        ];

        private static Brush ResolveHighlightBrush(RouteRenderSnapshot snapshot, string islandId) {
            if (snapshot.IsManual) return Brushes.Gold;
            var path = snapshot.Paths.FirstOrDefault(candidate => candidate.IslandIds.Contains(islandId));
            return path is null
                ? Brushes.DeepSkyBlue
                : AutomaticRouteBrushes[path.ColorIndex % AutomaticRouteBrushes.Length];
        }

        // Rebuilds the dashed-line overlay that visualises the ship's
        // current sailing route. The route is derived on the fly from
        // App.myCVM.CargoDetails (in its current order, which is what
        // SortByBarterChain / SolveOptimalRoute produced) prefixed with
        // Cox_Pirate as the fixed start island. Consecutive duplicate
        // islands are collapsed so a Cox_Pirate -> Cox_Pirate leg (when
        // the first cargo barter is AT Cox_Pirate) does not draw a
        // zero-length dot.
        //
        // Each leg is rendered as a dashed gold Line plus a small
        // gold Polygon arrowhead at the destination, rotated to match
        // the line's bearing so the sailing direction is obvious even
        // at a glance. Both Line and Polygon carry the ROUTE_LINE_TAG
        // so the cleanup pass below can recognise and remove them
        // without touching the unrelated per-island connector lines
        // (which live inside each GridContainer_*).
        //
        // The legs are added as the LAST children of Grid_MapMain so
        // they sit on top of the island blocks (small 10x10 rectangles)
        // - the visible portion of each line is across the map
        // background anyway, and being on top means a tight cluster of
        // island pins cannot hide the route segment passing through it.
        private void DrawRouteOverlay() {
            // Remove any previously drawn route overlay element. Tag
            // check keeps us from deleting the per-island connector
            // lines (which live inside GridContainer_* and never carry
            // our tag). Matches Line AND Polygon so the next pass
            // starts from a known-empty state.
            for (int i = Grid_MapMain.Children.Count - 1; i >= 0; i--) {
                if (Grid_MapMain.Children[i] is FrameworkElement elem
                    && elem.Tag is RouteOverlayTag) {
                    Grid_MapMain.Children.RemoveAt(i);
                }
            }

            var coordinator = App.myRouteCoordinator;
            IReadOnlyList<Barter> manualCargo = coordinator?.Mode == CargoMode.Manual
                ? App.myCVM.CargoDetails.ToList()
                : Array.Empty<Barter>();
            var snapshot = coordinator?.GetRenderSnapshot(manualCargo)
                ?? RouteRenderSnapshotFactory.CreateManual(manualCargo.Select(x => x.IsLandName).ToArray());
            var resolvedPaths = snapshot.Paths.Select(path => new {
                Path = path,
                Route = ResolveRoute(path, snapshot),
            }).Where(x => x.Route is { Count: >= 2 }).ToArray();
            var markerCounts = resolvedPaths
                .SelectMany(x => x.Route!)
                .GroupBy(island => island.IslandsName, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var markerOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var resolved in resolvedPaths) {
                var path = resolved.Path;
                var route = resolved.Route!;
                Brush stroke = snapshot.IsManual
                    ? Brushes.Gold
                    : AutomaticRouteBrushes[path.ColorIndex % AutomaticRouteBrushes.Length];
                DrawRoutePath(path.RouteNumber, route, stroke, markerCounts, markerOccurrences);
            }
        }

        private static RouteRenderSnapshot CurrentRenderSnapshot() {
            var coordinator = App.myRouteCoordinator;
            IReadOnlyList<Barter> manualCargo = coordinator?.Mode == CargoMode.Manual
                ? App.myCVM.CargoDetails.ToList()
                : Array.Empty<Barter>();
            return coordinator?.GetRenderSnapshot(manualCargo)
                ?? RouteRenderSnapshotFactory.CreateManual(manualCargo.Select(x => x.IsLandName).ToArray());
        }

        private List<Islands>? ResolveRoute(RouteRenderPath path, RouteRenderSnapshot snapshot) {
            var route = new List<Islands>(path.IslandIds.Count);
            foreach (string islandId in path.IslandIds) {
                var island = App.listIslands?.FirstOrDefault(x => x.IslandsName == islandId);
                if (island is null) {
                    string fingerprint = snapshot.IsManual
                        ? "manual"
                        : App.myRouteCoordinator?.CurrentPlan?.InputFingerprint ?? "automatic";
                    string key = fingerprint + "|" + islandId;
                    if (routeResolutionDiagnostics.Add(key))
                        App.myCFun.Log(LanguageService.Instance.Localize(
                            "str.Log.AutoRoute.MissingIsland", islandId), Brushes.OrangeRed);
                    return null;
                }
                route.Add(island);
            }
            return route;
        }

        private void DrawRoutePath(
            int routeNumber,
            IReadOnlyList<Islands> route,
            Brush stroke,
            IReadOnlyDictionary<string, int> markerCounts,
            Dictionary<string, int> markerOccurrences) {
            var coordinator = App.myRouteCoordinator;
            bool routeSelected = coordinator is null
                || coordinator.Mode != CargoMode.AutomaticRoute
                || coordinator.ShowAllRoutes
                || (!coordinator.ShowAllRoutes && coordinator.SelectedRouteNumber == routeNumber);
            for (int step = 0; step < route.Count; step++) {
                var island = route[step];
                if (!TryGetIslandCenter(island, out Grid? host, out Point center)
                    || host is null)
                    continue;
                int occurrenceIndex = markerOccurrences.GetValueOrDefault(island.IslandsName);
                markerOccurrences[island.IslandsName] = occurrenceIndex + 1;
                var offset = RouteStepMarkerLayout.OffsetFor(
                    occurrenceIndex, markerCounts[island.IslandsName]);
                bool markerFocused = App.myRouteCoordinator?.IsFocusedMarker(
                    routeNumber, island.IslandsName) == true;
                DrawRouteStepMarker(host, step + 1,
                    new Point(center.X + offset.X, center.Y + offset.Y),
                    stroke, routeSelected, markerFocused);
            }
            for (int i = 0; i < route.Count - 1; i++) {
                var fromIsland = route[i];
                var toIsland = route[i + 1];
                if (!TryGetIslandCenter(fromIsland, out Grid? fromHost, out Point from)
                    || !TryGetIslandCenter(toIsland, out Grid? toHost, out Point to)
                    || fromHost is null
                    || !ReferenceEquals(fromHost, toHost))
                    continue;
                var displayPath = RouteDisplayGeometry.BuildDirectLeg(
                    from, to);
                bool focused = App.myRouteCoordinator?.IsFocusedSegment(
                    routeNumber, fromIsland.IslandsName, toIsland.IslandsName) == true;
                for (int segment = 0; segment < displayPath.Count - 1; segment++)
                    DrawRouteSegment(fromHost, displayPath[segment], displayPath[segment + 1], stroke,
                        addArrow: segment == displayPath.Count - 2,
                        focused,
                        routeSelected,
                        routeNumber,
                        fromIsland.IslandsName,
                        toIsland.IslandsName);
            }
        }

        private void DrawRouteSegment(
            Grid host,
            Point from,
            Point to,
            Brush stroke,
            bool addArrow,
            bool focused,
            bool routeSelected,
            int routeNumber,
            string fromIslandId,
            string toIslandId) {
                double dx = to.X - from.X;
                double dy = to.Y - from.Y;
                if (dx == 0 && dy == 0) return;

                var visualStyle = RouteVisualStyle.For(routeSelected, focused, pulse: false);
                Brush displayStroke = focused ? Brushes.DeepPink : stroke;
                var line = new Line {
                    X1 = from.X,
                    Y1 = from.Y,
                    X2 = to.X,
                    Y2 = to.Y,
                    Stroke = displayStroke,
                    StrokeThickness = visualStyle.StrokeThickness,
                    StrokeDashArray = new DoubleCollection([4, 2]),
                    StrokeDashCap = PenLineCap.Round,
                    Tag = new RouteOverlayTag(routeNumber, fromIslandId, toIslandId),
                    IsHitTestVisible = false,
                    Opacity = visualStyle.Opacity,
                };
                if (focused) line.Effect = new DropShadowEffect {
                    Color = Colors.White,
                    BlurRadius = 10,
                    ShadowDepth = 0,
                    Opacity = 0.90,
                };
                host.Children.Add(line);

                if (visualStyle.ShowSelectionOverlay) {
                    var highlight = new Line {
                        X1 = from.X,
                        Y1 = from.Y,
                        X2 = to.X,
                        Y2 = to.Y,
                        Stroke = Brushes.White,
                        StrokeThickness = 1.1,
                        StrokeDashArray = new DoubleCollection([2, 2]),
                        Opacity = 0.92,
                        Tag = new RouteOverlayTag(routeNumber, fromIslandId, toIslandId),
                        IsHitTestVisible = false,
                    };
                    host.Children.Add(highlight);
                }

                if (App.myRouteCoordinator?.Mode == CargoMode.AutomaticRoute) {
                    double length = Math.Sqrt(dx * dx + dy * dy);
                    double endpointInset = Math.Min(14, Math.Max(0, length / 2 - 2));
                    double unitX = length > 0 ? dx / length : 0;
                    double unitY = length > 0 ? dy / length : 0;
                    var hitTarget = new Line {
                        X1 = from.X + unitX * endpointInset,
                        Y1 = from.Y + unitY * endpointInset,
                        X2 = to.X - unitX * endpointInset,
                        Y2 = to.Y - unitY * endpointInset,
                        Stroke = Brushes.Transparent,
                        StrokeThickness = 16,
                        Tag = new RouteOverlayTag(routeNumber, fromIslandId, toIslandId),
                        Cursor = Cursors.Hand,
                        IsHitTestVisible = true,
                    };
                    hitTarget.MouseLeftButtonDown += RouteSegment_MouseLeftButtonDown;
                    host.Children.Add(hitTarget);
                }

                if (!addArrow) return;
                double angleDeg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                var arrow = new Polygon {
                    Fill = displayStroke,
                    Stroke = displayStroke,
                    StrokeThickness = 1,
                    Points = new PointCollection {
                        new Point(0, 0),
                        new Point(-11, -5),
                        new Point(-11, 5),
                    },
                    Tag = new RouteOverlayTag(routeNumber, fromIslandId, toIslandId),
                    IsHitTestVisible = false,
                };
                var transforms = new TransformGroup();
                transforms.Children.Add(new RotateTransform(angleDeg));
                transforms.Children.Add(new TranslateTransform(to.X, to.Y));
                arrow.RenderTransform = transforms;
                host.Children.Add(arrow);
        }

        private void DrawRouteStepMarker(
            Grid host,
            int step,
            Point center,
            Brush stroke,
            bool routeSelected,
            bool focused) {
            const double diameter = 20;
            var marker = RouteStepMarkerFactory.CreateMarker(
                step, diameter, stroke, routeSelected, focused);
            marker.Opacity = routeSelected ? 1 : 0.32;
            marker.HorizontalAlignment = HorizontalAlignment.Left;
            marker.VerticalAlignment = VerticalAlignment.Top;
            marker.Margin = new Thickness(center.X + 7, center.Y - diameter - 7, 0, 0);
            marker.Tag = ROUTE_OVERLAY_ONLY;
            marker.IsHitTestVisible = false;
            Grid.SetZIndex(marker, 50);
            host.Children.Add(marker);
        }

        private void RouteSegment_MouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e) {
            if (sender is not FrameworkElement {
                    Tag: RouteOverlayTag {
                        RouteNumber: int routeNumber,
                        FromIslandId: string fromIslandId,
                        ToIslandId: string toIslandId,
                    }
                })
                return;
            if (App.myRouteCoordinator?.FocusRouteSegment(
                    routeNumber, fromIslandId, toIslandId) == true)
                App.myfmMain?.ActivateShipCargoSelection();
            e.Handled = true;
        }

        private static readonly NormalizedBounds LEFT_INSET_BOUNDS = new(0, 0, 0.3775, 0.3267);
        private static readonly NormalizedBounds RIGHT_INSET_BOUNDS = new(0.8175, 0, 1, 0.3267);
        private const double INSET_PADDING = 0.0125;

        private static NormalizedPoint GetDisplayCenterNormalized(Islands isl) {
            var group = IslandNavigationGeometry.GetDisplayGroup(isl.IslandsName);
            if (group is SpecialDisplayGroup.LeftInset or SpecialDisplayGroup.RightInset
                && App.listIslands != null) {
                var names = group == SpecialDisplayGroup.LeftInset
                    ? IslandNavigationGeometry.LeftInsetNames
                    : IslandNavigationGeometry.RightInsetNames;
                var points = App.listIslands
                    .Where(i => names.Contains(i.IslandsName) && i.HasNavigationCoordinates)
                    .ToDictionary(i => i.IslandsName, i => i.NavigationPoint, StringComparer.Ordinal);
                var bounds = group == SpecialDisplayGroup.LeftInset
                    ? LEFT_INSET_BOUNDS
                    : RIGHT_INSET_BOUNDS;
                var projected = IslandNavigationGeometry.ProjectToInset(points, bounds, INSET_PADDING);
                if (projected.TryGetValue(isl.IslandsName, out var point)) {
                    return point;
                }
            }

            var t = isl.IslandsThickness;
            return new NormalizedPoint(
                (t.Left + (1 - t.Right)) * 0.5,
                (t.Top + (1 - t.Bottom)) * 0.5);
        }

        // Pixel position of the displayed pin. Inset members use their
        // undistorted inset projection; main-map and bottom-edge members
        // retain the authored display centroid.
        private Point GetPosition(UIElement element, Grid host) {
            Point rootPoint = new Point(0, 0);
            try {
                if (host.IsLoaded) {
                    GeneralTransform transform = element.TransformToAncestor(host);
                    rootPoint = transform.Transform(new Point(0, 0));
                }
            }
            catch (Exception e) {
            }

            return rootPoint;
        }

        public void AdjustLabels(List<Label> labels) {
            bool adjusted;
            int iterations = 0;
            int maxIterations = labels.Count * 10; // 防止死循环的迭代次数上限

            do {
                adjusted = false;
                labels.Sort((a, b) => a.Margin.Top.CompareTo(b.Margin.Top));

                for (int i = 0; i < labels.Count - 1; i++) {
                    for (int j = i + 1; j < labels.Count; j++) {
                        if (AreOverlapping(labels[i], labels[j])) {
                            // Calculate distances to move down or up
                            double firstHeight = LabelPlacementHeight(labels[i]);
                            double secondHeight = LabelPlacementHeight(labels[j]);
                            double moveDownDistance = labels[i].Margin.Top + firstHeight - labels[j].Margin.Top;
                            double moveUpDistance = labels[j].Margin.Top - (labels[i].Margin.Top + firstHeight);


                            labels[j].Margin = new Thickness(labels[j].Margin.Left, labels[i].Margin.Top + firstHeight, labels[j].Margin.Right, labels[j].Margin.Bottom - secondHeight * 2);
                            adjusted = true;
                            // Determine the feasible and shorter movement
                            // bool canMoveDown = moveDownDistance > 0 && (labels[j].Margin.Top + moveDownDistance + labels[j].ActualHeight <= Grid_MapMain.ActualHeight);
                            // bool canMoveUp = moveUpDistance > 0 && (labels[j].Margin.Top - moveUpDistance >= 0);


                            // if (canMoveDown && (canMoveUp ? moveDownDistance < moveUpDistance : true)) {
                            //     labels[j].Margin = new Thickness(labels[j].Margin.Left, labels[i].Margin.Top + labels[i].ActualHeight, labels[j].Margin.Right, labels[j].Margin.Bottom - labels[j].ActualHeight);
                            //     adjusted = true;
                            // }
                            // else if (canMoveUp) {
                            //     labels[j].Margin = new Thickness(labels[j].Margin.Left, labels[i].Margin.Top - labels[j].ActualHeight, labels[j].Margin.Right, labels[j].Margin.Bottom - labels[j].ActualHeight);
                            //     adjusted = true;
                            // }
                        }
                    }
                }

                iterations++;
                if (iterations > maxIterations) {
                    Console.WriteLine("Stopping adjustments to prevent infinite loop.");
                    break;
                }
            } while (adjusted);


            // bool adjusted; // 标记是否有调整发生
            // int iterations = 0; // 记录循环的次数以避免死循环
            //
            // do {
            //     adjusted = false;
            //     // 按 Label 的上边界排序，以便顺序处理
            //     _labels.Sort((a, b) => a.Margin.Top.CompareTo(b.Margin.Top));
            //
            //     // 双层循环，比较每对 Label
            //     for (int i = 0; i < _labels.Count - 1; i++) {
            //         for (int j = i + 1; j < _labels.Count; j++) {
            //             // 如果两个 Label 重叠
            //             if (AreOverlapping(_labels[i], _labels[j])) {
            //                 // 计算向下或向上调整的距离
            //                 double moveDownDistance = _labels[i].Margin.Top + _labels[i].ActualHeight - _labels[j].Margin.Top;
            //                 double moveUpDistance = _labels[j].Margin.Top + _labels[j].ActualHeight - _labels[i].Margin.Top;
            //
            //                 // 选择移动距离较小的方向调整位置
            //                 if (moveDownDistance <= moveUpDistance) {
            //                     double newTop = _labels[i].Margin.Top + _labels[i].ActualHeight;
            //                     // 确保调整后不超出容器底部
            //                     if (newTop + _labels[j].ActualHeight <= Grid_MapMain.ActualHeight) {
            //                         _labels[j].Margin = new Thickness(_labels[j].Margin.Left, newTop, _labels[j].Margin.Right, _labels[j].Margin.Bottom - _labels[j].ActualHeight);
            //                         adjusted = true;
            //                     }
            //                     else {
            //                         // 如果超出容器底部，设置在底部
            //                         newTop = Grid_MapMain.ActualHeight - _labels[j].ActualHeight;
            //                         _labels[j].Margin = new Thickness(_labels[j].Margin.Left, newTop, _labels[j].Margin.Right, _labels[j].Margin.Bottom - _labels[j].ActualHeight);
            //                         adjusted = true;
            //                     }
            //                 }
            //                 else if (_labels[i].Margin.Top - moveUpDistance >= 0) {
            //                     // 向上调整，并确保不会产生负的边距
            //                     double newTop = _labels[i].Margin.Top - moveUpDistance;
            //                     _labels[j].Margin = new Thickness(_labels[j].Margin.Left, newTop, _labels[j].Margin.Right, _labels[j].Margin.Bottom - _labels[j].ActualHeight);
            //                     adjusted = true;
            //                 }
            //
            //                 // 如果有调整发生，跳出内层循环并重新开始排序和检查
            //                 if (adjusted)
            //                     break;
            //             }
            //         }
            //     }
            //
            //     // 增加迭代次数
            //     iterations++;
            //     // 设置一个迭代次数的阈值，超过这个值则停止循环，避免无限循环
            //     if (iterations > _labels.Count * 2) {
            //         Console.WriteLine("Stopping adjustments to prevent infinite loop.");
            //         break;
            //     }
            // } while (adjusted);


            // bool adjusted;
            // do {
            //     adjusted = false;
            //     // 按 Margin.Top 属性排序 Label 列表
            //     _labels.Sort((a, b) => a.Margin.Top.CompareTo(b.Margin.Top));
            //
            //     for (int i = 0; i < _labels.Count - 1; i++) {
            //         for (int j = i + 1; j < _labels.Count; j++) {
            //             if (AreOverlapping(_labels[i], _labels[j])) {
            //                 // 计算重叠解决后的新顶部位置
            //                 double newTop = _labels[i].Margin.Top + _labels[i].ActualHeight;
            //                 if (newTop > _labels[j].Margin.Top) {
            //                     _labels[j].Margin = new Thickness(_labels[j].Margin.Left, newTop, _labels[j].Margin.Right, _labels[j].Margin.Bottom - _labels[j].ActualHeight);
            //                     adjusted = true;
            //                 }
            //             }
            //         }
            //     }
            // } while (adjusted); // 如果有调整发生，重新检查所有Label


            // // 按 Margin.Top 属性排序 Label 列表
            // _labels.Sort((a, b) => a.Margin.Top.CompareTo(b.Margin.Top));
            //
            //
            // // 遍历 Label 列表，检查并解决重叠问题
            // for (int i = 0; i < _labels.Count; i++) {
            //     Label current = _labels[i];
            //
            //     for (int j = i + 1; j < _labels.Count; j++) {
            //         Label next = _labels[j];
            //
            //         // 检查是否重叠并且在水平方向上有重叠
            //         if (AreOverlapping(current, next)) {
            //             // 重叠，调整下一个 Label 的位置
            //             double currentBottom = current.Margin.Top + current.ActualHeight;
            //             double newTopMargin = currentBottom;
            //             next.Margin = new Thickness(next.Margin.Left, newTopMargin, next.Margin.Right, next.Margin.Bottom-next.ActualHeight);
            //
            //             // 由于调整了位置，可能需要重新检查当前label之前所有label
            //             i = -1;  // 重新开始循环，但因为循环会 i++，所以设置为 -1
            //             break;
            //         }
            //     }
            // }
        }

        private bool AreOverlapping(Label _label1, Label _label2) {
            double left1 = _label1.Margin.Left;
            double right1 = left1 + LabelPlacementWidth(_label1);
            double top1 = _label1.Margin.Top;
            double bottom1 = top1 + LabelPlacementHeight(_label1);

            double left2 = _label2.Margin.Left;
            double right2 = left2 + LabelPlacementWidth(_label2);
            double top2 = _label2.Margin.Top;
            double bottom2 = top2 + LabelPlacementHeight(_label2);

            bool horizontalOverlap = (left1 < right2 && right1 > left2);
            bool verticalOverlap = (top1 < bottom2 && bottom1 > top2);

            return horizontalOverlap && verticalOverlap;
        }

        private static double LabelPlacementWidth(Label label) {
            if (!Double.IsNaN(label.Width) && label.Width > 0) return label.Width;
            if (label.DesiredSize.Width > 0) return label.DesiredSize.Width;
            return label.ActualWidth;
        }

        private static double LabelPlacementHeight(Label label) {
            if (!Double.IsNaN(label.Height) && label.Height > 0) return label.Height;
            if (label.DesiredSize.Height > 0) return label.DesiredSize.Height;
            return label.ActualHeight;
        }

        private static Size MeasureLabelForPlacement(Label label) {
            // Measure the natural size after a route style change, but leave Arrange
            // to the parent panel. Manually arranging this live child bypasses its
            // Margin and made the 100 ms layout timer move labels back and forth.
            Thickness originalMargin = label.Margin;
            label.Margin = new Thickness(0);
            label.Width = Double.NaN;
            label.Height = Double.NaN;
            label.InvalidateMeasure();
            label.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            Size measured = new Size(
                Math.Ceiling(label.DesiredSize.Width),
                Math.Ceiling(label.DesiredSize.Height));
            label.Margin = originalMargin;
            label.Width = measured.Width;
            label.Height = measured.Height;
            return measured;
        }

        private void NewMargin(Label _label, FrameworkElement? host = null) {
            double labelWidth = LabelPlacementWidth(_label);
            double labelHeight = LabelPlacementHeight(_label);
            double rightEdge = _label.Margin.Left + labelWidth;
            double bottomEdge = _label.Margin.Top + labelHeight;

            double newLeftMargin = _label.Margin.Left;
            double newTopMargin = _label.Margin.Top;

            // 检查并调整右边界
            double availableWidth = host?.ActualWidth > 0 ? host.ActualWidth : this.ActualWidth;
            double availableHeight = host?.ActualHeight > 0 ? host.ActualHeight : this.ActualHeight;
            if (rightEdge > availableWidth) {
                newLeftMargin = availableWidth - labelWidth;
                newLeftMargin = Math.Max(0, newLeftMargin); // 避免负边距
            }

            // 检查左边界
            if (_label.Margin.Left < 0) {
                newLeftMargin = 0; // 确保 Label 不超出左边界
            }

            // 检查并调整底边界
            if (bottomEdge > availableHeight) {
                newTopMargin = availableHeight - labelHeight;
                newTopMargin = Math.Max(0, newTopMargin); // 避免负边距
            }

            // 应用新的边距
            _label.Margin = new Thickness(newLeftMargin, newTopMargin, _label.Margin.Right, _label.Margin.Bottom);
        }

        private Grid FindGrid(Grid _grid, string _name) {
            foreach (var child in _grid.Children) {
                if (child is Grid && ((Grid)child).Name == _name) {
                    return (Grid)child;
                }
            }

            return null;
        }

        private Image FindImage(Grid _grid, string _name) {
            foreach (var child in _grid.Children) {
                if (child is Image && ((Image)child).Name == _name) {
                    return (Image)child;
                }
            }

            return null;
        }

        private Label FindLabel(Grid _grid, string _name) {
            foreach (var child in _grid.Children) {
                if (child is Label && ((Label)child).Name == _name) {
                    return (Label)child;
                }
            }

            return null;
        }

        private Line FindLine(Grid _grid, string _name) {
            foreach (var child in _grid.Children) {
                if (child is Line && ((Line)child).Name == _name) {
                    return (Line)child;
                }
            }

            return null;
        }

        // Return a light-tinted brush derived from the input colour,
        // so the text label remains readable on the dark map
        // background regardless of which group colour was passed in.
        // Strategy: invert lightness (light colours become darker,
        // dark colours become lighter) so the text contrasts with
        // both the dark navy map background AND the island block
        // (which uses the input brush colour directly).
        private static Brush LightenForMapBg(Brush _brush) {
            if (_brush is SolidColorBrush scb) {
                Color c = scb.Color;
                // Compute perceived lightness, then flip toward 1.0
                // for dark inputs and 0.0 for light inputs. Target a
                // brightness that contrasts with both the map (very
                // dark navy) and the caller's brush (saturated, mid-).
                double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
                double target;
                if (lum < 0.5) {
                    // dark input -> push to a light/pale tint of the
                    // same hue so it still reads as part of the group
                    target = Math.Min(1.0, lum + 0.7);
                }
                else {
                    // already-light input -> keep light, push even paler
                    target = Math.Min(1.0, lum + 0.15);
                }
                byte r = (byte)Math.Min(255, (int)(c.R * (target / Math.Max(0.001, lum))));
                byte g = (byte)Math.Min(255, (int)(c.G * (target / Math.Max(0.001, lum))));
                byte b = (byte)Math.Min(255, (int)(c.B * (target / Math.Max(0.001, lum))));
                return new SolidColorBrush(Color.FromRgb(r, g, b));
            }
            // Non-SolidColorBrush (e.g. linear gradient) - fall back
            // to white so the text is always readable.
            return Brushes.White;
        }

        private Brush GetBursh(Barter _barter) {
            switch (_barter.BarterGroup) {
                case -1:
                    return Brushes.Bisque;
                    break;
                case 0:
                    return Brushes.Aquamarine;
                    break;
                case 1:
                    return Brushes.CornflowerBlue;
                    break;
                case 2:
                    return Brushes.DarkCyan;
                    break;
                case 3:
                    return Brushes.CadetBlue;
                    break;
                case 4:
                    return Brushes.Chocolate;
                    break;
                case 5:
                    return Brushes.BurlyWood;
                    break;
                case 6:
                    return Brushes.DarkTurquoise;
                    break;
                case 7:
                    return Brushes.Ivory;
                    break;
                case 8:
                    return Brushes.DeepSkyBlue;
                    break;
                case 9:
                    return Brushes.DarkSalmon;
                    break;
                case 10:
                    return Brushes.LawnGreen;
                    break;
                case 11:
                    return Brushes.Orchid;
                    break;
                case 12:
                    return Brushes.Olive;
                    break;
                case 13:
                    return Brushes.Orange;
                    break;
                case 14:
                    return Brushes.Plum;
                    break;
                case 15:
                    return Brushes.DarkViolet;
                    break;
                case 16:
                    return Brushes.Yellow;
                    break;
                case 17:
                    return Brushes.Tomato;
                    break;
                case 18:
                    return Brushes.MediumSlateBlue;
                    break;
                case 19:
                    return Brushes.SpringGreen;
                    break;
                case 20:
                    return Brushes.SandyBrown;
                    break;
                default:
                    return Brushes.RoyalBlue;
                    break;
            }
        }

        public void IslandsButtonInitialisation(Barter _barter, Brush _brush) {
            ButtonInitialisation(_barter, _brush);
        }

        public void IslandsButtonInitialisation() {
            Grid_MapMain.Children.Clear();

            listGrid_Islands = new List<Grid>();

            InitTempGrid();
            // Filter barters by completeness + LV ceiling. With MAX_MAP_LV=7 (post Phase
            // A+B), every legitimate barter in the catalog is shown; the hook is in
            // place for future tier filtering without touching this site again.
            foreach (Barter myBarter in App.myPVM.BarterCollection.Where(b =>
                b.ExchangeDone == false && b.ExchangeQuantity > 0 &&
                (!int.TryParse(b.Item1?.ItemLV, out int lv) || lv <= MAX_MAP_LV))) {
                ButtonInitialisation(myBarter, GetBursh(myBarter));
            }
            EnsureAutomaticWarehouseNodes();
        }

        private void ButtonInitialisation(
            Barter _barter,
            Brush _brush,
            bool isWarehouse = false,
            string? warehouseLabel = null) {
            // Computed once and reused everywhere below instead of
            // repeating the Item1Name/Item2Name check (and instead of
            // re-deriving "temp-ness" later from a Name string), so a
            // Temp placeholder island is unambiguously identified.
            bool isTemp = !isWarehouse && !(_barter.Item1Name != "" && _barter.Item2Name != null);
            Grid? overlayHost = GetOverlayHost(_barter.IsLand);
            if (overlayHost is null) return;

            Grid myGrid_Container = new Grid();

            if (!isTemp && !isWarehouse) {
                myGrid_Container.Name = "GridContainer_" + _barter.IsLandName;
                myGrid_Container.MouseLeftButtonDown += Islands_MouseLeftButtonDown;
                myGrid_Container.MouseRightButtonDown += Islands_MouseRightButtonDown;
                myGrid_Container.MouseDown += MyGrid_Container_MouseDown;
            }
            else if (isWarehouse) {
                myGrid_Container.Name = "GridContainer_" + _barter.IsLandName + "Warehouse";
            }
            else {
                myGrid_Container.Name = "GridContainer_" + _barter.IsLandName + "Temp";
            }

            Grid myGrid_Image = new Grid();
            if (!isTemp && !isWarehouse) {
                myGrid_Image.Name = "GridImage_" + _barter.IsLandName;
            }
            else if (isWarehouse) {
                myGrid_Image.Name = "GridImage_" + _barter.IsLandName + "Warehouse";
            }
            else {
                myGrid_Image.Name = "GridImage_" + _barter.IsLandName + "Temp";
            }


            TryGetIslandCenter(_barter.IsLand, out _, out Point islandCenter);
            myGrid_Image.Margin = new Thickness(
                islandCenter.X - 5,
                islandCenter.Y - 5,
                overlayHost.ActualWidth - islandCenter.X - 5,
                overlayHost.ActualHeight - islandCenter.Y - 5);
            myGrid_Image.Width = 10;
            myGrid_Image.Height = 10;

            Rectangle myRectangle = new Rectangle();
            myRectangle.Fill = _brush;
            if (isWarehouse) {
                myRectangle.Stroke = Brushes.Gold;
                myRectangle.StrokeThickness = 2;
            }

            Label myLabel = new Label();
            if (!isTemp && !isWarehouse) {
                myLabel.Name = "Label_" + _barter.IsLand.IslandsName;
            }
            else if (isWarehouse) {
                myLabel.Name = "Label_" + _barter.IsLand.IslandsName + "Warehouse";
            }
            else {
                myLabel.Name = "Label_" + _barter.IsLand.IslandsName + "Temp";
            }

            myLabel.UpdateLayout();
            // Per-group readable color scheme. The caller passes a brush
            // (_brush) to colour the island block, but the text label needs
            // a colour that contrasts with BOTH the dark navy map background
            // AND the dim island block. Pick a light tint derived from the
            // caller's brush so the text visually ties back to the block.
            myLabel.Foreground = LightenForMapBg(_brush);
            // Foreground: light tint of the group colour so the text reads
            // on the dark navy map background.
            myLabel.Foreground = LightenForMapBg(_brush);
            //myLabel.Foreground = Brushes.Red;
            // Background: a semi-transparent black panel under the text so it
            // remains readable over the busy map (textures, other labels,
            // lines). Empty-content labels (islands not in any active
            // barter) get Brushes.Transparent so no dark rectangle shows
            // up next to the island (that was the "shadow" artifact).
                        // No background panel: the light-tint text colour
            // (LightenForMapBg) already gives high contrast on the dark
            // navy map background, so any panel is just visual weight.
            // The user flagged the dark rectangle next to the label as a
            // "shadow" artifact - gone entirely. Empty-content labels
            // also get null so no rectangle renders.
            // Re-add a very subtle panel only for labels WITH text.
            // Empty-content labels stay null (they're Collapsed above so
            // they don't render at all). Use a lighter alpha (40 = 16%
            // opaque) to avoid the "shadow" complaint - just enough
            // background contrast to make the light-tint text readable,
            // not a heavy block.
            myLabel.Background = myLabel.Content == ""
                ? null
                // 80 = 31% opaque (up from 40/16%) per user request for
                // a slightly darker background for better text readability.
                : new SolidColorBrush(Color.FromArgb(80, 0, 0, 0));

            myLabel.HorizontalAlignment = HorizontalAlignment.Left;
            myLabel.VerticalAlignment = VerticalAlignment.Top;
            if (isWarehouse) {
                myLabel.Content = warehouseLabel ?? _barter.IsLand.IslandsNameDisplay;
                myLabel.Foreground = Brushes.Gold;
                myLabel.Background = new SolidColorBrush(Color.FromArgb(120, 5, 22, 30));
            }
            else if (_barter.Item1Name != "" && _barter.Item2Name != "") {
                // Phase 6 (i18n): label format lives in the resource dictionary
                // (str.Map.LabelFormat) so it can flip to a different layout
                // in another language.  For now both en-US and zh-TW share
                // the same shape ("[{0}] {1} => {2} [{3}]") because the
                // arrow is a visual symbol; item names flip independently
                // via Barter.Item1NameDisplay / Item2NameDisplay.
                string fmt = Localization.LanguageService.Instance?.Localize("str.Map.LabelFormat")
                              ?? "[{0}] {1} => {2} [{3}]";
                myLabel.Content = string.Format(
                    fmt,
                    _barter.Item1Number * _barter.ExchangeQuantity,
                    _barter.Item1NameDisplay,
                    _barter.Item2NameDisplay,
                    _barter.Item2Number * _barter.ExchangeQuantity);
            }
            else {
                myLabel.Content = "";
            }
            //myLabel.Margin = new Thickness(double.Max(0, Grid_MapMain.Margin.Left - myLabel.ActualWidth), Grid_MapMain.Margin.Top + myGrid_Image.ActualHeight, double.Min(Grid_MapMain.Margin.Right, Grid_MapMain.Margin.Right + myLabel.ActualWidth), Grid_MapMain.Margin.Top + myLabel.ActualHeight);

            if (myLabel.ActualWidth != 0 && myLabel.ActualHeight != 0) {
                myLabel.Width = myLabel.ActualWidth;
                myLabel.Height = myLabel.ActualHeight;
            }
            else {
                var size = new Size(Double.PositiveInfinity, Double.PositiveInfinity);
                myLabel.Measure(size);
                myLabel.Arrange(new Rect(myLabel.DesiredSize));
                myLabel.Width = myLabel.ActualWidth;
                myLabel.Height = myLabel.ActualHeight;
            }

            myLabel.Margin = new Thickness(myGrid_Image.Margin.Left - myLabel.Width / 2, myGrid_Image.Margin.Top + myGrid_Image.ActualHeight, myGrid_Image.Margin.Right - myLabel.Width, myGrid_Image.Margin.Bottom - myLabel.Height);

            NewMargin(myLabel, overlayHost);

            myGrid_Container.Children.Add(myGrid_Image);
            myGrid_Image.Children.Add(myRectangle);
            if (myLabel.Content != "")
                myGrid_Container.Children.Add(myLabel);
            else
                myLabel.Visibility = Visibility.Collapsed;

            Line myLine = null;
            // Bug fix: this used to check _barter.IsLand.IslandsName.Contains("Temp"),
            // but IslandsName is just the enum name (never has a "Temp" suffix), so
            // that condition was always true and a connector line got added even to
            // Temp placeholders (which have no visible label to connect to).
            if (!isTemp && !isWarehouse) {
                myLine = new Line();
                myLine.Name = "Line_" + _barter.IsLand.IslandsName;
                // Use the same light-tint derived from the group brush
                // as the text label, so the connector stays visible on the
                // dark map background and visually links to its group.
                myLine.Stroke = LightenForMapBg(_brush);
                //myLine.Stroke = Brushes.Red;
                myLine.StrokeThickness = 1.5;

                double x = myGrid_Image.ActualWidth / 2;
                double y1 = myGrid_Image.ActualHeight;
                double y2 = myGrid_Image.Margin.Top + myGrid_Image.ActualHeight;


                myLine.X1 = x;
                myLine.X2 = x;
                myLine.Y1 = y1;
                myLine.Y2 = y2;
                myGrid_Container.Children.Add(myLine);
            }

            // Stash direct references to this island's visual parts on the
            // container's Tag. IslandsButtonRearrange reads this instead of
            // re-parsing Grid.Name substrings/suffixes - that string-based
            // lookup was fragile and could fail silently, leaving Temp
            // placeholders (and potentially real islands) stuck at their
            // construction-time position after a window resize.
            myGrid_Container.Tag = new IslandVisual {
                Islands = _barter.IsLand,
                IsTemp = isTemp,
                IsWarehouse = isWarehouse,
                Host = overlayHost,
                ImageGrid = myGrid_Image,
                Label = myLabel,
                Line = myLine,
                Rectangle = myRectangle,
                BaseBackground = myLabel.Background,
                BaseBorderBrush = myLabel.BorderBrush,
                BaseBorderThickness = myLabel.BorderThickness,
                BaseFontWeight = myLabel.FontWeight,
                BaseFontSize = myLabel.FontSize,
                BaseRectangleStroke = myRectangle.Stroke,
                BaseRectangleStrokeThickness = myRectangle.StrokeThickness,
            };

            overlayHost.Children.Add(myGrid_Container);

            if (listGrid_Islands.FirstOrDefault(b => b.Name == "GridContainer_" + _barter.IsLand.IslandsName) == null) {
                listGrid_Islands.Add(myGrid_Container);
            }

            //IslandsButtonRearrange();
        }

        private void EnsureAutomaticWarehouseNodes() {
            var snapshot = CurrentRenderSnapshot();
            if (App.myRouteCoordinator?.Mode != CargoMode.AutomaticRoute) return;
            foreach (string islandId in snapshot.WarehouseIslandIds.OrderBy(x => x, StringComparer.Ordinal)) {
                var island = App.listIslands?.FirstOrDefault(candidate => candidate.IslandsName == islandId);
                if (island is null) continue;
                var roles = App.myRouteCoordinator.CurrentPlan?.Routes
                    .Where(route => App.myRouteCoordinator.ShowAllRoutes
                        || route.Number == App.myRouteCoordinator.SelectedRouteNumber)
                    .SelectMany(route => route.Steps)
                    .Where(step => step.IslandId == islandId)
                    .ToArray() ?? [];
                bool pickup = roles.Any(step => step is WarehousePickupStep);
                bool unload = roles.Any(step => step is WarehouseUnloadStep);
                string role = pickup && unload ? "装货/卸货" : pickup ? "装货" : "卸货";
                string label = $"{island.IslandsNameDisplay} · {role}";

                var existingGrid = listGrid_Islands.FirstOrDefault(grid =>
                    grid.Tag is IslandVisual visual && visual.Islands.IslandsName == islandId);
                if (existingGrid?.Tag is IslandVisual existing) {
                    existing.IsWarehouse = true;
                    existing.IsTemp = false;
                    existing.Rectangle.Stroke = Brushes.Gold;
                    existing.Rectangle.StrokeThickness = 2;
                    existing.BaseRectangleStroke = Brushes.Gold;
                    existing.BaseRectangleStrokeThickness = 2;
                    if (string.IsNullOrWhiteSpace(existing.Label.Content?.ToString())) {
                        existing.Label.Content = label;
                        existing.Label.Foreground = Brushes.Gold;
                        existing.Label.Visibility = Visibility.Visible;
                        if (!existingGrid.Children.Contains(existing.Label)) existingGrid.Children.Add(existing.Label);
                    }
                    continue;
                }

                var marker = new Barter(
                    island, new Items("", "000", "0"), new Items("", "000", "0"),
                    0, false, 0, 0, 0);
                ButtonInitialisation(marker, Brushes.DarkGoldenrod, true, label);
            }
        }

        private void MyGrid_Container_MouseDown(object sender, MouseButtonEventArgs e) {
            if (e.ChangedButton == MouseButton.Middle && e.ButtonState == MouseButtonState.Pressed) {
                App.myRouteCoordinator?.ActivateManual();
                var clickedGrid = sender as Grid;
                if (clickedGrid != null) {
                    Barter myBarter = App.myPVM.BarterCollection.FirstOrDefault(b => b.IsLandName == clickedGrid.Name.Substring(14, clickedGrid.Name.Length - 14))!;
                    if (App.myCVM.CargoDetails.FirstOrDefault(b => b.IsLandName == myBarter.IsLandName) == null) {
                        App.myCVM.CargoDetails.Add(myBarter);
                    }
                    else {
                        App.myCVM.CargoDetails.Remove(App.myCVM.CargoDetails.FirstOrDefault(b => b.IsLandName == myBarter.IsLandName));
                    }

                    App.myfmMain.myShipCargo.UpdateCurrentLV();
                    App.myfmMain.myShipCargo.SaveData();
                    // Re-render map labels so the middle-clicked point
                    // becomes Bold (or reverts) immediately - without this
                    // call, the label FontWeight would never update and
                    // the bold highlight only appeared on the next map
                    // rebuild (e.g. via UpdateMapControl).
                    App.myfmMain.myMapControl.IslandsButtonInitialisation();
                }
            }
        }

        private void Islands_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            var clickedGrid = sender as Grid;
            if (clickedGrid != null && e.ClickCount == 2) {
                Barter myBarter = App.myPVM.BarterCollection.FirstOrDefault(b => b.IsLandName == clickedGrid.Name.Substring(14, clickedGrid.Name.Length - 14))!;
                myBarter.ExchangeDone = true;
                App.myfmMain.myPlannerControl.Grouping();
                App.myfmMain.myPlannerControl.SaveData();
                App.myRouteCoordinator?.RefreshCompletedBarters(App.myPVM.BarterCollection);
                if (App.myCVM.CargoDetails.FirstOrDefault(b => b.IsLandName == myBarter.IsLandName) != null) {
                    App.myCVM.CargoDetails.Remove(App.myCVM.CargoDetails.FirstOrDefault(b => b.IsLandName == myBarter.IsLandName));
                    App.myfmMain.myShipCargo.UpdateCurrentLV();
                    App.myfmMain.myShipCargo.SaveData();
                }
            }
        }

        private void Islands_MouseRightButtonDown(object sender, MouseButtonEventArgs e) {
            var clickedGrid = sender as Grid;
            if (clickedGrid != null) {
                Barter? myBarter = App.myPVM.BarterCollection.FirstOrDefault(b => b.IsLandName == clickedGrid.Name.Substring(14, clickedGrid.Name.Length - 14));
                if (myBarter != null) {
                    App.myfmMain.dockingManager_Main.ActiveWindow = App.myfmMain.document_Planner;
                    App.myfmMain.myPlannerControl.DataGrid_Planner.SelectedItem = myBarter;

                    App.myfmMain.myPlannerControl.DataGrid_Planner.ScrollInView(new RowColumnIndex(App.myfmMain.myPlannerControl.DataGrid_Planner.SelectedIndex, 0));
                }
            }
        }
    }
}
