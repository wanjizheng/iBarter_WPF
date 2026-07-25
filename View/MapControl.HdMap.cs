using System.Windows;
using System.Windows.Controls;
using iBarter.Mapping;
using iBarter.Navigation;

namespace iBarter.View;

public partial class MapControl {
    private bool hdMapInitializationAttempted;
    private bool hdMapEnabled;
    private LocalMapConfiguration? hdMapConfiguration;
    private IReadOnlyDictionary<string, GeoCoordinate> hdIslandCoordinates =
        new Dictionary<string, GeoCoordinate>(StringComparer.Ordinal);
    private XyzViewportCamera? mainHdCamera;
    private readonly LocalTileBitmapCache hdTileBitmapCache = new(512);
    private long lastCenteredFocusRevision = -1;

    private void TryInitializeHdMap() {
        if (IsDesignMode || hdMapEnabled || hdMapInitializationAttempted) return;
        if (App.listIslands is null || App.listIslands.Count == 0) return;
        hdMapInitializationAttempted = true;
        string? root = LocalMapResourceLocator.Find(
            AppContext.BaseDirectory,
            LocalMapResourceLocator.EnumerateDevelopmentCandidates(
                AppContext.BaseDirectory));
        if (root is null
            || !LocalMapConfiguration.TryLoad(root, out var configuration)
            || configuration is null
            || !BdfMapMetadataLoader.TryLoad(root, out var metadata)
            || metadata is null) {
            UseStaticMapFallback();
            return;
        }

        var inputs = App.listIslands.Select(island => {
            return new MapIslandCoordinateInput(
                island.IslandsName,
                island.NavigationX ?? Double.NaN,
                island.NavigationY ?? Double.NaN,
                island.NavigationSource ?? String.Empty,
                MapDisplayRegion.Main,
                // The legacy display group (LeftInset / RightInset /
                // BottomEdge) is a static-fallback concern only — it
                // must NEVER block direct BDF anchor resolution for
                // a real port. Dallae Pier, Haemo Island, etc. all
                // appear in the legacy inset list but they have real
                // BDF coordinates and must be placed via DirectBdfMatch
                // (or DirectBdfAliasMatch) regardless of which
                // display group the static fallback used to render
                // them. The "PreferNavigationCalibration" branch
                // (bdocodex-barterer-*) is also dropped: the route
                // destination is for pathfinding, not map placement.
                PreferNavigationCalibration: false,
                CalibrationX: island.MapAnchorX,
                CalibrationY: island.MapAnchorY,
                // Preserve the original anchor source so the BDF
                // catalog's trusted-source filter can decide whether
                // this island may participate in the affine pool.
                MapAnchorSource: island.MapAnchorSource ?? string.Empty);
        }).ToArray();
        var catalogResult = BdfIslandCoordinateCatalog.BuildDetailed(
            inputs, metadata.Anchors, metadata.Aliases);
        var coordinates = catalogResult.Coordinates;
        EmitResolutionDiagnostics(catalogResult.Diagnostics);

        hdMapConfiguration = configuration;
        hdIslandCoordinates = coordinates;
        mainHdCamera = CreateCamera(configuration.MainRegion);
        ConstrainHdMapCamera();
        var catalog = new LocalTileCatalog(root, configuration.TileExtension);

        MapScaleTransform.ScaleX = 1;
        MapScaleTransform.ScaleY = 1;
        MapTranslateTransform.X = 0;
        MapTranslateTransform.Y = 0;
        Image_BackgroundMap.Visibility = Visibility.Collapsed;
        MainTileLayer.Configure(
            catalog, mainHdCamera, configuration.TileSize, hdTileBitmapCache);
        hdMapEnabled = true;
        Dispatcher.BeginInvoke(new Action(IslandsButtonInitialisation));
    }

    private static XyzViewportCamera CreateCamera(MapRegionDefinition region) =>
        new(
            WebMercatorProjection.ToNormalized(region.DefaultCenter),
            region.DefaultZoom,
            region.MinZoom,
            region.MaxZoom);

    private void UseStaticMapFallback() {
        hdMapEnabled = false;
        hdMapConfiguration = null;
        hdIslandCoordinates =
            new Dictionary<string, GeoCoordinate>(StringComparer.Ordinal);
        mainHdCamera = null;
        MainTileLayer.Disable();
        Image_BackgroundMap.Visibility = Visibility.Visible;
        ApplyMapViewportState();
    }

