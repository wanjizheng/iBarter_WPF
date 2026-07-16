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
    private XyzViewportCamera? rightInsetCamera;
    private readonly LocalTileBitmapCache hdTileBitmapCache = new(512);

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
            MapDisplayRegion region = IslandNavigationGeometry.GetDisplayGroup(
                island.IslandsName) switch {
                    SpecialDisplayGroup.LeftInset => MapDisplayRegion.Hidden,
                    SpecialDisplayGroup.RightInset => MapDisplayRegion.RightInset,
                    _ => MapDisplayRegion.Main,
                };
            return new MapIslandCoordinateInput(
                island.IslandsName,
                island.NavigationX ?? Double.NaN,
                island.NavigationY ?? Double.NaN,
                island.NavigationSource ?? String.Empty,
                region);
        }).ToArray();
        var coordinates = BdfIslandCoordinateCatalog.Build(
            inputs, metadata.Anchors, metadata.Aliases);
        if (configuration.RightInsetRegion.ContainingIslands.Any(
            islandId => !coordinates.ContainsKey(islandId))) {
            UseStaticMapFallback();
            return;
        }

        hdMapConfiguration = configuration;
        hdIslandCoordinates = coordinates;
        mainHdCamera = CreateCamera(configuration.MainRegion);
        rightInsetCamera = CreateCamera(configuration.RightInsetRegion);
        var catalog = new LocalTileCatalog(root, configuration.TileExtension);

        MapScaleTransform.ScaleX = 1;
        MapScaleTransform.ScaleY = 1;
        MapTranslateTransform.X = 0;
        MapTranslateTransform.Y = 0;
        Image_BackgroundMap.Visibility = Visibility.Collapsed;
        MainTileLayer.Configure(
            catalog, mainHdCamera, configuration.TileSize, hdTileBitmapCache);
        RightInsetTileLayer.Configure(
            catalog, rightInsetCamera, configuration.TileSize, hdTileBitmapCache);
        RightInsetPanel.Visibility = Visibility.Visible;
        hdMapEnabled = true;
        UpdateRightInsetSize();
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
        rightInsetCamera = null;
        MainTileLayer.Disable();
        RightInsetTileLayer.Disable();
        RightInsetPanel.Visibility = Visibility.Collapsed;
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
        RightInsetTileLayer.RefreshTiles();
        IslandsButtonRearrange();
    }

    private void UpdateRightInsetSize() {
        if (!hdMapEnabled || MapViewport.ActualWidth <= 0 || MapViewport.ActualHeight <= 0)
            return;
        RightInsetPanel.Width = Math.Clamp(MapViewport.ActualWidth * 0.30, 500, 560);
        RightInsetPanel.Height = Math.Clamp(MapViewport.ActualHeight * 0.34, 220, 380);
    }

    private MapDisplayRegion GetDisplayRegion(Islands island) {
        if (!hdMapEnabled) return MapDisplayRegion.Main;
        return IslandNavigationGeometry.GetDisplayGroup(island.IslandsName) switch {
            SpecialDisplayGroup.LeftInset => MapDisplayRegion.Hidden,
            SpecialDisplayGroup.RightInset => MapDisplayRegion.RightInset,
            _ => MapDisplayRegion.Main,
        };
    }

    private Grid? GetOverlayHost(Islands island) => GetDisplayRegion(island) switch {
        MapDisplayRegion.Main => Grid_MapMain,
        MapDisplayRegion.RightInset => Grid_RightInsetOverlay,
        _ => null,
    };

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
        XyzViewportCamera? camera = GetDisplayRegion(island) == MapDisplayRegion.RightInset
            ? rightInsetCamera
            : mainHdCamera;
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
}
