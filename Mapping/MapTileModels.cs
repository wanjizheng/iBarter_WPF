namespace iBarter.Mapping;

public readonly record struct GeoCoordinate(double Latitude, double Longitude);
public readonly record struct NormalizedMercatorPoint(double X, double Y);
public readonly record struct WorldPixelPoint(double X, double Y);
public readonly record struct ViewportPoint(double X, double Y);
public readonly record struct TileAddress(int Zoom, int X, int Y);

public readonly record struct VisibleTilePlacement(
    TileAddress Address,
    double ScreenX,
    double ScreenY,
    double DisplaySize);

public sealed record VisibleTileLayout(
    int TileZoom,
    IReadOnlyList<VisibleTilePlacement> Tiles);
