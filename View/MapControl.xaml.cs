using Syncfusion.Windows.Controls.PivotGrid;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
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

        public List<Grid> listGrid_Islands = new List<Grid>();
        private List<Label> listLabels = null;
        private List<Grid> listImages = null;
        private List<Line> listLines = null;
        public DispatcherTimer myTimer = new DispatcherTimer();

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
            public Grid ImageGrid;
            public Label Label;
            public Line Line;
        }

        public MapControl() {
            InitializeComponent();
            //InitTempGrid();

            myTimer.Interval = TimeSpan.FromMilliseconds(100);
            myTimer.Tick += TimerOnTick;
            // DockingManager unloads the control when its tab is hidden,
            // so restart the timer on Loaded too - otherwise the auto-
            // layout (IslandsButtonRearrange) and overlap-avoidance
            // (AdjustLabels) stop firing after the user switches tabs.
            this.Loaded += (_, _) => {
                if (myTimer != null && !myTimer.IsEnabled) myTimer.Start();
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
            myTimer.Start();
        }

        private void TimerOnTick(object? sender, EventArgs e) {
            IslandsButtonRearrange();
            InvalidateVisual();
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

            for (int i = listGrid_Islands.Count - 1; i >= 0; i--) {
                Grid grid = listGrid_Islands[i];
                IslandVisual visual = grid.Tag as IslandVisual;
                // Where(...) returns a non-null IEnumerable, so the legacy check
                // never fired and stale pins accumulated. Use Any() instead.
                bool hasActiveBarter = visual != null && App.myPVM.BarterCollection.Any(b =>
                    b.ExchangeDone == false &&
                    b.ExchangeQuantity > 0 &&
                    b.IsLandName == visual.Islands.IslandsName);
                if (!hasActiveBarter) {
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

                    Barter barter = App.myCVM.CargoDetails.FirstOrDefault(b => b.IsLandName == myIslands.IslandsName);
                    if (barter != null) {
                        myLabel.FontWeight = FontWeights.Bold;
                        myLabel.FontSize = 14;
                        myLabel.Width = Double.NaN;
                        myLabel.Height = Double.NaN;
                    }
                    else if (myLabel.FontWeight == FontWeights.Bold) {
                        myLabel.FontWeight = FontWeights.Normal;
                        myLabel.FontSize = 12;
                        myLabel.Width = Double.NaN;
                        myLabel.Height = Double.NaN;
                    }

                    // Use the same projected centre as DrawRouteOverlay.
                    // Resetting this from raw IslandsThickness on every timer
                    // tick made inset pins jump back to their old positions
                    // while dashed route endpoints stayed projected.
                    var displayCenter = GetDisplayCenterNormalized(myIslands);
                    Grid_Image.Margin = new Thickness(
                        displayCenter.X * Grid_MapMain.ActualWidth - 5,
                        displayCenter.Y * Grid_MapMain.ActualHeight - 5,
                        (1 - displayCenter.X) * Grid_MapMain.ActualWidth - 5,
                        (1 - displayCenter.Y) * Grid_MapMain.ActualHeight - 5);

                    // Place the label BELOW the island block by default,
                    // but flip it ABOVE when the island sits too close
                    // to the map's bottom edge - otherwise the label gets
                    // clipped below the map (IslandsThickness.Bottom is
                    // the gap from the island bottom to the map bottom;
                    // a small value means the label would overflow).
                    double labelTop;
                    if (1 - displayCenter.Y < 0.08) {
                        // flip: position label above the island block
                        // (label baseline = top - label height)
                        labelTop = Grid_Image.Margin.Top - myLabel.ActualHeight;
                    }
                    else {
                        // default: position label below the island block
                        labelTop = Grid_Image.Margin.Top + Grid_Image.ActualHeight;
                    }
                    myLabel.Margin = new Thickness(Grid_Image.Margin.Left - myLabel.ActualWidth / 2, labelTop, Grid_Image.Margin.Right - myLabel.ActualWidth, Grid_Image.Margin.Bottom - myLabel.ActualHeight);


                    NewMargin(myLabel);
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

            AdjustLabels(listLabels);
            InvalidateVisual();
            foreach (Line myLine in listLines) {
                var relativePoint_Image = GetPosition(listImages.FirstOrDefault(i => i.Name.Contains(myLine.Name.Substring(5, myLine.Name.Length - 5))));
                var relativePoint_Label = GetPosition(listLabels.FirstOrDefault(i => i.Name.Contains(myLine.Name.Substring(5, myLine.Name.Length - 5))));

                myLine.X1 = relativePoint_Image.X + 5;
                myLine.X2 = myLine.X1;
                myLine.Y1 = relativePoint_Image.Y + 5;
                myLine.Y2 = relativePoint_Label.Y;
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
        private static readonly object ROUTE_LINE_TAG = new object();
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
                    && ReferenceEquals(elem.Tag, ROUTE_LINE_TAG)) {
                    Grid_MapMain.Children.RemoveAt(i);
                }
            }

            var coordinator = App.myRouteCoordinator;
            IReadOnlyList<Barter> manualCargo = coordinator?.Mode == CargoMode.Manual
                ? App.myCVM.CargoDetails.ToList()
                : Array.Empty<Barter>();
            var snapshot = coordinator?.GetRenderSnapshot(manualCargo)
                ?? RouteRenderSnapshotFactory.CreateManual(manualCargo.Select(x => x.IsLandName).ToArray());
            foreach (var path in snapshot.Paths) {
                var route = ResolveRoute(path, snapshot);
                if (route is null || route.Count < 2) continue;
                Brush stroke = snapshot.IsManual
                    ? Brushes.Gold
                    : AutomaticRouteBrushes[path.ColorIndex % AutomaticRouteBrushes.Length];
                int patternRound = snapshot.IsManual ? 0 : path.ColorIndex / AutomaticRouteBrushes.Length;
                var dash = new DoubleCollection(AutomaticDashPatterns[patternRound % AutomaticDashPatterns.Length]);
                DrawRoutePath(route, stroke, dash);
            }
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
            IReadOnlyList<Islands> route,
            Brush stroke,
            DoubleCollection dashArray) {
            for (int i = 0; i < route.Count - 1; i++) {
                var fromIsland = route[i];
                var toIsland = route[i + 1];
                var navigationPath = ShippingCorridorGraph.BuildPath(
                    fromIsland.IslandsName,
                    new NavigationPoint(fromIsland.NavigationPoint.X, fromIsland.NavigationPoint.Y),
                    toIsland.IslandsName,
                    new NavigationPoint(toIsland.NavigationPoint.X, toIsland.NavigationPoint.Y));
                var displayPath = navigationPath.Select((point, index) =>
                    GetNavigationDisplayPoint(fromIsland, toIsland, point, index, navigationPath.Count)).ToArray();
                for (int segment = 0; segment < displayPath.Length - 1; segment++)
                    DrawRouteSegment(displayPath[segment], displayPath[segment + 1], stroke, dashArray,
                        addArrow: segment == displayPath.Length - 2);
            }
        }

        private void DrawRouteSegment(
            Point from,
            Point to,
            Brush stroke,
            DoubleCollection dashArray,
            bool addArrow) {
                double dx = to.X - from.X;
                double dy = to.Y - from.Y;
                if (dx == 0 && dy == 0) return;

                Grid_MapMain.Children.Add(new Line {
                    X1 = from.X,
                    Y1 = from.Y,
                    X2 = to.X,
                    Y2 = to.Y,
                    Stroke = stroke,
                    StrokeThickness = 2.5,
                    StrokeDashArray = dashArray,
                    Tag = ROUTE_LINE_TAG,
                    IsHitTestVisible = false,
                });

                if (!addArrow) return;
                double angleDeg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                var arrow = new Polygon {
                    Fill = stroke,
                    Stroke = stroke,
                    StrokeThickness = 1,
                    Points = new PointCollection {
                        new Point(0, 0),
                        new Point(-11, -5),
                        new Point(-11, 5),
                    },
                    Tag = ROUTE_LINE_TAG,
                    IsHitTestVisible = false,
                };
                var transforms = new TransformGroup();
                transforms.Children.Add(new RotateTransform(angleDeg));
                transforms.Children.Add(new TranslateTransform(to.X, to.Y));
                arrow.RenderTransform = transforms;
                Grid_MapMain.Children.Add(arrow);
        }

        private Point GetNavigationDisplayPoint(
            Islands fromIsland,
            Islands toIsland,
            NavigationPoint navigationPoint,
            int index,
            int count) {
            if (index == 0) return GetIslandCenter(fromIsland);
            if (index == count - 1) return GetIslandCenter(toIsland);

            bool fromRight = IslandNavigationGeometry.GetDisplayGroup(fromIsland.IslandsName)
                == SpecialDisplayGroup.RightInset;
            bool toRight = IslandNavigationGeometry.GetDisplayGroup(toIsland.IslandsName)
                == SpecialDisplayGroup.RightInset;
            if (fromRight || toRight) {
                var references = App.listIslands
                    .Where(x => IslandNavigationGeometry.RightInsetNames.Contains(x.IslandsName)
                        && x.HasNavigationCoordinates)
                    .Select(x => x.NavigationPoint)
                    .ToArray();
                var projected = IslandNavigationGeometry.ProjectPointToInset(
                    references, RIGHT_INSET_BOUNDS, INSET_PADDING, navigationPoint);
                return new Point(projected.X * Grid_MapMain.ActualWidth, projected.Y * Grid_MapMain.ActualHeight);
            }

            bool southern = IslandNavigationGeometry.GetDisplayGroup(fromIsland.IslandsName)
                    == SpecialDisplayGroup.BottomEdge
                && IslandNavigationGeometry.GetDisplayGroup(toIsland.IslandsName)
                    == SpecialDisplayGroup.BottomEdge;
            if (southern) {
                var start = GetIslandCenter(fromIsland);
                var end = GetIslandCenter(toIsland);
                double tx = Math.Abs(toIsland.NavigationPoint.X - fromIsland.NavigationPoint.X) < 1
                    ? 0
                    : (navigationPoint.X - fromIsland.NavigationPoint.X)
                      / (toIsland.NavigationPoint.X - fromIsland.NavigationPoint.X);
                double ty = Math.Abs(toIsland.NavigationPoint.Y - fromIsland.NavigationPoint.Y) < 1
                    ? 0
                    : (navigationPoint.Y - fromIsland.NavigationPoint.Y)
                      / (toIsland.NavigationPoint.Y - fromIsland.NavigationPoint.Y);
                return new Point(
                    start.X + (end.X - start.X) * tx,
                    start.Y + (end.Y - start.Y) * ty);
            }

            return index < count / 2 ? GetIslandCenter(fromIsland) : GetIslandCenter(toIsland);
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
        private Point GetIslandCenter(Islands isl) {
            var p = GetDisplayCenterNormalized(isl);
            return new Point(p.X * Grid_MapMain.ActualWidth, p.Y * Grid_MapMain.ActualHeight);
        }

        private Point GetPosition(UIElement element) {
            Point rootPoint = new Point(0, 0);
            try {
                if (Grid_MapMain.IsLoaded) {
                    GeneralTransform transform = element.TransformToAncestor(Grid_MapMain);
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
                            double moveDownDistance = labels[i].Margin.Top + labels[i].ActualHeight - labels[j].Margin.Top;
                            double moveUpDistance = labels[j].Margin.Top - (labels[i].Margin.Top + labels[i].ActualHeight);


                            labels[j].Margin = new Thickness(labels[j].Margin.Left, labels[i].Margin.Top + labels[i].ActualHeight, labels[j].Margin.Right, labels[j].Margin.Bottom - labels[j].ActualHeight * 2);
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
            double right1 = left1 + _label1.ActualWidth;
            double top1 = _label1.Margin.Top;
            double bottom1 = top1 + _label1.ActualHeight;

            double left2 = _label2.Margin.Left;
            double right2 = left2 + _label2.ActualWidth;
            double top2 = _label2.Margin.Top;
            double bottom2 = top2 + _label2.ActualHeight;

            bool horizontalOverlap = (left1 < right2 && right1 > left2);
            bool verticalOverlap = (top1 < bottom2 && bottom1 > top2);

            return horizontalOverlap && verticalOverlap;
        }

        private void NewMargin(Label _label) {
            double rightEdge = _label.Margin.Left + _label.ActualWidth;
            double bottomEdge = _label.Margin.Top + _label.ActualHeight;

            double newLeftMargin = _label.Margin.Left;
            double newTopMargin = _label.Margin.Top;

            // 检查并调整右边界
            if (rightEdge > this.ActualWidth) {
                newLeftMargin = this.ActualWidth - _label.ActualWidth;
                newLeftMargin = Math.Max(0, newLeftMargin); // 避免负边距
            }

            // 检查左边界
            if (_label.Margin.Left < 0) {
                newLeftMargin = 0; // 确保 Label 不超出左边界
            }

            // 检查并调整底边界
            if (bottomEdge > this.ActualHeight) {
                newTopMargin = this.ActualHeight - _label.ActualHeight;
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
            //Grid_MapMain.Children.Clear();

            for (int i = Grid_MapMain.Children.Count - 1; i > 0; i--) {
                var child = Grid_MapMain.Children[i];
                if (child.GetName() == "Image_BackgroundMap") {
                    continue;
                }

                Grid_MapMain.Children.Remove(child);
            }

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
        }

        private void ButtonInitialisation(Barter _barter, Brush _brush) {
            // Computed once and reused everywhere below instead of
            // repeating the Item1Name/Item2Name check (and instead of
            // re-deriving "temp-ness" later from a Name string), so a
            // Temp placeholder island is unambiguously identified.
            bool isTemp = !(_barter.Item1Name != "" && _barter.Item2Name != null);

            Grid myGrid_Container = new Grid();

            if (!isTemp) {
                myGrid_Container.Name = "GridContainer_" + _barter.IsLandName;
                myGrid_Container.MouseLeftButtonDown += Islands_MouseLeftButtonDown;
                myGrid_Container.MouseRightButtonDown += Islands_MouseRightButtonDown;
                myGrid_Container.MouseDown += MyGrid_Container_MouseDown;
            }
            else {
                myGrid_Container.Name = "GridContainer_" + _barter.IsLandName + "Temp";
            }

            Grid myGrid_Image = new Grid();
            if (!isTemp) {
                myGrid_Image.Name = "GridImage_" + _barter.IsLandName;
            }
            else {
                myGrid_Image.Name = "GridImage_" + _barter.IsLandName + "Temp";
            }


            var displayCenter = GetDisplayCenterNormalized(_barter.IsLand);
            myGrid_Image.Margin = new Thickness(
                displayCenter.X * Grid_MapMain.ActualWidth - 5,
                displayCenter.Y * Grid_MapMain.ActualHeight - 5,
                (1 - displayCenter.X) * Grid_MapMain.ActualWidth - 5,
                (1 - displayCenter.Y) * Grid_MapMain.ActualHeight - 5);
            myGrid_Image.Width = 10;
            myGrid_Image.Height = 10;

            Rectangle myRectangle = new Rectangle();
            myRectangle.Fill = _brush;

            Label myLabel = new Label();
            if (!isTemp) {
                myLabel.Name = "Label_" + _barter.IsLand.IslandsName;
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
            if (_barter.Item1Name != "" && _barter.Item2Name != "") {
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

            NewMargin(myLabel);

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
            if (!isTemp) {
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
                ImageGrid = myGrid_Image,
                Label = myLabel,
                Line = myLine
            };

            Grid_MapMain.Children.Add(myGrid_Container);

            if (listGrid_Islands.FirstOrDefault(b => b.Name == "GridContainer_" + _barter.IsLand.IslandsName) == null) {
                listGrid_Islands.Add(myGrid_Container);
            }

            //IslandsButtonRearrange();
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