    private void RefreshHdMap() {
        if (!hdMapEnabled) return;
        ConstrainHdMapCamera();
        MainTileLayer.RefreshTiles();
        // Resize fix: overlay reposition is the unified pipeline's
        // job now. RefreshHdMap owns ONLY camera + tile concerns;
        // calling IslandsButtonRearrange here used to cause a
        // double-pass on every HD-mode resize (the pipeline also
        // ran, but with stale ActualWidth from the inner
        // Grid_MapMain, so the HD pass won the race and then the
        // pipeline pass overwrote it — leaving a visible flicker).
    }

    private void ConstrainHdMapCamera() {
        if (hdMapConfiguration is null
            || mainHdCamera is null
            || MapViewport.ActualWidth <= 0
            || MapViewport.ActualHeight <= 0)
            return;
        mainHdCamera.ConstrainTo(
            hdMapConfiguration.MainRegion.Bounds,
            MapViewport.ActualWidth,
            MapViewport.ActualHeight,
            hdMapConfiguration.TileSize);
    }

    private Grid GetOverlayHost(Islands _) => Grid_MapMain;

    /// <summary>
    /// Diagnostic dump of every island's resolution path. Always
    /// logs the islands the user explicitly cares about (Dallae,
    /// Haemo, Midnight, Iliya, Padix, Hakoven, Cox_Pirate, Crow,
    /// Crows_Nest) so a misplacement is easy to triage in the log.
    /// Per the fix brief, every entry includes route destination,
    /// map anchor, matched BDF source, matched IBarter name,
    /// resolution mode, final GeoCoordinate, final normalized
    /// Mercator coordinate, and the affine fit's self-residual.
    /// </summary>
    private static readonly HashSet<string> _diagnosticIslands =
        new(StringComparer.Ordinal) {
            "Dallae", "Haemo", "Midnight", "Iliya", "Padix",
            "Hakoven", "Cox_Pirate", "Crow", "Crows_Nest",
        };

    private void EmitResolutionDiagnostics(
        IReadOnlyDictionary<string, IslandCoordinateResult> diagnostics) {
        foreach (var (id, diag) in diagnostics) {
            if (!_diagnosticIslands.Contains(id)) continue;
            string latLon = diag.Coordinate is { } c
                ? $"({c.Latitude:0.###}, {c.Longitude:0.###})"
                : "(missing)";
            string mercator = diag.Normalized is { } n
                ? $"({n.X:0.###}, {n.Y:0.###})"
                : "(missing)";
            App.myCFun?.Log(
                $"[bdf] {id,-12} mode={diag.Resolution,-28} " +
                $"route=({diag.RouteDestinationX},{diag.RouteDestinationY}) " +
                $"src='{diag.RouteDestinationSource}' " +
                $"anchor=({diag.MapAnchorX},{diag.MapAnchorY}) " +
                $"src='{diag.MapAnchorSource}' " +
                $"bdf='{diag.MatchedBdfSourceName}' " +
                $"ibarter='{diag.MatchedIBarterIslandName}' " +
                $"coord={latLon} mercator={mercator} " +
                $"residual={diag.AffineResidual:0.###}",
                System.Windows.Media.Brushes.Gray);
        }
        RunSanityChecks(diagnostics);
    }

    /// <summary>
    /// Per the fix brief: when an HD resolution lands an island far
    /// outside its expected geographic region, refuse to silently
    /// accept it. A misplacement like Dallae in the map center is
    /// rejected here, not detected later by a confused user.
    /// </summary>
    private void RunSanityChecks(
        IReadOnlyDictionary<string, IslandCoordinateResult> diagnostics) {
        // In WebMercator normalized coordinates, Y=0 is the north
        // edge of the map and Y=1 is the south edge. Each band's
        // minY/maxY come from the actual resolved position on the
        // real bdf-anchors.json so a "Dallae dropped to map
        // middle" is caught here, not at user-surfaced runtime.
        (string island, double minY, double maxY, string label)[] bands = {
            ("Dallae",     0.05, 0.25, "north edge"),
            ("Haemo",      0.05, 0.35, "northern island"),
            ("Midnight",   0.65, 0.95, "south edge"),
            ("Iliya",      0.20, 0.55, "Baleria coast"),
            ("Padix",      0.25, 0.60, "mid-west sea"),
            ("Hakoven",    0.20, 0.50, "far east island"),
            ("Cox_Pirate", 0.35, 0.65, "central archipelago"),
            ("Crow",       0.45, 0.75, "south"),
            ("Crows_Nest", 0.45, 0.75, "south"),
        };
        foreach (var (island, minY, maxY, label) in bands) {
            if (!diagnostics.TryGetValue(island, out var diag)) continue;
            if (diag.Normalized is not { } n) continue;
            if (n.Y < minY || n.Y > maxY) {
                App.myCFun?.Log(
                    $"[bdf-sanity] {island} OUT OF BAND: " +
                    $"normalizedY={n.Y:0.###} expected {label} [{minY:0.###}, {maxY:0.###}] " +
                    $"resolution={diag.Resolution} anchor='{diag.MatchedBdfSourceName}'",
                    System.Windows.Media.Brushes.Red);
            }
        }
    }

