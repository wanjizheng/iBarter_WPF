using System.IO;
using System.Text.Json;
using iBarter.Mapping;
using iBarter.Navigation;
using Xunit;

namespace IslandNavigationTests;

/// <summary>
/// Live verification of the HD map fix using the real
/// Resources/MapTiles/bdf-anchors.json + ibarter-bdf-aliases.json
/// shipped in the repo. These tests read the actual data so a
/// regression on the canonical sources is caught immediately.
///
/// The script mirrors what View/MapControl.HdMap.cs.TryInitializeHdMap
/// does in production: parse the JSON files, build
/// <see cref="MapIslandCoordinateInput"/> for each
/// <see cref="iBarter.Model.Islands"/>, run
/// <see cref="BdfIslandCoordinateCatalog.BuildDetailed"/>, and
/// assert each tracked island lands in the expected region of the
/// rendered map.
/// </summary>
public class BdfRealDataResolutionTests {

    private const string MapTilesRoot = "Resources/MapTiles";

    /// <summary>
    /// Walk up from the binary output directory to the repo root
    /// where Resources/MapTiles lives. Tests run with the working
    /// directory set to the bin folder; we need the real source.
    /// </summary>
    private static string FindRepoMapTiles() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null) {
            string candidate = Path.Combine(dir.FullName, "Resources", "MapTiles");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        // Fallback to literal; assertion will fail with the path.
        return Path.Combine(AppContext.BaseDirectory, MapTilesRoot);
    }

    [Fact]
    public void Dallae_resolves_to_northern_map_region_using_real_bdf_anchors() {
        var (inputs, anchors, aliases) = LoadFor(new[] { "Dallae" });

        var result = BdfIslandCoordinateCatalog.BuildDetailed(
            inputs, anchors, aliases);

        Assert.True(result.Coordinates.ContainsKey("Dallae"));
        var normalized = WebMercatorProjection.ToNormalized(
            result.Coordinates["Dallae"]);

        // BDO Codex places Dallae Pier at the northernmost tip of
        // the world map. In WebMercator normalized coords
        // (Y=0=north, Y=1=south), that lands at Y ≤ 0.20 with the
        // exact anchor at lat=77.73 producing Y≈0.166.
        Assert.True(normalized.Y <= 0.20,
            $"Dallae Pier is at the northernmost tip but normalized Y = {normalized.Y}");
        Assert.True(normalized.Y >= 0.05,
            $"Dallae must be inside the rendered map (Y>0); got {normalized.Y}");

        var diag = result.Diagnostics["Dallae"];
        // Resolution must NOT be the affine fallback; we want the
        // direct BDF match (or alias match).
        Assert.NotEqual(IslandResolutionMode.TrustedAffineFallback, diag.Resolution);
        Assert.NotEqual(IslandResolutionMode.Missing, diag.Resolution);
        Assert.True(
            diag.Resolution == IslandResolutionMode.DirectBdfMatch
            || diag.Resolution == IslandResolutionMode.DirectBdfAliasMatch
            || diag.Resolution == IslandResolutionMode.DirectBdfNormalizedMatch,
            $"Dallae must resolve via a direct BDF path; got {diag.Resolution}");
    }

    [Fact]
    public void Key_islands_all_resolve_to_expected_regions() {
        // Each row: (iBarter island, expected BDF source name,
        // min normalized Y, max normalized Y, region label).
        // In WebMercator normalized coords, Y=0 is north (top of map)
// and Y=1 is south (bottom). So "north edge" = small Y,
// "south edge" = large Y. The brief's expected ranges are
// interpreted under this convention.
var expectations = new[] {
            new ExpectationRow("Dallae",    "Dallae Pier",            0.05, 0.25, "north edge"),
            new ExpectationRow("Haemo",     "Haemo Island",           0.05, 0.35, "north island"),
            new ExpectationRow("Midnight",  "Starry Midnight Port",   0.65, 0.95, "south edge"),
            new ExpectationRow("Iliya",     "Iliyia Island",          0.20, 0.55, "Baleria coast"),
            new ExpectationRow("Padix",     "Padix Island",           0.25, 0.60, "mid-west sea"),
            new ExpectationRow("Hakoven",   "Hakoven Island",         0.20, 0.55, "far east island"),
        };

        var names = expectations.Select(e => e.Island).ToArray();
        var (inputs, anchors, aliases) = LoadFor(names);

        var result = BdfIslandCoordinateCatalog.BuildDetailed(
            inputs, anchors, aliases);

        foreach (var row in expectations) {
            Assert.True(result.Coordinates.ContainsKey(row.Island),
                $"{row.Island} ({row.Label}) must resolve from real bdf-anchors.json");
            var normalized = WebMercatorProjection.ToNormalized(
                result.Coordinates[row.Island]);
            Assert.True(normalized.Y >= row.MinY && normalized.Y <= row.MaxY,
                $"{row.Island} ({row.Label}) expected normalized Y in [{row.MinY:0.###}, {row.MaxY:0.###}] " +
                $"but got {normalized.Y:0.###} (anchor '{result.Diagnostics[row.Island].MatchedBdfSourceName}')");
            Assert.Equal(row.ExpectedSource, result.Diagnostics[row.Island].MatchedBdfSourceName);
        }
    }

    [Fact]
    public void Diagnostic_log_for_key_islands_includes_route_and_anchor() {
        // The resolution-diagnostics log path has to expose every
        // field the brief requires: islandId, route destination
        // x/y/source, map anchor x/y/source, matched BDF source,
        // matched IBarter name, resolution mode, final GeoCoordinate,
        // final normalized Mercator, affine residual. This test
        // pins the shape.
        var (inputs, anchors, aliases) = LoadFor(new[] { "Dallae", "Haemo" });

        var result = BdfIslandCoordinateCatalog.BuildDetailed(
            inputs, anchors, aliases);

        var dallae = result.Diagnostics["Dallae"];
        Assert.Equal("Dallae", dallae.IslandId);
        Assert.False(string.IsNullOrEmpty(dallae.RouteDestinationSource));
        Assert.False(string.IsNullOrEmpty(dallae.MapAnchorSource));
        Assert.False(string.IsNullOrEmpty(dallae.MatchedBdfSourceName));
        Assert.NotNull(dallae.Coordinate);
        Assert.NotNull(dallae.Normalized);
        Assert.True(dallae.AffineResidual >= 0.0);
    }

    private static (MapIslandCoordinateInput[] inputs, BdfMapAnchor[] anchors,
        Dictionary<string, string> aliases) LoadFor(string[] islandNames) {
        // The test process's working directory is the binary
        // output folder (Tools/IslandNavigationTests/bin/Debug/...);
        // Resources/MapTiles lives at the repo root. Walk up to
        // find it so the test reads the real shipped data.
        string mapTilesRoot = FindRepoMapTiles();
        Assert.True(Directory.Exists(mapTilesRoot),
            $"Could not find Resources/MapTiles at {mapTilesRoot}");
        var anchorsDto = JsonSerializer.Deserialize<AnchorsDto>(
            File.ReadAllText(Path.Combine(mapTilesRoot, "bdf-anchors.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var aliasesDto = JsonSerializer.Deserialize<AliasesDto>(
            File.ReadAllText(Path.Combine(mapTilesRoot, "ibarter-bdf-aliases.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(anchorsDto);
        Assert.NotNull(anchorsDto.Anchors);
        var anchors = anchorsDto.Anchors
            .Where(anchor => !string.IsNullOrWhiteSpace(anchor.SourceName)
                && double.IsFinite(anchor.Lat)
                && double.IsFinite(anchor.Lon))
            .Select(anchor => new BdfMapAnchor(
                anchor.SourceName!,
                anchor.IBarterIslandName,
                anchor.Lat,
                anchor.Lon,
                anchor.SourceUrl ?? string.Empty))
            .ToArray();

        BdfMapMetadataLoader.TryLoad(mapTilesRoot, out var metadata);
        var aliases = metadata?.Aliases;
        var aliasDict = new Dictionary<string, string>(StringComparer.Ordinal);
        if (aliases is not null) {
            foreach (var kv in aliases) aliasDict[kv.Key] = kv.Value;
        }

        var inputs = islandNames.Select(name => new MapIslandCoordinateInput(
            IslandId: name,
            NavigationX: Double.NaN,
            NavigationY: Double.NaN,
            NavigationSource: "Resources/Islands.csv",
            MapDisplayRegion.Main,
            PreferNavigationCalibration: false,
            CalibrationX: null,
            CalibrationY: null,
            MapAnchorSource: "Resources/Islands.csv"))
        .ToArray();
        return (inputs, anchors, aliasDict);
    }

    private sealed class ExpectationRow {
        public string Island { get; }
        public string ExpectedSource { get; }
        public double MinY { get; }
        public double MaxY { get; }
        public string Label { get; }
        public ExpectationRow(string island, string expectedSource, double minY, double maxY, string label) {
            Island = island;
            ExpectedSource = expectedSource;
            MinY = minY;
            MaxY = maxY;
            Label = label;
        }
    }

    private sealed class AnchorsDto {
        public AnchorDto[]? Anchors { get; set; }
    }

    private sealed class AnchorDto {
        public string? SourceName { get; set; }
        public string? IBarterIslandName { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
        public string? SourceUrl { get; set; }
    }

    private sealed class AliasesDto {
        public AliasDto[]? Aliases { get; set; }
    }

    private sealed class AliasDto {
        public string? IBarter { get; set; }
        public string? BdfSourceName { get; set; }
    }
}