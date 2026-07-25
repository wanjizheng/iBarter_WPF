using iBarter.Mapping;
using Xunit;

namespace IslandNavigationTests;

public sealed class BdfAffineProductionRegressionTests {
    private static NormalizedMercatorPoint Transform(double x, double y) =>
        new(0.20 + x * 0.001 + y * 0.0002,
            0.30 - x * 0.0001 + y * 0.0008);

    [Fact]
    public void Affine_fallback_uses_preserved_map_anchor_not_route_destination() {
        var inputs = new[] {
            Control("A", routeX: 9_000, routeY: 9_000, mapX: 0, mapY: 0),
            Control("B", routeX: 8_000, routeY: 8_000, mapX: 100, mapY: 0),
            Control("C", routeX: 7_000, routeY: 7_000, mapX: 0, mapY: 100),
            new MapIslandCoordinateInput(
                "Missing",
                NavigationX: 999_999,
                NavigationY: -999_999,
                NavigationSource: "bdocodex-barterer-npc-1",
                MapDisplayRegion.Main,
                CalibrationX: 40,
                CalibrationY: 60,
                MapAnchorSource: "main-map-calibrated"),
        };
        var anchors = new[] {
            Anchor("A", 0, 0),
            Anchor("B", 100, 0),
            Anchor("C", 0, 100),
        };

        var result = BdfIslandCoordinateCatalog.BuildDetailed(
            inputs, anchors, new Dictionary<string, string>());

        Assert.Equal(
            IslandResolutionMode.TrustedAffineFallback,
            result.Diagnostics["Missing"].Resolution);
        var actual = WebMercatorProjection.ToNormalized(
            result.Coordinates["Missing"]);
        var expected = Transform(40, 60);
        Assert.Equal(expected.X, actual.X, 10);
        Assert.Equal(expected.Y, actual.Y, 10);
    }

    [Fact]
    public void Untrusted_direct_anchor_cannot_change_trusted_affine_output() {
        var trusted = new[] {
            Control("A", 9_000, 9_000, 0, 0),
            Control("B", 8_000, 8_000, 100, 0),
            Control("C", 7_000, 7_000, 0, 100),
            Target("Missing", 50, 50),
        };
        var trustedAnchors = new[] {
            Anchor("A", 0, 0),
            Anchor("B", 100, 0),
            Anchor("C", 0, 100),
        };

        var baseline = BdfIslandCoordinateCatalog.BuildDetailed(
            trusted, trustedAnchors, new Dictionary<string, string>());

        var poisonedInput = new MapIslandCoordinateInput(
            "Poison",
            NavigationX: 123_456,
            NavigationY: -654_321,
            NavigationSource: "bdocodex-calibrated",
            MapDisplayRegion.Main,
            CalibrationX: 50,
            CalibrationY: 50,
            MapAnchorSource: "bdocodex-calibrated");
        var poisonedAnchor = new BdfMapAnchor(
            "Poison", "Poison", -80, 175, "village.js");

        var withPoison = BdfIslandCoordinateCatalog.BuildDetailed(
            trusted.Append(poisonedInput).ToArray(),
            trustedAnchors.Append(poisonedAnchor).ToArray(),
            new Dictionary<string, string>());

        Assert.Equal(
            baseline.Coordinates["Missing"],
            withPoison.Coordinates["Missing"]);
        Assert.Equal(
            IslandResolutionMode.TrustedAffineFallback,
            withPoison.Diagnostics["Missing"].Resolution);
    }

    [Fact]
    public void Unique_normalized_name_match_reports_normalized_mode() {
        var input = new MapIslandCoordinateInput(
            "Example", 10, 20, "main-map-calibrated", MapDisplayRegion.Main);
        var anchor = new BdfMapAnchor(
            "Example Island", null, 12, 34, "connect.js");

        var result = BdfIslandCoordinateCatalog.BuildDetailed(
            new[] { input }, new[] { anchor }, new Dictionary<string, string>());

        Assert.Equal(
            IslandResolutionMode.DirectBdfNormalizedMatch,
            result.Diagnostics["Example"].Resolution);
        Assert.Equal(new GeoCoordinate(12, 34), result.Coordinates["Example"]);
    }

    [Fact]
    public void Ambiguous_normalized_name_is_not_silently_selected() {
        var input = new MapIslandCoordinateInput(
            "Example", 10, 20, "main-map-calibrated", MapDisplayRegion.Main);
        var anchors = new[] {
            new BdfMapAnchor("Example Island", null, 12, 34, "connect.js"),
            new BdfMapAnchor("Example Port", null, 56, 78, "village.js"),
        };

        var result = BdfIslandCoordinateCatalog.BuildDetailed(
            new[] { input }, anchors, new Dictionary<string, string>());

        Assert.False(result.Coordinates.ContainsKey("Example"));
        Assert.Equal(
            IslandResolutionMode.Missing,
            result.Diagnostics["Example"].Resolution);
    }

    [Fact]
    public void Crows_nest_without_direct_anchor_uses_trusted_affine_fallback() {
        var inputs = new[] {
            Control("A", 9_000, 9_000, 0, 0),
            Control("B", 8_000, 8_000, 100, 0),
            Control("C", 7_000, 7_000, 0, 100),
            new MapIslandCoordinateInput(
                "Crows_Nest",
                NavigationX: 300,
                NavigationY: 400,
                NavigationSource: "main-map-calibrated",
                MapDisplayRegion.Main,
                CalibrationX: 30,
                CalibrationY: 70,
                MapAnchorSource: "main-map-calibrated"),
        };
        var anchors = new[] {
            Anchor("A", 0, 0),
            Anchor("B", 100, 0),
            Anchor("C", 0, 100),
        };

        var result = BdfIslandCoordinateCatalog.BuildDetailed(
            inputs, anchors,
            new Dictionary<string, string> { ["Crows_Nest"] = "Crow's Nest" });

        Assert.True(result.Coordinates.ContainsKey("Crows_Nest"));
        Assert.Equal(
            IslandResolutionMode.TrustedAffineFallback,
            result.Diagnostics["Crows_Nest"].Resolution);
        var actual = WebMercatorProjection.ToNormalized(
            result.Coordinates["Crows_Nest"]);
        var expected = Transform(30, 70);
        Assert.Equal(expected.X, actual.X, 10);
        Assert.Equal(expected.Y, actual.Y, 10);
    }

    private static MapIslandCoordinateInput Control(
        string id,
        double routeX,
        double routeY,
        double mapX,
        double mapY) =>
        new(
            id,
            routeX,
            routeY,
            "bdocodex-barterer-npc-test",
            MapDisplayRegion.Main,
            CalibrationX: mapX,
            CalibrationY: mapY,
            MapAnchorSource: "bdo-world-direct");

    private static MapIslandCoordinateInput Target(
        string id,
        double mapX,
        double mapY) =>
        new(
            id,
            999_999,
            -999_999,
            "bdocodex-barterer-npc-target",
            MapDisplayRegion.Main,
            CalibrationX: mapX,
            CalibrationY: mapY,
            MapAnchorSource: "main-map-calibrated");

    private static BdfMapAnchor Anchor(string id, double x, double y) {
        var coordinate = WebMercatorProjection.FromNormalized(Transform(x, y));
        return new BdfMapAnchor(
            id, id, coordinate.Latitude, coordinate.Longitude, "connect.js");
    }
}
