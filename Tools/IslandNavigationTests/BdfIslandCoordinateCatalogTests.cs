using iBarter.Mapping;
using Xunit;

namespace IslandNavigationTests;

public sealed class BdfIslandCoordinateCatalogTests {
    [Fact]
    public void Explicit_bdf_anchor_wins_for_right_inset_island() {
        var inputs = new[] {
            new MapIslandCoordinateInput(
                "Hakoven", 100, 200, "bdo-world-direct", MapDisplayRegion.RightInset),
        };
        var anchors = new[] {
            new BdfMapAnchor("Hakoven Island", "Hakoven", 38, 150, "connect.js"),
        };

        var catalog = BdfIslandCoordinateCatalog.Build(
            inputs, anchors, new Dictionary<string, string>());

        Assert.Equal(new GeoCoordinate(38, 150), catalog["Hakoven"]);
    }

    [Fact]
    public void Affine_calibration_fills_main_map_island_without_direct_anchor() {
        static NormalizedMercatorPoint Transform(double x, double y) =>
            new(0.2 + x * 0.001 + y * 0.0002, 0.3 - x * 0.0001 + y * 0.0008);
        var inputs = new[] {
            Input("A", 0, 0),
            Input("B", 100, 0),
            Input("C", 0, 100),
            Input("D", 100, 100),
            new MapIslandCoordinateInput(
                "Missing", 40, 60, "main-map-calibrated", MapDisplayRegion.Main),
        };
        var anchors = inputs.Take(4).Select(input => {
            GeoCoordinate coordinate = WebMercatorProjection.FromNormalized(
                Transform(input.NavigationX, input.NavigationY));
            return new BdfMapAnchor(
                input.IslandId, input.IslandId,
                coordinate.Latitude, coordinate.Longitude, "connect.js");
        }).ToArray();

        var catalog = BdfIslandCoordinateCatalog.Build(
            inputs, anchors, new Dictionary<string, string>());
        var actual = WebMercatorProjection.ToNormalized(catalog["Missing"]);
        var expected = Transform(40, 60);

        Assert.Equal(expected.X, actual.X, 10);
        Assert.Equal(expected.Y, actual.Y, 10);
    }

    [Fact]
    public void Hidden_left_inset_island_is_not_added_by_affine_fallback() {
        var inputs = new[] {
            Input("A", 0, 0),
            Input("B", 100, 0),
            Input("C", 0, 100),
            new MapIslandCoordinateInput(
                "Hidden", 50, 50, "bdocodex-calibrated", MapDisplayRegion.Hidden),
        };
        var anchors = inputs.Take(3).Select(input =>
            new BdfMapAnchor(
                input.IslandId, input.IslandId,
                input.NavigationY / 10, input.NavigationX / 10, "connect.js")).ToArray();

        var catalog = BdfIslandCoordinateCatalog.Build(
            inputs, anchors, new Dictionary<string, string>());

        Assert.False(catalog.ContainsKey("Hidden"));
    }

    private static MapIslandCoordinateInput Input(string id, double x, double y) =>
        new(id, x, y, "bdo-world-direct", MapDisplayRegion.Main);
}
