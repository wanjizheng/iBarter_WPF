namespace iBarter.Mapping;

public static class WebMercatorProjection {
    public const double MaxLatitude = 85.0511287798066;

    public static NormalizedMercatorPoint ToNormalized(GeoCoordinate coordinate) {
        double latitude = Math.Clamp(coordinate.Latitude, -MaxLatitude, MaxLatitude);
        double longitude = Math.Clamp(coordinate.Longitude, -180, 180);
        double latitudeRadians = latitude * Math.PI / 180;
        double x = (longitude + 180) / 360;
        double y = (1 - Math.Asinh(Math.Tan(latitudeRadians)) / Math.PI) / 2;
        return new NormalizedMercatorPoint(x, Math.Clamp(y, 0, 1));
    }

    public static GeoCoordinate FromNormalized(NormalizedMercatorPoint point) {
        double x = Math.Clamp(point.X, 0, 1);
        double y = Math.Clamp(point.Y, 0, 1);
        double longitude = x * 360 - 180;
        double latitude = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y))) * 180 / Math.PI;
        return new GeoCoordinate(latitude, longitude);
    }

    public static WorldPixelPoint ToWorldPixels(
        GeoCoordinate coordinate,
        int zoom,
        int tileSize) {
        var normalized = ToNormalized(coordinate);
        double worldSize = WorldSize(zoom, tileSize);
        return new WorldPixelPoint(normalized.X * worldSize, normalized.Y * worldSize);
    }

    public static double WorldSize(double zoom, int tileSize) =>
        tileSize * Math.Pow(2, zoom);
}
