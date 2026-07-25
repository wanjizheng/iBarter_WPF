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
    private IReadOnlyDictionary<string, GeoCoordinate> hdRouteCoordinates =
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
                PreferNavigationCalibration: false,
                CalibrationX: island.MapAnchorX,
                CalibrationY: island.MapAnchorY,
                MapAnchorSource: island.MapAnchorSource ?? string.Empty);
        }).ToArray();
        var catalogResult = BdfIslandCoordinateCatalog.BuildDetailed(
            inputs, metadata.Anchors, metadata.Aliases);
        hdIslandCoordinates = catalogResult.Coordinates;
        hdRouteCoordinates = catalogResult.RouteCoordinates;
        EmitResolutionDiagnostics(catalogResult.Diagnostics);
        ReportRouteDestinationProvenance();

        hdMapConfiguration = configuration;
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
        hdRouteCoordinates =
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
        ReportSanityWarnings(diagnostics);
    }

    private void ReportRouteDestinationProvenance() {
        if (App.listIslands is null) return;

        string[] allFallbacks = App.listIslands
            .Where(island => island.HasNavigationCoordinates
                && island.UsesCatalogFallbackDestination)
            .Select(island => island.IslandsName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // The complete catalog gap is useful diagnostics, not an application
        // error. Keep it neutral and reserve the warning colour for fallbacks
        // that are actually present in the user's active route.
        if (allFallbacks.Length > 0) {
            App.myCFun?.Log(
                $"[route-coordinate] {allFallbacks.Length} locations use catalog fallback " +
                $"coordinates rather than an explicit barter NPC: {String.Join(", ", allFallbacks)}",
                System.Windows.Media.Brushes.Gray);
        }

        var activeIds = App.myPVM?.BarterCollection
            .Where(barter => !barter.ExchangeDone && barter.ExchangeQuantity > 0)
            .Select(barter => barter.IsLandName)
            .ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);
        string[] activeFallbacks = App.listIslands
            .Where(island => activeIds.Contains(island.IslandsName)
                && island.UsesCatalogFallbackDestination)
            .Select(island => island.IslandsName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (activeFallbacks.Length > 0) {
            App.myCFun?.Log(
                $"[route-coordinate] Active route contains non-NPC fallback stops: " +
                $"{String.Join(", ", activeFallbacks)}. Map overlays and distance use " +
                $"NavigationX/Y consistently, but these values are not yet explicit NPC positions.",
                System.Windows.Media.Brushes.OrangeRed);
        }
    }

    private void ReportSanityWarnings(
        IReadOnlyDictionary<string, IslandCoordinateResult> diagnostics) {
        // Only retain bands backed by a direct map anchor or a well-established
        // geographic expectation. Crow's Ship and Crow's Nest currently resolve
        // through affine fallback and have no direct BDF anchor; treating them as
        // definitely "south" produced false red alarms and conveyed more certainty
        // than the source data supports.
        (string island, double minY, double maxY, string label)[] bands = {
            ("Dallae",     0.05, 0.25, "north edge"),
            ("Haemo",      0.05, 0.35, "northern island"),
            ("Midnight",   0.65, 0.95, "south edge"),
            ("Iliya",      0.20, 0.55, "Baleria coast"),
            ("Padix",      0.25, 0.60, "mid-west sea"),
            ("Hakoven",    0.20, 0.50, "far east island"),
            ("Cox_Pirate", 0.35, 0.65, "central archipelago"),
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
            var staticProjected = MapProjectionHelper.ProjectNormalized(
                normalized.X,
                normalized.Y,
                host.ActualWidth,
                host.ActualHeight);
            center = new Point(staticProjected.X, staticProjected.Y);
            return host.ActualWidth > 0 && host.ActualHeight > 0;
        }

        if (hdMapConfiguration is null
            || !hdRouteCoordinates.TryGetValue(island.IslandsName, out var coordinate))
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
            && hdRouteCoordinates.TryGetValue(fromIslandId, out var fromCoordinate)
            && hdRouteCoordinates.TryGetValue(toIslandId, out var toCoordinate)) {
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
