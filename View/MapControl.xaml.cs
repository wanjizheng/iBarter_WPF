using Syncfusion.Windows.Controls.PivotGrid;
using Syncfusion.UI.Xaml.Grid;
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
        private static readonly FontWeight MapLabelFontWeight = FontWeights.Medium;
        private static readonly FontWeight HighlightedMapLabelFontWeight = FontWeights.SemiBold;
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
        // Was private before the resize-reflow fix. Made internal so the
        // pure helpers extracted into View/MapControl.Reflow.cs can
        // carry IslandVisual in their signatures (and so the test
        // project under Tools/AutomaticRoutePlanningTests can pin the
        // cleanup helper against a real IslandVisual instance).
        internal class IslandVisual {
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
                // Audit round 5: schedule an island-label rebuild at
                // Render priority. The first call almost always lands
                // before layout; the retry path inside
                // EnsureRouteIslandLabels fires the actual create
                // once Grid_MapMain has ActualWidth/ActualHeight > 0.
                Dispatcher.BeginInvoke(new Action(EnsureRouteIslandLabels),
                    DispatcherPriority.Render);
                // Geometry refresh on tab activation — the timer was
                // stopped in Unloaded, so this catches the first
                // paint after the user shows the map tab again.
                ScheduleMapOverlayReflow();
            };
            this.Unloaded += (_, _) => {
                EndMapPan();
                if (myTimer != null && myTimer.IsEnabled) myTimer.Stop();
            };
            // Pure-geometry changes funnel through the single
            // ScheduleMapOverlayReflow() pipeline. Both the inner
            // Grid_MapMain and the outer MapViewport fire
            // SizeChanged when the user resizes the window or the
            // dock layout changes; the pipeline coalesces a burst
            // of events into one deferred pass that reads the
            // latest ActualWidth/ActualHeight after layout.
            Grid_MapMain.SizeChanged += (_, _) => ScheduleMapOverlayReflow();
            MapViewport.SizeChanged += (_, _) => {
                if (hdMapEnabled) RefreshHdMap();
                CenterFocusedSegmentIfNeeded();
                ScheduleMapOverlayReflow();
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
            // Timer is the safety net for events that don't fire
            // SizeChanged (e.g. docking-tab unload-then-show, or a
            // font/theme change that only invalidates measured
            // bounds). Route through the same coalescing pipeline
            // so a burst of timer ticks collapses to one reflow.
            ScheduleMapOverlayReflow();
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
            EndMapPan();
            lastPanPoint = e.GetPosition(MapViewport);
            if (!MapViewport.CaptureMouse()) return;
            isPanningMap = true;
            e.Handled = true;
        }

        private void MapViewport_MouseMove(object sender, MouseEventArgs e) {
            if (!isPanningMap) return;
            if (e.LeftButton != MouseButtonState.Pressed) {
                // Mouse-up can be consumed by window chrome or a docking-tab
                // transition. Movement with the button already released is a
                // reliable final safety net for a missed up event.
                EndMapPan();
                return;
            }
            if (!MapViewport.IsMouseCaptured) {
                isPanningMap = false;
                return;
            }
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
            bool wasPanning = isPanningMap || MapViewport.IsMouseCaptured;
            EndMapPan();
            if (wasPanning) e.Handled = true;
        }

        private void MapViewport_LostMouseCapture(object sender, MouseEventArgs e) {
            isPanningMap = false;
        }

        private void EndMapPan() {
            isPanningMap = false;
            if (MapViewport.IsMouseCaptured) {
                MapViewport.ReleaseMouseCapture();
            }
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
                // RefreshHdMap is now SOLELY responsible for camera
                // constraint + tile refresh; overlay reflow is the
                // pipeline's job (it knows about route-step labels
                // too, which RefreshHdMap never did).
                RefreshHdMap();
                ScheduleMapOverlayReflow();
                return;
            }
            MapScaleTransform.ScaleX = viewportState.Scale;
            MapScaleTransform.ScaleY = viewportState.Scale;
            MapTranslateTransform.X = viewportState.OffsetX;
            MapTranslateTransform.Y = viewportState.OffsetY;
            // Audit round 6 + reflow fix: zoom/pan must trigger the
            // full overlay reflow so route-step labels and warehouse
            // labels both follow the new projected centre.
            ScheduleMapOverlayReflow();
        }

        private double GetLabelScreenFontSize(bool highlighted) {
            if (hdMapEnabled) return 13 + (highlighted ? 2 : 0);
            double requested = 13 * Math.Sqrt(viewportState.Scale) + (highlighted ? 2 : 0);
            return Math.Clamp(requested, 11, 16);
        }

        // Both legacy warehouse Labels and automatic-route TextBlocks
        // live below the map scale transform. Use one effective-font
        // calculation so they have the same on-screen size at every zoom.
        private double GetMapLabelFontSize(bool highlighted) =>
            GetLabelScreenFontSize(highlighted)
                / (hdMapEnabled ? 1 : viewportState.Scale);

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

            // Resize fix: extract the stale-visual cleanup into a pure helper
            // so the decision logic + the actual list mutation can
            // both be unit-tested. The helper returns strictly
            // descending indices, and RemoveAtDescendingIndices
            // walks them in that order — there is NO additional
            // reverse in this caller. The beforeRemove callback
            // detaches the grid from its parent Panel BEFORE the
            // list mutation shifts later indices.
            var staleIndices = MapIslandVisualCleanup.CollectStaleIndices<Grid>(
                listGrid_Islands,
                grid => BuildCleanupSnapshot(grid, renderSnapshot),
                snap => !snap.IsTemp
                    && !App.myPVM.BarterCollection.Any(b =>
                        b.ExchangeDone == false &&
                        b.ExchangeQuantity > 0 &&
                        b.IsLandName == snap.IslandId)
                    && !renderSnapshot.WarehouseIslandIds.Contains(snap.IslandId));
            MapIslandVisualCleanup.RemoveAtDescendingIndices<Grid>(
                listGrid_Islands,
                staleIndices,
                beforeRemove: grid => {
                    if (grid.Parent is Panel parent) {
                        parent.Children.Remove(grid);
                    }
                });

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
                        myLabel.FontWeight = HighlightedMapLabelFontWeight;
                        myLabel.FontSize = GetMapLabelFontSize(true);
                        myLabel.BorderBrush = highlightBrush;
                        myLabel.BorderThickness = new Thickness(2);
                        myLabel.Background = new SolidColorBrush(Color.FromArgb(155, 5, 22, 30));
                        visual.Rectangle.Stroke = highlightBrush;
                        visual.Rectangle.StrokeThickness = 3;
                    }
                    else {
                        myLabel.FontWeight = visual.BaseFontWeight;
                        myLabel.FontSize = GetMapLabelFontSize(false);
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

                    PositionIslandLabel(visual, host, center, labelSize);
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

        private void PositionIslandLabel(
            IslandVisual visual,
            Grid host,
            Point center,
            Size labelSize) {
            Grid image = visual.ImageGrid;
            Label label = visual.Label;
            double imageHeight = image.ActualHeight > 0 ? image.ActualHeight : image.Height;
            double belowIslandTop = image.Margin.Top + imageHeight;
            double aboveIslandTop = image.Margin.Top - labelSize.Height;

            // Preserve the existing bottom-edge flip for ordinary labels.
            double labelTop = host.ActualHeight - center.Y
                    < Math.Max(40, host.ActualHeight * 0.08)
                ? aboveIslandTop
                : belowIslandTop;

            if (visual.IsWarehouse) {
                // Barter labels live in a separate route-step overlay. They
                // are not part of legacy AdjustLabels(), so use their actual
                // rendered bottom edge to place pickup/unload text in the next
                // free vertical slot. This covers the final barter + unload
                // on the same island without guessing from label text.
                double? lowestBarterBottom = FindLowestBarterLabelBottom(
                    host, visual.Islands.IslandsName);
                labelTop = iBarter.Routing.RouteStepLabelRenderer.ComputeWarehouseLabelTop(
                    labelTop,
                    aboveIslandTop,
                    lowestBarterBottom,
                    labelSize.Height,
                    host.ActualHeight);
            }

            double visibleWidth = GetUnobscuredMapWidth(host);
            double labelLeft = iBarter.Routing.RouteStepLabelRenderer.ClampLabelPosition(
                image.Margin.Left - labelSize.Width / 2,
                labelSize.Width,
                visibleWidth);
            labelTop = iBarter.Routing.RouteStepLabelRenderer.ClampLabelPosition(
                labelTop,
                labelSize.Height,
                host.ActualHeight);
            label.Margin = new Thickness(
                labelLeft,
                labelTop,
                Math.Max(0, visibleWidth - labelLeft - labelSize.Width),
                Math.Max(0, host.ActualHeight - labelTop - labelSize.Height));
        }

        private double GetUnobscuredMapWidth(Grid host) {
            double hostWidth = host.ActualWidth;
            try {
                FrameworkElement? rightDock = App.myfmMain?.dockRight_ShipCargo;
                if (rightDock is null || !rightDock.IsVisible || rightDock.ActualWidth <= 0)
                    return hostWidth;
                Point dockLeft = rightDock.TranslatePoint(new Point(0, 0), host);
                return iBarter.Routing.RouteStepLabelRenderer.VisibleExtentBeforeOccluder(
                    hostWidth,
                    dockLeft.X,
                    occluderVisible: true);
            }
            catch (InvalidOperationException) {
                // The dock and map can briefly be in different visual trees
                // while Syncfusion rearranges documents. The next layout pass
                // retries with the current tree.
                return hostWidth;
            }
        }

        private static double? FindLowestBarterLabelBottom(Grid host, string islandId) {
            double? lowest = null;
            foreach (FrameworkElement element in host.Children.OfType<FrameworkElement>()) {
                if (element.Visibility != Visibility.Visible
                    || element.Tag is not RouteStepMapLabel {
                        StepKind: RouteStepKind.Barter,
                    } stepLabel
                    || !StringComparer.Ordinal.Equals(stepLabel.IslandId, islandId)) {
                    continue;
                }

                double height = element.ActualHeight > 0
                    ? element.ActualHeight
                    : element.Height;
                if (Double.IsNaN(height) || height <= 0) continue;
                double bottom = element.Margin.Top + height;
                lowest = lowest is null ? bottom : Math.Max(lowest.Value, bottom);
            }
            return lowest;
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
            if (coordinator?.Mode == CargoMode.AutomaticRoute
                && !coordinator.ShowRouteGuides)
                return;
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
                var displayPath = RouteDisplayGeometry.BuildDirectLeg(from, to);
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

        // Return a readable group tint for the dark map.  Do not push light
        // group colours to pure white: that erased the group distinction and
        // made a label appear white until an unrelated hover visual changed
        // the surface beneath it.
        private static Brush LightenForMapBg(Brush _brush) {
            if (_brush is SolidColorBrush scb) {
                Color c = scb.Color;
                double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
                // Dark colours need lifting; bright colours need preserving,
                // not whitening.  0.74 provides contrast against the map and
                // still leaves the original group hue visible.
                double target = lum < 0.56 ? Math.Min(0.82, lum + 0.42) : 0.74;
                byte r = (byte)Math.Min(255, (int)(c.R * (target / Math.Max(0.001, lum))));
                byte g = (byte)Math.Min(255, (int)(c.G * (target / Math.Max(0.001, lum))));
                byte b = (byte)Math.Min(255, (int)(c.B * (target / Math.Max(0.001, lum))));
                return new SolidColorBrush(Color.FromRgb(r, g, b));
            }
            // Non-solid brushes are not used for groups today.  Keep the
            // fallback neutral-but-readable rather than pure white.
            return Brushes.Gainsboro;
        }

        private static Brush GetBrushForGroup(int group) {
            switch (group) {
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

        private static Brush GetBursh(Barter _barter) =>
            GetBrushForGroup(_barter.BarterGroup);

        public void IslandsButtonInitialisation(Barter _barter, Brush _brush) {
            ButtonInitialisation(_barter, _brush);
        }

        public void IslandsButtonInitialisation() {
            Grid_MapMain.Children.Clear();

            listGrid_Islands = new List<Grid>();

            InitTempGrid();
            // Bug 3 fix: when an automatic route is active the map must
            // show the route's actual steps (pickup/barter/unload) only.
            // The legacy code also rendered every Planner barter that had
            // ExchangeQuantity > 0, which:
            //   * put a "金色仙人掌花束 → 匠人的贝壳项链" label on top of an
            //     Iliya unload step even though the barter was not part of
            //     the current route;
            //   * subscribed Islands_MouseLeftButtonDown on that grid, so
            //     double-clicking what the user perceived as the unload
            //     node flipped the unrelated barter's ExchangeDone.
            // The fix is to skip Planner-barter nodes while in
            // AutomaticRoute mode (EnsureAutomaticWarehouseNodes handles
            // the route nodes, including the unload label).
            bool suppressPlannerBarters = App.myRouteCoordinator?.Mode == CargoMode.AutomaticRoute
                && App.myRouteCoordinator.CurrentPlan is not null
                && App.myRouteCoordinator.CurrentPlan.Routes.Count > 0;
            if (!suppressPlannerBarters) {
                foreach (Barter myBarter in App.myPVM.BarterCollection.Where(b =>
                    b.ExchangeDone == false && b.ExchangeQuantity > 0 &&
                    (!int.TryParse(b.Item1?.ItemLV, out int lv) || lv <= MAX_MAP_LV))) {
                    ButtonInitialisation(myBarter, GetBursh(myBarter));
                }
            }
            EnsureAutomaticWarehouseNodes();
            EnsureRouteIslandLabels();
            // Audit round 6: explicit reposition at the end of every
            // IslandsButtonInitialisation. The reconcile pass above
            // already calls RepositionRouteStepLabels on the Empty
            // outcome, but the Bulk outcome (Built) does not — and
            // public callers of IslandsButtonInitialisation expect
            // the freshly-added wrappers to be positioned before
            // the call returns.
            RepositionRouteStepLabels();
            // Warehouse labels use a different visual pipeline from barter
            // labels. Re-run their placement now that the barter wrappers
            // have measured bounds, so the first rendered frame is already
            // collision-free (the timer also maintains it afterward).
            RepositionAutomaticWarehouseLabels();
        }

        private void RepositionAutomaticWarehouseLabels() {
            foreach (IslandVisual visual in listGrid_Islands
                .Select(grid => grid.Tag as IslandVisual)
                .Where(visual => visual is { IsWarehouse: true }
                    && visual.Label.Visibility == Visibility.Visible)!) {
                if (!TryGetIslandCenter(visual.Islands, out Grid? host, out Point center)
                    || host is null) {
                    continue;
                }
                visual.Host = host;
                Size labelSize = visual.LabelPlacementSize.Width > 0
                    && visual.LabelPlacementSize.Height > 0
                    ? visual.LabelPlacementSize
                    : MeasureLabelForPlacement(visual.Label);
                visual.LabelPlacementSize = labelSize;
                PositionIslandLabel(visual, host, center, labelSize);
            }
        }

        /// <summary>
        /// Sentinel Tag used by route-only island labels so the next
        /// refresh can distinguish them from ButtonInitialisation
        /// containers and warehouse markers. Deprecated by
        /// <see cref="RouteStepMapLabel"/>; kept here temporarily
        /// so the visual tree-walk helpers compile while the
        /// old tests migrate.
        /// </summary>
        [System.Obsolete("Use RouteStepMapLabel as the wrapper tag; " +
            "RouteIslandLabelTag is the v1 per-island label and is " +
            "replaced by the per-step descriptor.")]
        internal sealed record RouteIslandLabelTag(string IslandId);

        /// <summary>
        /// Audit round 4 (regression fix): in AutomaticRoute mode the
        /// Planner-barter render loop is suppressed (it was reusing
        /// Planner barters to drive both labels and CK handlers, and
        /// the side effect was that islands without a Planner
        /// barter but visited by the actual route — e.g. Baremi,
        /// Crow in the user's example — lost their name label).
        /// This method draws a single hit-test-transparent island
        /// name label for every island the visible routes touch,
        /// independent of whether a Planner barter exists for it.
        ///
        /// <para>The label is added directly to <c>Grid_MapMain</c>
        /// (NOT to <c>listGrid_Islands</c>), so
        /// <see cref="IslandsButtonRearrange"/> never touches it.
        /// Positioning mirrors the same island centre the warehouse
        /// nodes and route markers use, so the name sits directly
        /// under the island block — exactly like the legacy Planner
        /// labels did.</para>
        ///
        /// <para>Labels are pure island names. They carry no
        /// BarterRowId, no StepKind, no handlers, and no
        /// completion authority. MapControl.Islands_MouseLeftButtonDown
        /// still gates completion on
        /// <see cref="RouteMapNode.AuthorizesCompletion"/>, so
        /// double-clicking an island name can never flip
        /// <c>ExchangeDone</c>.</para>
        /// </summary>
        private void EnsureRouteIslandLabels() {
            // Audit round 6: the v1 system keyed every label on a
            // HashSet<IslandId>, which meant two BarterSteps on the
            // same island lost their individual identity — the second
            // step either dropped or got the first step's text. This
            // round keys each label on (RouteNumber, StepIndex) so
            // every real route step gets its own label.
            var coordinator = App.myRouteCoordinator;
            if (coordinator?.Mode != CargoMode.AutomaticRoute
                || coordinator.CurrentPlan is not { } plan) {
                // Mode exit or no plan: drop every route-step label
                // so the manual-mode Planner map isn't polluted by
                // automatic-route residues.
                RemoveAllRouteStepLabels();
                return;
            }

            var planned = iBarter.Routing.RouteStepLabelPlanner.PlanLabels(
                plan,
                coordinator.ShowAllRoutes,
                coordinator.SelectedRouteNumber,
                BuildItemDisplayNameLookup(),
                BuildBarterGroupLookup(),
                coordinator.CompletedBarterRowIds);

            // Warehouse dedup pass: Iliya with both pickup AND
            // unload gets only one warehouse label.  Real Barter
            // labels are NEVER deduped by island.
            planned = iBarter.Routing.RouteStepLabelPlanner.StripWarehouseDuplicates(planned);

            var existing = CollectExistingRouteStepLabelIdentities();
            var outcome = iBarter.Routing.RouteStepLabelRenderer.Reconcile(
                planned, existing,
                Grid_MapMain.ActualWidth, Grid_MapMain.ActualHeight,
                out var toAdd, out var toRemove);

            if (RouteIslandLabelDiagnostics.Enabled) {
                RouteIslandLabelDiagnostics.Log(
                    $"EnsureRouteStepLabels: outcome={outcome} " +
                    $"host={Grid_MapMain.ActualWidth:0.##}x{Grid_MapMain.ActualHeight:0.##} " +
                    $"planned={planned.Count} existing={existing.Count} " +
                    $"add={toAdd.Count} remove={toRemove.Count}");
            }

            if (toRemove.Count > 0) RemoveRouteStepLabelsByIdentity(toRemove);

            if (outcome == iBarter.Routing.RouteStepLabelRenderer.RebuildOutcome.Deferred) {
                ScheduleRouteStepLabelsRetry();
                return;
            }
            if (outcome == iBarter.Routing.RouteStepLabelRenderer.RebuildOutcome.Empty) {
                // Even when no add/remove is required, a viewport
                // geometry change (zoom, pan, HD camera move) can
                // still require a reposition pass.  Calling
                // RepositionRouteStepLabels here is cheap (it's a
                // Margin update, not a label rebuild) and keeps the
                // first call after a route change consistent with
                // later viewport changes.
                RepositionRouteStepLabels();
                return;
            }
            if (toAdd.Count > 0) AddRouteStepLabels(toAdd);
        }

        /// <summary>
        /// Audit round 6: every viewport change (zoom, pan, HD
        /// camera, resize) calls this.  It walks all existing
        /// <see cref="RouteStepMapLabel"/> wrappers in the visual
        /// tree and updates their <c>Margin</c> based on the current
        /// island centres returned by <see cref="TryGetIslandCenter"/>.
        /// Identity is preserved — the wrapper's <c>Tag</c> is
        /// untouched, so the route-step distinction survives.
        /// </summary>
        public void RepositionRouteStepLabels() {
            if (Grid_MapMain.ActualWidth <= 0 || Grid_MapMain.ActualHeight <= 0) {
                return; // not laid out yet; retry path handles it
            }
            int updated = 0, missing = 0;
            // occurrenceByIsland tracks how many step labels we've
            // already placed on the same island; the renderer uses
            // this index to compute the deterministic vertical
            // offset.
            var occurrenceByIsland = new Dictionary<string, int>(StringComparer.Ordinal);
            RepositionChildren(Grid_MapMain, occurrenceByIsland, ref updated, ref missing);
            if (RouteIslandLabelDiagnostics.Enabled) {
                RouteIslandLabelDiagnostics.Log(
                    $"RepositionRouteStepLabels: updated={updated} missing={missing} " +
                    $"host={Grid_MapMain.ActualWidth:0.##}x{Grid_MapMain.ActualHeight:0.##}");
            }
        }

        private void RepositionChildren(
            DependencyObject parent,
            Dictionary<string, int> occurrenceByIsland,
            ref int updated, ref int missing) {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++) {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement fe && fe.Tag is RouteStepMapLabel label) {
                    RepositionSingle(fe, label, occurrenceByIsland, ref updated, ref missing);
                }
                else {
                    // Recurse into non-label containers so we find
                    // every RouteStepMapLabel, even when the WPF
                    // tree has been re-arranged.
                    RepositionChildren(child, occurrenceByIsland, ref updated, ref missing);
                }
            }
        }

        private void RepositionSingle(
            FrameworkElement wrapper,
            RouteStepMapLabel label,
            Dictionary<string, int> occurrenceByIsland,
            ref int updated, ref int missing) {
            // Look up the island metadata.  If the island has been
            // removed from the catalog (e.g. the user removed a
            // warehouse row from App.listIslands), do NOT silently
            // keep the wrapper at its old position — the audit
            // requires an explicit failure rather than a stale
            // orphan.
            var island = App.listIslands?.FirstOrDefault(x => x.IslandsName == label.IslandId);
            if (island is null) {
                missing++;
                RouteIslandLabelDiagnostics.LogMissing(label.IslandId);
                return;
            }
            if (!TryGetIslandCenter(island, out Grid? host, out Point center) || host is null) {
                // Same failure: don't keep the wrapper at a stale
                // position.  We could hide the wrapper instead, but
                // the audit's "explicit failure" rule says: report
                // it and leave the wrapper for the next reconcile
                // pass to remove.
                missing++;
                RouteIslandLabelDiagnostics.LogMissing(label.IslandId);
                return;
            }
            int occurrence = occurrenceByIsland.TryGetValue(label.IslandId, out var n) ? n : 0;
            occurrenceByIsland[label.IslandId] = occurrence + 1;
            UpdateRouteStepLabelTypography(wrapper, label);
            double verticalOffset = iBarter.Routing.RouteStepLabelRenderer
                .ComputeVerticalOffset(occurrence, host.ActualHeight);
            PositionRouteStepLabel(wrapper, host, center, verticalOffset);
            // Ensure the wrapper sits in the per-island overlay host
            // (its actual owner).  If a previous bug ever added it
            // directly to Grid_MapMain, the next add/remove cycle
            // leaves the wrapper stranded; we re-parent to host.
            if (!ReferenceEquals(wrapper.Parent, host)) {
                if (wrapper.Parent is Panel oldParent) oldParent.Children.Remove(wrapper);
                host.Children.Add(wrapper);
            }
            updated++;
        }

        private void PositionRouteStepLabel(
            FrameworkElement wrapper, Grid host, Point center, double verticalOffset) {
            // Centre horizontally on the island block; stack
            // vertically by occurrence offset (so pickup + barter +
            // unload on the same island don't overlap).
            double visibleWidth = GetUnobscuredMapWidth(host);
            FitRouteStepLabelInsideHost(wrapper, visibleWidth);
            double left = iBarter.Routing.RouteStepLabelRenderer.ClampLabelPosition(
                center.X - wrapper.Width / 2,
                wrapper.Width,
                visibleWidth);
            double top = iBarter.Routing.RouteStepLabelRenderer.ClampLabelPosition(
                center.Y + verticalOffset,
                wrapper.Height,
                host.ActualHeight);
            wrapper.Margin = new Thickness(
                left,
                top,
                Math.Max(0, visibleWidth - left - wrapper.Width),
                Math.Max(0, host.ActualHeight - top - wrapper.Height));
        }

        private static void FitRouteStepLabelInsideHost(
            FrameworkElement wrapper, double hostWidth) {
            if (wrapper is not Grid grid
                || grid.Children.OfType<Border>().FirstOrDefault() is not { } border
                || border.Child is not TextBlock textBlock)
                return;
            border.MaxWidth = Double.PositiveInfinity;
            textBlock.TextWrapping = TextWrapping.NoWrap;
            double naturalWidth = MeasureElementForPlacement(border).Width;
            double width = iBarter.Routing.RouteStepLabelRenderer.ConstrainLabelExtent(
                naturalWidth, hostWidth);
            border.MaxWidth = width;
            textBlock.TextWrapping = naturalWidth > width
                ? TextWrapping.Wrap
                : TextWrapping.NoWrap;
            border.Measure(new Size(width, Double.PositiveInfinity));
            wrapper.Width = Math.Min(width, border.DesiredSize.Width);
            wrapper.Height = border.DesiredSize.Height;
        }

        private HashSet<string> CollectExistingRouteStepLabelIdentities() {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            CollectRouteStepLabelIdentities(Grid_MapMain, ids);
            return ids;
        }

        private static void CollectRouteStepLabelIdentities(
            DependencyObject parent, HashSet<string> ids) {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++) {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement fe && fe.Tag is RouteStepMapLabel label) {
                    ids.Add(label.Identity);
                }
                CollectRouteStepLabelIdentities(child, ids);
            }
        }

        private void RemoveRouteStepLabelsByIdentity(IReadOnlyList<string> identities) {
            var idSet = new HashSet<string>(identities, StringComparer.Ordinal);
            RemoveMatchingRouteStepLabels(Grid_MapMain, idSet);
        }

        private static void RemoveMatchingRouteStepLabels(
            DependencyObject parent, HashSet<string> idSet) {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = count - 1; i >= 0; i--) {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement fe && fe.Tag is RouteStepMapLabel label
                    && idSet.Contains(label.Identity)) {
                    if (parent is Panel panel) panel.Children.Remove(fe);
                }
                else {
                    RemoveMatchingRouteStepLabels(child, idSet);
                }
            }
        }

        private void RemoveAllRouteStepLabels() {
            int count = Grid_MapMain.Children.Count;
            for (int i = count - 1; i >= 0; i--) {
                if (Grid_MapMain.Children[i] is FrameworkElement fe
                    && fe.Tag is RouteStepMapLabel) {
                    Grid_MapMain.Children.RemoveAt(i);
                }
            }
            // Recurse into nested hosts (per-island overlay grids)
            // to catch any step labels that ended up in a child host.
            RemoveAllNestedStepLabels(Grid_MapMain);
        }

        private static void RemoveAllNestedStepLabels(DependencyObject parent) {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = count - 1; i >= 0; i--) {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Panel panel
                    && child is FrameworkElement fe
                    && fe.Tag is RouteStepMapLabel) {
                    panel.Children.Remove(fe);
                }
                else {
                    RemoveAllNestedStepLabels(child);
                }
            }
        }

        private void AddRouteStepLabels(IReadOnlyList<RouteStepMapLabel> labels) {
            // occurrenceByIsland lets us compute the vertical offset
            // that pairs with RepositionRouteStepLabels' pass.
            var occurrenceByIsland = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var existing in EnumerateExistingStepLabels())
                occurrenceByIsland[existing.IslandId] =
                    occurrenceByIsland.GetValueOrDefault(existing.IslandId) + 1;
            foreach (var label in labels) {
                var island = App.listIslands?.FirstOrDefault(x => x.IslandsName == label.IslandId);
                if (island is null) {
                    RouteIslandLabelDiagnostics.LogMissing(label.IslandId);
                    continue;
                }
                if (!TryGetIslandCenter(island, out Grid? host, out Point center) || host is null) {
                    RouteIslandLabelDiagnostics.LogMissing(label.IslandId);
                    continue;
                }
                int occurrence = occurrenceByIsland.TryGetValue(label.IslandId, out var n) ? n : 0;
                occurrenceByIsland[label.IslandId] = occurrence + 1;
                var wrapper = CreateRouteStepLabelWrapper(label);
                PositionRouteStepLabel(wrapper, host, center,
                    iBarter.Routing.RouteStepLabelRenderer.ComputeVerticalOffset(
                        occurrence, host.ActualHeight));
                host.Children.Add(wrapper);
                // Audit round 7: the wrapper's child is a TextBlock,
                // not a Label. The legacy `listLabels` collection is
                // owned by ButtonInitialisation; route step labels do
                // not belong there and must not be cast. Repositioning
                // is driven entirely by RepositionRouteStepLabels which
                // walks the visual tree by Tag, so this list is
                // intentionally not extended for the new step
                // pipeline.
            }
        }

        private IEnumerable<RouteStepMapLabel> EnumerateExistingStepLabels() {
            return EnumerateStepLabelsRecursive(Grid_MapMain);
        }

        private static IEnumerable<RouteStepMapLabel> EnumerateStepLabelsRecursive(
            DependencyObject parent) {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++) {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement fe && fe.Tag is RouteStepMapLabel label)
                    yield return label;
                foreach (var nested in EnumerateStepLabelsRecursive(child))
                    yield return nested;
            }
        }

        private Grid CreateRouteStepLabelWrapper(RouteStepMapLabel label) {
            // Restore the legacy map language: a barter's label colour
            // comes from its Planner BarterGroup, while warehouse
            // operations remain gold. The RowId -> group lookup happens
            // before rendering, so this never guesses a barter from the
            // island name (which is ambiguous when an island has several
            // exchanges).
            var groupBrush = label.IsWarehouseOperation
                ? Brushes.Gold
                : GetBrushForGroup(label.BarterGroup ?? Int32.MinValue);
            var labelForeground = label.IsWarehouseOperation
                ? Brushes.Gold
                : LightenForMapBg(groupBrush);
            var labelBackground = label.IsWarehouseOperation
                ? new SolidColorBrush(Color.FromArgb(140, 5, 22, 30))
                // Same translucent black panel used by the legacy
                // Planner labels. The previous route-step renderer
                // calculated this brush but never attached it.
                : new SolidColorBrush(Color.FromArgb(80, 0, 0, 0));
            bool highlighted = CurrentRenderSnapshot().HighlightedIslandIds.Contains(label.IslandId);
            var textBlock = new TextBlock {
                Text = label.DisplayText,
                Foreground = labelForeground,
                FontSize = GetMapLabelFontSize(highlighted),
                FontWeight = highlighted
                    ? HighlightedMapLabelFontWeight
                    : MapLabelFontWeight,
                TextWrapping = TextWrapping.NoWrap,
            };
            textBlock.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");
            TextOptions.SetTextFormattingMode(textBlock, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(textBlock, TextRenderingMode.ClearType);
            var border = new Border {
                Background = labelBackground,
                BorderBrush = labelForeground,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(3, 1, 3, 1),
                Child = textBlock,
            };
            var interactionNode = iBarter.Routing.RouteStepLabelInteraction.CreateNode(label);
            var wrapper = new Grid {
                Name = "RouteStepLabel_" + label.Identity,
                // The legacy exchange labels supported left double-click
                // (complete) and right-click (locate in Planner). Restore
                // that only for an actual BarterStep with a persistent
                // RowId. Pickup/unload labels remain non-interactive.
                IsHitTestVisible = interactionNode.AuthorizesCompletion,
                Tag = label,
                DataContext = interactionNode,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Cursor = interactionNode.AuthorizesCompletion
                    ? Cursors.Hand
                    : Cursors.Arrow,
            };
            if (interactionNode.AuthorizesCompletion) {
                wrapper.MouseLeftButtonDown += Islands_MouseLeftButtonDown;
                wrapper.MouseRightButtonDown += Islands_MouseRightButtonDown;
            }
            Panel.SetZIndex(wrapper, 60); // above route lines (default 0)
            wrapper.Children.Add(border);

            var size = MeasureElementForPlacement(border);
            wrapper.Width = size.Width;
            wrapper.Height = size.Height;
            return wrapper;
        }

        private void UpdateRouteStepLabelTypography(
            FrameworkElement wrapper,
            RouteStepMapLabel label) {
            if (wrapper is not Grid grid
                || grid.Children.OfType<Border>().FirstOrDefault() is not { } border
                || border.Child is not TextBlock textBlock) return;

            bool highlighted = CurrentRenderSnapshot().HighlightedIslandIds.Contains(label.IslandId);
            double fontSize = GetMapLabelFontSize(highlighted);
            FontWeight fontWeight = highlighted
                ? HighlightedMapLabelFontWeight
                : MapLabelFontWeight;
            if (Math.Abs(textBlock.FontSize - fontSize) <= 0.01
                && textBlock.FontWeight == fontWeight) return;

            textBlock.FontSize = fontSize;
            textBlock.FontWeight = fontWeight;
            var size = MeasureElementForPlacement(border);
            wrapper.Width = size.Width;
            wrapper.Height = size.Height;
        }

        private bool routeStepLabelsRetryScheduled;
        private void ScheduleRouteStepLabelsRetry() {
            if (routeStepLabelsRetryScheduled) return;
            routeStepLabelsRetryScheduled = true;
            Dispatcher.BeginInvoke(new Action(() => {
                routeStepLabelsRetryScheduled = false;
                EnsureRouteIslandLabels();
            }), DispatcherPriority.Render);
        }

        private Dictionary<string, string> BuildItemDisplayNameLookup() {
            // The WPF catalog may be unavailable in tests; fall back
            // to an empty dictionary and let RouteStepLabelPlanner
            // use the raw ItemId for display.
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (App.listItems is null) return map;
            foreach (var item in App.listItems) {
                if (item is null || string.IsNullOrEmpty(item.ItemID)) continue;
                var display = !string.IsNullOrEmpty(item.ItemNameDisplay)
                    ? item.ItemNameDisplay
                    : item.ItemName;
                if (!string.IsNullOrEmpty(display) && !map.ContainsKey(item.ItemID))
                    map[item.ItemID] = display;
            }
            return map;
        }

        private static Dictionary<string, int> BuildBarterGroupLookup() {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            if (App.myPVM?.BarterCollection is null) return map;
            foreach (var barter in App.myPVM.BarterCollection) {
                if (barter is null || string.IsNullOrWhiteSpace(barter.PlannerRowId)) continue;
                map.TryAdd(barter.PlannerRowId, barter.BarterGroup);
            }
            return map;
        }

        private static Size MeasureElementForPlacement(FrameworkElement element) {
            element.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            return element.DesiredSize;
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
            myLabel.SetResourceReference(Control.FontFamilyProperty, "AppFontFamily");
            myLabel.FontWeight = MapLabelFontWeight;
            TextOptions.SetTextFormattingMode(myLabel, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(myLabel, TextRenderingMode.ClearType);
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
                var language = LanguageService.Instance;
                string pickupRole = language.Localize("str.Map.AutoRoute.PickupRole");
                string unloadRole = language.Localize("str.Map.AutoRoute.UnloadRole");
                string role = pickup && unload
                    ? $"{pickupRole}/{unloadRole}"
                    : pickup ? pickupRole : unloadRole;
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
            if (clickedGrid == null || e.ClickCount != 2) return;

            // Bug 3 / Audit 3 (round 2): the primary authorization gate
            // is the RouteMapNode attached as DataContext or Tag. The
            // grid's Name suffix is checked only as defense-in-depth
            // — a regression that wires this handler up to a non-barter
            // grid can never mark an unrelated barter done, because
            // RouteMapNode.AuthorizesCompletion returns false for any
            // Pickup/Unload node regardless of the grid's Name.
            RouteMapNode? node = clickedGrid.DataContext as RouteMapNode
                ?? clickedGrid.Tag as RouteMapNode;
            if (node is null && (clickedGrid.Name.EndsWith("Warehouse", StringComparison.Ordinal)
                || clickedGrid.Name.EndsWith("Temp", StringComparison.Ordinal)))
                return;  // legacy name-suffix guard

            if (node is null) {
                // Fallback for legacy grid layouts that don't yet carry a
                // RouteMapNode. Resolve by island name, but ONLY when the
                // grid is not a Warehouse/Temp variant.
                string islandId = clickedGrid.Name.Length > 14
                    ? clickedGrid.Name.Substring(14, clickedGrid.Name.Length - 14)
                    : string.Empty;
                Barter myBarter = App.myPVM.BarterCollection.FirstOrDefault(
                    b => b.IsLandName == islandId);
                if (myBarter == null) return;
                myBarter.ExchangeDone = true;
                CompleteBarterViaPipeline(myBarter);
                e.Handled = true;
                return;
            }

            if (!node.AuthorizesCompletion) {
                // Audit round 2 (3): a non-barter node — even one that
                // was mis-built to carry a RowId — MUST refuse to flip
                // ExchangeDone. The defense-in-depth defense ensures the
                // "golden cactus bouquet → artisan shell necklace" unload
                // bug can never recur.
                return;
            }

            // Authoritative path: find the Planner row whose
            // persistent PlannerRowId matches the node's
            // BarterRowId. We do NOT rebuild a legacy
            // (Island,Item1,Item2) tuple from the current index —
            // that would silently re-derive the wrong row whenever
            // the user had reordered the Planner collection.
            string targetRowId = RouteTaskIdentity.PlannerRowId(node.BarterRowId!);
            Barter? targetBarter = null;
            for (int i = 0; i < App.myPVM.BarterCollection.Count; i++) {
                var b = App.myPVM.BarterCollection[i];
                if (string.Equals(b.PlannerRowId, targetRowId, StringComparison.Ordinal)) {
                    targetBarter = b;
                    break;
                }
            }
            if (targetBarter == null) return;
            // A capacity-safe route can split one Planner row across several
            // trips. Complete this exact route task first; the coordinator
            // checks every sibling segment and flips Planner CK only after the
            // final segment is done.
            CompleteBarterViaPipeline(targetBarter, node.BarterRowId);
            e.Handled = true;
        }

        private void CompleteBarterViaPipeline(
            Barter myBarter,
            string? completedTaskRowId = null) {
            // Audit round 3: row identity comes from the persistent
            // Barter.PlannerRowId; we never re-derive from index or
            // (Island, Item1, Item2).
            string rowId = myBarter.PlannerRowId;
            App.myRouteCoordinator?.ApplyBarterCompletionProgress(
                App.myfmMain.myPlannerControl.BuildCurrentAutomaticRouteRequest(
                    App.myfmMain.myPlannerControl.ResolveSelectedOptimizationProfileSafe()),
                App.myPVM.BarterCollection,
                rowId,
                completed: true,
                completedTaskRowId: completedTaskRowId);

            // A partial route task reduces Planner.ExchangeQuantity (for
            // example 10 -> 8). Recompute inventory projections and parley
            // from that authoritative remaining quantity before saving.
            App.myfmMain.myPlannerControl.RefreshDerivedValuesAfterRouteProgress();

            // Grouping performs the synchronous map rebuild. Do it only after
            // the coordinator has accepted the CK overlay; the old order
            // rebuilt from the stale completed-id set and then relied solely
            // on an asynchronous event to repair both route views.
            App.myfmMain.myPlannerControl.Grouping();
            App.myfmMain.myPlannerControl.SaveData();
            if (App.myCVM.CargoDetails.FirstOrDefault(b => b.IsLandName == myBarter.IsLandName) != null) {
                App.myCVM.CargoDetails.Remove(App.myCVM.CargoDetails.FirstOrDefault(b => b.IsLandName == myBarter.IsLandName));
                App.myfmMain.myShipCargo.UpdateCurrentLV();
                App.myfmMain.myShipCargo.SaveData();
            }
            // Automatic mode deliberately makes UpdateCurrentLV a no-op. Force
            // its route ItemsSource to re-read VisibleAutomaticSteps so the
            // completed card disappears even if the control missed an earlier
            // RouteDisplayChanged subscription while docking/loading.
            App.myfmMain.myShipCargo.RefreshAfterRouteProgress();
            App.myfmMain.myPlannerControl.RefreshParleyAfterExternalChange();
        }

        private void Islands_MouseRightButtonDown(object sender, MouseButtonEventArgs e) {
            var clickedGrid = sender as Grid;
            if (clickedGrid != null) {
                RouteMapNode? node = clickedGrid.DataContext as RouteMapNode
                    ?? clickedGrid.Tag as RouteMapNode;
                Barter? myBarter;
                if (node is not null) {
                    // Route-step labels must select the exact Planner row.
                    // An island can contain multiple exchanges, so falling
                    // back to FirstOrDefault(IsLandName) would select the
                    // wrong row even though the visible text was correct.
                    if (node.StepKind != RouteStepKind.Barter
                        || string.IsNullOrWhiteSpace(node.BarterRowId)) return;
                    myBarter = App.myPVM.BarterCollection.FirstOrDefault(b =>
                        string.Equals(
                            b.PlannerRowId,
                            RouteTaskIdentity.PlannerRowId(node.BarterRowId),
                            StringComparison.Ordinal));
                }
                else {
                    // Preserve the legacy manual-map behavior for the old
                    // island containers, whose identity is still encoded in
                    // GridContainer_<IslandId>.
                    if (!clickedGrid.Name.StartsWith("GridContainer_", StringComparison.Ordinal)
                        || clickedGrid.Name.Length <= 14) return;
                    string islandId = clickedGrid.Name.Substring(14);
                    myBarter = App.myPVM.BarterCollection.FirstOrDefault(
                        b => b.IsLandName == islandId);
                }
                if (myBarter != null) {
                    SelectAndRevealPlannerBarter(myBarter);
                    e.Handled = true;
                }
            }
        }

        private void SelectAndRevealPlannerBarter(Barter barter) {
            var main = App.myfmMain;
            var planner = main?.myPlannerControl;
            var grid = planner?.DataGrid_Planner;
            if (main is null || planner is null || grid is null) return;

            main.dockingManager_Main.ActiveWindow = main.document_Planner;
            grid.SelectedItem = barter;

            // Activating a docked document is asynchronous. Calling
            // SfDataGrid.ScrollInView immediately can reach its visual-row
            // generator before it exists (the user-reported NRE). Defer to
            // Render and resolve the real row/column rather than passing
            // SelectedIndex/-1 to the third-party control.
            grid.Dispatcher.BeginInvoke(new Action(() => {
                if (!grid.IsLoaded || grid.View is null) return;
                grid.UpdateLayout();
                int rowIndex = grid.ResolveToRowIndex(barter);
                if (rowIndex <= 0) return;

                int columnIndex = 1;
                var firstVisible = grid.Columns.FirstOrDefault(column => !column.IsHidden)
                    ?? grid.Columns.FirstOrDefault();
                if (firstVisible is not null) {
                    int resolved = grid.ResolveToGridVisibleColumnIndex(
                        grid.Columns.IndexOf(firstVisible));
                    if (resolved >= 1) columnIndex = resolved;
                }

                try {
                    grid.ScrollInView(new RowColumnIndex(rowIndex, columnIndex));
                }
                catch (NullReferenceException) {
                    // Selection and Planner activation already succeeded.
                    // Some Syncfusion layouts still have no visual row on
                    // this render pass; scrolling is optional and must not
                    // terminate the application.
                }
            }), DispatcherPriority.Render);
        }
    }
}
