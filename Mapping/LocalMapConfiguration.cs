namespace iBarter.Mapping;

using System.IO;
using System.Text.Json;

public readonly record struct MapGeoBounds(
    double South,
    double North,
    double West,
    double East) {
    public bool Contains(GeoCoordinate coordinate) =>
        coordinate.Latitude >= South
        && coordinate.Latitude <= North
        && coordinate.Longitude >= West
        && coordinate.Longitude <= East;
}

public sealed record MapRegionDefinition(
    string Id,
    MapGeoBounds Bounds,
    GeoCoordinate DefaultCenter,
    double DefaultZoom,
    int MinZoom,
    int MaxZoom,
    IReadOnlyList<string> ContainingIslands);

public sealed record LocalMapConfiguration(
    string Root,
    int TileSize,
    string TileExtension,
    int MinZoom,
    int MaxZoom,
    MapRegionDefinition MainRegion,
    MapRegionDefinition RightInsetRegion) {
    public static bool TryLoad(string root, out LocalMapConfiguration? configuration) {
        configuration = null;
        try {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var manifest = JsonSerializer.Deserialize<ManifestDto>(
                File.ReadAllText(Path.Combine(root, "manifest.json")), options);
            var regions = JsonSerializer.Deserialize<RegionsDto>(
                File.ReadAllText(Path.Combine(root, "regions.json")), options);
            if (manifest?.Projection is null
                || !manifest.Projection.Verified
                || !StringComparer.OrdinalIgnoreCase.Equals(
                    manifest.Projection.Name, "WebMercator")
                || !StringComparer.OrdinalIgnoreCase.Equals(manifest.TileScheme, "XYZ")
                || manifest.TileSize <= 0
                || manifest.MinZoom > manifest.MaxZoom
                || regions?.Regions is null)
                return false;

            var main = ParseRegion(regions.Regions.FirstOrDefault(x => x.Id == "main"));
            var right = ParseRegion(
                regions.Regions.FirstOrDefault(x => x.Id == "north-east-inset"));
            if (main is null || right is null) return false;

            string extension = "." + (manifest.TileFormat ?? "jpg").Trim().TrimStart('.');
            configuration = new LocalMapConfiguration(
                Path.GetFullPath(root),
                manifest.TileSize,
                extension,
                manifest.MinZoom,
                manifest.MaxZoom,
                main,
                right);
            return true;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException) {
            return false;
        }
    }

    private static MapRegionDefinition? ParseRegion(RegionDto? region) {
        if (region?.WorldLatRange is not { Length: 2 }
            || region.WorldLonRange is not { Length: 2 }
            || region.ZoomRange is not { Length: 2 })
            return null;
        return new MapRegionDefinition(
            region.Id ?? string.Empty,
            new MapGeoBounds(
                Math.Min(region.WorldLatRange[0], region.WorldLatRange[1]),
                Math.Max(region.WorldLatRange[0], region.WorldLatRange[1]),
                Math.Min(region.WorldLonRange[0], region.WorldLonRange[1]),
                Math.Max(region.WorldLonRange[0], region.WorldLonRange[1])),
            new GeoCoordinate(region.DefaultCenterLat, region.DefaultCenterLon),
            region.DefaultZoom,
            region.ZoomRange.Min(),
            region.ZoomRange.Max(),
            region.ContainingIslands ?? []);
    }

    private sealed class ManifestDto {
        public ProjectionDto? Projection { get; set; }
        public string? TileScheme { get; set; }
        public int TileSize { get; set; }
        public string? TileFormat { get; set; }
        public int MinZoom { get; set; }
        public int MaxZoom { get; set; }
    }

    private sealed class ProjectionDto {
        public string? Name { get; set; }
        public bool Verified { get; set; }
    }

    private sealed class RegionsDto {
        public RegionDto[]? Regions { get; set; }
    }

    private sealed class RegionDto {
        public string? Id { get; set; }
        public double[]? WorldLatRange { get; set; }
        public double[]? WorldLonRange { get; set; }
        public double DefaultCenterLat { get; set; }
        public double DefaultCenterLon { get; set; }
        public double DefaultZoom { get; set; }
        public int[]? ZoomRange { get; set; }
        public string[]? ContainingIslands { get; set; }
    }
}
