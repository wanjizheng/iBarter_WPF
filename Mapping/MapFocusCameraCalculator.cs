namespace iBarter.Mapping;

public readonly record struct MapFocusCamera(
    NormalizedMercatorPoint Center,
    double Zoom);

public static class MapFocusCameraCalculator {
    public static MapFocusCamera Calculate(
        GeoCoordinate from,
        GeoCoordinate to,
        double viewportWidth,
        double viewportHeight,
        int tileSize,
        int minZoom,
        int maxZoom,
        double occupiedFraction = 0.70) {
        var a = WebMercatorProjection.ToNormalized(from);
        var b = WebMercatorProjection.ToNormalized(to);
        var center = new NormalizedMercatorPoint(
            (a.X + b.X) / 2,
            (a.Y + b.Y) / 2);
        double spanX = Math.Abs(a.X - b.X);
        double spanY = Math.Abs(a.Y - b.Y);
        double usableWidth = Math.Max(1, viewportWidth * occupiedFraction);
        double usableHeight = Math.Max(1, viewportHeight * occupiedFraction);
        double requiredWorldSizeX = spanX > 1e-9
            ? usableWidth / spanX
            : Double.PositiveInfinity;
        double requiredWorldSizeY = spanY > 1e-9
            ? usableHeight / spanY
            : Double.PositiveInfinity;
        double requiredWorldSize = Math.Min(requiredWorldSizeX, requiredWorldSizeY);
        double zoom = double.IsFinite(requiredWorldSize)
            ? Math.Log2(requiredWorldSize / tileSize)
            : maxZoom;
        return new MapFocusCamera(center, Math.Clamp(zoom, minZoom, maxZoom));
    }
}
