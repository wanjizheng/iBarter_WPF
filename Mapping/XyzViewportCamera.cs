namespace iBarter.Mapping;

public sealed class XyzViewportCamera {
    public NormalizedMercatorPoint Center { get; private set; }
    public double Zoom { get; private set; }
    public int MinZoom { get; }
    public int MaxZoom { get; }

    public XyzViewportCamera(
        NormalizedMercatorPoint center,
        double zoom,
        int minZoom,
        int maxZoom) {
        if (minZoom > maxZoom) throw new ArgumentOutOfRangeException(nameof(minZoom));
        MinZoom = minZoom;
        MaxZoom = maxZoom;
        Center = Clamp(center);
        Zoom = Math.Clamp(zoom, minZoom, maxZoom);
    }

    public void Reset(NormalizedMercatorPoint center, double zoom) {
        Center = Clamp(center);
        Zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
    }

    public void PanByScreen(double deltaX, double deltaY, int tileSize) {
        double worldSize = WebMercatorProjection.WorldSize(Zoom, tileSize);
        Center = Clamp(new NormalizedMercatorPoint(
            Center.X - deltaX / worldSize,
            Center.Y - deltaY / worldSize));
    }

    public void ZoomAt(
        double screenX,
        double screenY,
        double viewportWidth,
        double viewportHeight,
        double nextZoom,
        int tileSize) {
        nextZoom = Math.Clamp(nextZoom, MinZoom, MaxZoom);
        if (Math.Abs(nextZoom - Zoom) < 0.000001) return;

        double oldWorldSize = WebMercatorProjection.WorldSize(Zoom, tileSize);
        var anchor = new NormalizedMercatorPoint(
            Center.X + (screenX - viewportWidth / 2) / oldWorldSize,
            Center.Y + (screenY - viewportHeight / 2) / oldWorldSize);
        double newWorldSize = WebMercatorProjection.WorldSize(nextZoom, tileSize);
        Center = Clamp(new NormalizedMercatorPoint(
            anchor.X - (screenX - viewportWidth / 2) / newWorldSize,
            anchor.Y - (screenY - viewportHeight / 2) / newWorldSize));
        Zoom = nextZoom;
    }

    /// <summary>
    /// Keeps the visible camera rectangle inside the supplied map coverage.
    /// The effective minimum zoom increases with the viewport size, so users
    /// cannot zoom out far enough to expose the tile layer's empty exterior.
    /// Call this after pan, zoom, focus, or a viewport resize.
    /// </summary>
    public void ConstrainTo(
        MapGeoBounds bounds,
        double viewportWidth,
        double viewportHeight,
        int tileSize) {
        if (!double.IsFinite(viewportWidth)
            || !double.IsFinite(viewportHeight)
            || viewportWidth <= 0
            || viewportHeight <= 0
            || tileSize <= 0)
            return;

        NormalizedMercatorPoint northWest = WebMercatorProjection.ToNormalized(
            new GeoCoordinate(bounds.North, bounds.West));
        NormalizedMercatorPoint southEast = WebMercatorProjection.ToNormalized(
            new GeoCoordinate(bounds.South, bounds.East));
        double left = Math.Min(northWest.X, southEast.X);
        double right = Math.Max(northWest.X, southEast.X);
        double top = Math.Min(northWest.Y, southEast.Y);
        double bottom = Math.Max(northWest.Y, southEast.Y);
        double normalizedWidth = right - left;
        double normalizedHeight = bottom - top;
        if (normalizedWidth <= 0 || normalizedHeight <= 0) return;

        double requiredWorldSize = Math.Max(
            viewportWidth / normalizedWidth,
            viewportHeight / normalizedHeight);
        double requiredZoom = Math.Log2(requiredWorldSize / tileSize);
        double effectiveMinZoom = Math.Clamp(
            Math.Max(MinZoom, requiredZoom), MinZoom, MaxZoom);
        Zoom = Math.Clamp(Math.Max(Zoom, effectiveMinZoom), MinZoom, MaxZoom);

        double worldSize = WebMercatorProjection.WorldSize(Zoom, tileSize);
        double halfWidth = viewportWidth / (2 * worldSize);
        double halfHeight = viewportHeight / (2 * worldSize);
        Center = new NormalizedMercatorPoint(
            ClampAxis(Center.X, left + halfWidth, right - halfWidth),
            ClampAxis(Center.Y, top + halfHeight, bottom - halfHeight));
    }

    private static double ClampAxis(double value, double minimum, double maximum) =>
        minimum > maximum
            ? (minimum + maximum) / 2
            : Math.Clamp(value, minimum, maximum);

    private static NormalizedMercatorPoint Clamp(NormalizedMercatorPoint point) =>
        new(Math.Clamp(point.X, 0, 1), Math.Clamp(point.Y, 0, 1));
}