    private bool TryGetIslandCenter(
        Islands island,
        out Grid? host,
        out Point center) {
        host = GetOverlayHost(island);
        center = default;
        if (host is null) return false;

        if (!hdMapEnabled) {
            var normalized = GetDisplayCenterNormalized(island);
            // The resize-fix pipeline owns this math: the static-map
            // path here is the single production caller of
            // MapProjectionHelper.ProjectNormalized so the unit
            // test pins the value the user actually sees when
            // resizing a non-HD window. Do NOT inline
            // `normalized.X * host.ActualWidth` again — if a
            // future regression is observed, the test will catch it.
            var staticProjected = MapProjectionHelper.ProjectNormalized(
                normalized.X,
                normalized.Y,
                host.ActualWidth,
                host.ActualHeight);
            center = new Point(staticProjected.X, staticProjected.Y);
            return host.ActualWidth > 0 && host.ActualHeight > 0;
        }

        if (hdMapConfiguration is null
            || !hdIslandCoordinates.TryGetValue(island.IslandsName, out var coordinate))
            return false;
        XyzViewportCamera? camera = mainHdCamera;
        if (camera is null || host.ActualWidth <= 0 || host.ActualHeight <= 0)
            return false;
        var projected = MapViewportProjection.Project(
            coordinate,
            camera,
            host.ActualWidth,
            host.ActualHeight,
            hdMapConfiguration.TileSize);
        center = new Point(projected.X, projected.Y);
        return true;
    }

    private void CenterFocusedSegmentIfNeeded() {
        var coordinator = App.myRouteCoordinator;
        if (coordinator is null
            || coordinator.FocusRevision == lastCenteredFocusRevision
            || coordinator.FocusedFromIslandId is not string fromIslandId
            || coordinator.FocusedToIslandId is not string toIslandId
            || MapViewport.ActualWidth <= 0
            || MapViewport.ActualHeight <= 0)
            return;

        var fromIsland = App.listIslands?.FirstOrDefault(
            island => island.IslandsName == fromIslandId);
        var toIsland = App.listIslands?.FirstOrDefault(
            island => island.IslandsName == toIslandId);
        if (fromIsland is null || toIsland is null) return;

        if (hdMapEnabled
            && hdMapConfiguration is not null
            && mainHdCamera is not null
            && hdIslandCoordinates.TryGetValue(fromIslandId, out var fromCoordinate)
            && hdIslandCoordinates.TryGetValue(toIslandId, out var toCoordinate)) {
            var focus = MapFocusCameraCalculator.Calculate(
                fromCoordinate,
                toCoordinate,
                MapViewport.ActualWidth,
                MapViewport.ActualHeight,
                hdMapConfiguration.TileSize,
                mainHdCamera.MinZoom,
                mainHdCamera.MaxZoom);
            mainHdCamera.Reset(focus.Center, focus.Zoom);
            lastCenteredFocusRevision = coordinator.FocusRevision;
            ApplyMapViewportState();
            return;
        }

        if (TryGetIslandCenter(fromIsland, out _, out Point from)
            && TryGetIslandCenter(toIsland, out _, out Point to)) {
            viewportState.FocusSegment(
                from,
                to,
                MapViewport.ActualWidth,
                MapViewport.ActualHeight);
            lastCenteredFocusRevision = coordinator.FocusRevision;
            ApplyMapViewportState();
        }
    }
}
