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
                PreferNavigationCalibration:
                    IslandNavigationGeometry.LeftInsetNames.Contains(island.IslandsName)
                    || island.NavigationSource.StartsWith(
                        "bdocodex-barterer-", StringComparison.OrdinalIgnoreCase),
                CalibrationX: island.MapAnchorX,
                CalibrationY: island.MapAnchorY);
        }).ToArray();
        var coordinates = BdfIslandCoordinateCatalog.Build(
            inputs, metadata.Anchors, metadata.Aliases);

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
