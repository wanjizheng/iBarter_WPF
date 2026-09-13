namespace iBarter.Mapping;

public static class MapViewportProjection {
    public static ViewportPoint Project(
        GeoCoordinate coordinate,
        XyzViewportCamera camera,
        double viewportWidth,
        double viewportHeight,
        int tileSize) {
        var normalized = WebMercatorProjection.ToNormalized(coordinate);
        double worldSize = WebMercatorProjection.WorldSize(camera.Zoom, tileSize);
        return new ViewportPoint(
            (normalized.X - camera.Center.X) * worldSize + viewportWidth / 2,
            (normalized.Y - camera.Center.Y) * worldSize + viewportHeight / 2);
    }

    public static bool IsVisible(
        ViewportPoint point,
        double viewportWidth,
        double viewportHeight,
        double padding = 0) =>
        point.X >= -padding
        && point.X <= viewportWidth + padding
        && point.Y >= -padding
        && point.Y <= viewportHeight + padding;
}
