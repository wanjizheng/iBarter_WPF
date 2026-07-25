using iBarter.Mapping;
using Xunit;

namespace IslandNavigationTests;

public sealed class BdfIslandCoordinateCatalogTests {
    [Fact]
    public void Explicit_bdf_anchor_wins_for_former_right_inset_island_on_main_map() {
        var inputs = new[] {
            new MapIslandCoordinateInput(
                "Hakoven", 100, 200, "bdo-world-direct", MapDisplayRegion.Main),
        };
        var anchors = new[] {
            new BdfMapAnchor("Hakoven Island", "Hakoven", 38, 150, "connect.js"),
        };

        var catalog = BdfIslandCoordinateCatalog.Build(
            inputs, anchors, new Dictionary<string, string>());

        Assert.Equal(new GeoCoordinate(38, 150), catalog["Hakoven"]);
    }

    [Fact]
    public void Olvia_alias_selects_the_coast_exchange_point_instead_of_the_village() {
        var inputs = new[] {
            new MapIslandCoordinateInput(
                "Olvia", -142326, 126708, "bdo-world-direct", MapDisplayRegion.Main),
        };
        var anchors = new[] {
            new BdfMapAnchor("Olvia", null, -5.747174, -5.515137, "village.js"),
            new BdfMapAnchor(
                "Olvia Coast", null,
                -4.565473550710278, -0.0439453125, "connect.js"),
        };

        var catalog = BdfIslandCoordinateCatalog.Build(
            inputs,
            anchors,
            new Dictionary<string, string> {
                ["Olvia"] = "Olvia Coast",
            });

        Assert.Equal(
            new GeoCoordinate(-4.565473550710278, -0.0439453125),
            catalog["Olvia"]);
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
    public void Former_left_inset_island_is_added_to_main_map_by_affine_fallback() {
        var inputs = new[] {
            Input("A", 0, 0),
            Input("B", 100, 0),
            Input("C", 0, 100),
            new MapIslandCoordinateInput(
                "Cox_Pirate", 50, 50, "bdocodex-calibrated",
                MapDisplayRegion.Main, PreferNavigationCalibration: true),
        };
        var anchors = inputs.Take(3).Select(input =>
            new BdfMapAnchor(
                input.IslandId, input.IslandId,
                input.NavigationY / 10, input.NavigationX / 10, "connect.js"))
            .Append(new BdfMapAnchor(
                "Ancient unrelated node", "Cox_Pirate", 80, 170, "connect.js"))
            .ToArray();

        var catalog = BdfIslandCoordinateCatalog.Build(
            inputs, anchors, new Dictionary<string, string>());

        Assert.True(catalog.ContainsKey("Cox_Pirate"));
        Assert.NotEqual(new GeoCoordinate(80, 170), catalog["Cox_Pirate"]);
    }

    [Fact]
    public void Barterer_destination_uses_node_anchor_only_for_calibration() {
        static NormalizedMercatorPoint Transform(double x, double y) =>
            new(0.2 + x * 0.001, 0.3 + y * 0.001);
        var inputs = new[] {
            Input("A", 0, 0),
            Input("B", 100, 0),
            Input("C", 0, 100),
            new MapIslandCoordinateInput(
                "Padix", 40, 80, "bdocodex-barterer-npc-58915",
                MapDisplayRegion.Main,
                PreferNavigationCalibration: true,
                CalibrationX: 40,
                CalibrationY: 60),
        };
        var anchors = inputs.Select(input => {
            double anchorX = input.CalibrationX ?? input.NavigationX;
            double anchorY = input.CalibrationY ?? input.NavigationY;
            GeoCoordinate coordinate = WebMercatorProjection.FromNormalized(
                Transform(anchorX, anchorY));
            return new BdfMapAnchor(
                input.IslandId, input.IslandId,
                coordinate.Latitude, coordinate.Longitude, "connect.js");
        }).ToArray();

        var catalog = BdfIslandCoordinateCatalog.Build(
            inputs, anchors, new Dictionary<string, string>());
        var actual = WebMercatorProjection.ToNormalized(catalog["Padix"]);
        var nodeAnchor = WebMercatorProjection.ToNormalized(
            new GeoCoordinate(anchors[3].Latitude, anchors[3].Longitude));

        Assert.Equal(Transform(40, 80).X, actual.X, 10);
        Assert.Equal(Transform(40, 80).Y, actual.Y, 10);
        Assert.NotEqual(nodeAnchor, actual);
    }

    private static MapIslandCoordinateInput Input(string id, double x, double y) =>
        new(id, x, y, "bdo-world-direct", MapDisplayRegion.Main);
}
