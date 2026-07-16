using System.IO;
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
            AppContext.BaseDirectory, EnumerateDevelopmentMapRoots());
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
                    IslandNavigationGeometry.LeftInsetNames.Contains(island.IslandsName));
        }).ToArray();
        var coordinates = BdfIslandCoordinateCatalog.Build(
            inputs, metadata.Anchors, metadata.Aliases);

        hdMapConfiguration = configuration;
        hdIslandCoordinates = coordinates;
        mainHdCamera = CreateCamera(configuration.MainRegion);
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

    private static IEnumerable<string> EnumerateDevelopmentMapRoots() {
#if DEBUG
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null) {
            string sibling = Path.Combine(
                directory.FullName, "BDOMap", "Resource", "MapTiles");
            if (seen.Add(sibling)) yield return sibling;
            directory = directory.Parent;
        }

        string configured = Path.Combine(
            "E:", "wanjizheng", "MyProject", "BDOMap", "Resource", "MapTiles");
        if (seen.Add(configured)) yield return configured;
#else
        yield break;
#endif
    }

    private void RefreshHdMap() {
        if (!hdMapEnabled) return;
        MainTileLayer.RefreshTiles();
        IslandsButtonRearrange();
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
            center = new Point(
                normalized.X * host.ActualWidth,
                normalized.Y * host.ActualHeight);
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
