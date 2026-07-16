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

    private static NormalizedMercatorPoint Clamp(NormalizedMercatorPoint point) =>
        new(Math.Clamp(point.X, 0, 1), Math.Clamp(point.Y, 0, 1));
}
