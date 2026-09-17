namespace iBarter.View;

using iBarter.Mapping;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

public sealed class LocalXyzTileLayer : Canvas {
    private readonly Dictionary<TileAddress, Image> visibleImages = [];
    private LocalTileBitmapCache bitmapCache = new();
    private LocalTileCatalog? catalog;
    private XyzViewportCamera? camera;
    private int tileSize = 256;
    private int refreshGeneration;

    public XyzViewportCamera? Camera => camera;

    public LocalXyzTileLayer() {
        ClipToBounds = true;
        IsHitTestVisible = false;
        if (DesignerProperties.GetIsInDesignMode(this)) return;
        SizeChanged += (_, _) => RefreshTiles();
    }

    internal void Configure(
        LocalTileCatalog tileCatalog,
        XyzViewportCamera viewportCamera,
        int configuredTileSize,
        LocalTileBitmapCache? sharedBitmapCache = null) {
        catalog = tileCatalog;
        camera = viewportCamera;
        tileSize = configuredTileSize;
        if (sharedBitmapCache is not null) bitmapCache = sharedBitmapCache;
        Visibility = Visibility.Visible;
        RefreshTiles();
    }

    public void Disable() {
        refreshGeneration++;
        catalog = null;
        camera = null;
        visibleImages.Clear();
        Children.Clear();
        bitmapCache.Clear();
        Visibility = Visibility.Collapsed;
    }

    public void RefreshTiles() {
        if (!Dispatcher.CheckAccess()) {
            Dispatcher.BeginInvoke(RefreshTiles);
            return;
        }
        _ = RefreshTilesAsync(++refreshGeneration);
    }

    private async Task RefreshTilesAsync(int generation) {
        try {
            if (catalog is null || camera is null
                || ActualWidth <= 0 || ActualHeight <= 0)
                return;

            var layout = VisibleTileEnumerator.Enumerate(
                camera, ActualWidth, ActualHeight, tileSize);
            var desired = layout.Tiles.Select(tile => tile.Address).ToHashSet();
            foreach (var address in visibleImages.Keys.Where(x => !desired.Contains(x)).ToArray()) {
                Children.Remove(visibleImages[address]);
                visibleImages.Remove(address);
            }

            foreach (var placement in layout.Tiles)
                if (visibleImages.TryGetValue(placement.Address, out var existing))
                    Position(existing, placement);

            var pending = layout.Tiles
                .Where(placement => !visibleImages.ContainsKey(placement.Address))
                .Select(placement => (
                    Placement: placement,
                    Path: catalog.TryResolve(placement.Address)))
                .Where(item => item.Path is not null)
                .Select(async item => (
                    item.Placement,
                    Bitmap: await bitmapCache.GetAsync(item.Path!)))
                .ToArray();
            var loaded = await Task.WhenAll(pending);
            if (generation != refreshGeneration || catalog is null) return;

            foreach (var item in loaded) {
                if (item.Bitmap is null || visibleImages.ContainsKey(item.Placement.Address))
                    continue;
                var image = new Image {
                    Source = item.Bitmap,
                    Stretch = Stretch.Fill,
                    SnapsToDevicePixels = true,
                    IsHitTestVisible = false,
                };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                Position(image, item.Placement);
                visibleImages[item.Placement.Address] = image;
                Children.Add(image);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException
            and not StackOverflowException) {
            // The static map underneath is the safety fallback. A corrupt or
            // transiently unavailable tile must never break the WPF UI thread.
        }
    }

    private static void Position(Image image, VisibleTilePlacement placement) {
        image.Width = placement.DisplaySize + 0.5;
        image.Height = placement.DisplaySize + 0.5;
        SetLeft(image, placement.ScreenX);
        SetTop(image, placement.ScreenY);
    }
}
