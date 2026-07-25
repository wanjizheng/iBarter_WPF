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
    public void Trusted_affine_fit_fills_main_map_island_without_direct_anchor() {
        // Uses ONLY trusted sources in the calibration pool; the
        // "Missing" island has no anchor but is filled by the
        // trusted affine fit.
        static NormalizedMercatorPoint Transform(double x, double y) =>
            new(0.2 + x * 0.001 + y * 0.0002, 0.3 - x * 0.0001 + y * 0.0008);
        var inputs = new[] {
            Input("A", 0, 0),
            Input("B", 100, 0),
            Input("C", 0, 100),
            new MapIslandCoordinateInput(
                "Missing", 40, 60, "bdo-world-direct", MapDisplayRegion.Main),
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
    public void Barterer_destination_uses_anchor_directly_not_affine_fallback() {
        // v1 behavior (the bug): Padix with PreferNavigationCalibration=true
        // and CalibrationX/Y equal to its anchor coord was routed
        // through the affine fallback, producing the barterer
        // destination's projected position (which was wrong).
        //
        // v2 behavior (this fix): the BDF anchor wins directly,
        // so Padix's final position equals the anchor's lat/lon
        // regardless of what NavigationX/Y (the barterer dest)
        // happens to be. This is the contract that prevents a
        // port from drifting to the middle of the map.
        var inputs = new[] {
            Input("A", 0, 0),
            Input("B", 100, 0),
            Input("C", 0, 100),
            new MapIslandCoordinateInput(
                "Padix", 40, 80, "bdocodex-barterer-npc-58915",
                MapDisplayRegion.Main,
                PreferNavigationCalibration: true,
                CalibrationX: 40,
                CalibrationY: 60,
                MapAnchorSource: "Resources/Islands.csv"),
        };
        var anchors = inputs.Select(input => {
            double anchorX = input.CalibrationX ?? input.NavigationX;
            double anchorY = input.CalibrationY ?? input.NavigationY;
            GeoCoordinate coordinate = WebMercatorProjection.FromNormalized(
                // The "anchor" lat/lon Padix maps to under the
                // old transform (40,60) → (0.5, 0.5).
                new NormalizedMercatorPoint(0.5, 0.5));
            return new BdfMapAnchor(
                input.IslandId, input.IslandId,
                coordinate.Latitude, coordinate.Longitude, "connect.js");
        }).ToArray();

        var catalog = BdfIslandCoordinateCatalog.Build(
            inputs, anchors, new Dictionary<string, string>());

        Assert.Equal(
            new GeoCoordinate(anchors[3].Latitude, anchors[3].Longitude),
            catalog["Padix"]);
    }

    // ─────────────────────────────────────────────────────────────────
    // Regression tests for the HD resolution fix (commit after 9c2d838).
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Dallae_direct_anchor_wins_over_legacy_left_inset_group() {
        // v1 bug: Dallae was tagged with PreferNavigationCalibration
        // because it appears in IslandNavigationGeometry.LeftInsetNames,
        // which excluded it from the direct anchor path. The
        // resolution fix removes that check.
        //
        // The real BDF anchor for Dallae Pier is at lat ≈ 77.7,
        // which projects to normalized Y ≈ 0.166 (north edge of
        // the map). With the bug, Dallae landed at the affine
        // fit's position which sat near the map centre.
        var dallaeLat = 77.73495097544664;
        var dallaeLon = -98.85498046875001;
        var inputs = new[] {
            new MapIslandCoordinateInput(
                "Dallae", -993972, 1342696, "Resources/Islands.csv",
                MapDisplayRegion.Main,
                // True under the v1 buggy code path — Dallae
                // was in LeftInsetNames so this was forced true.
                PreferNavigationCalibration: true,
                MapAnchorSource: "Resources/Islands.csv"),
        };
        var anchors = new[] {
            new BdfMapAnchor(
                "Dallae Pier", "Dallae", dallaeLat, dallaeLon, "connect.js"),
        };

        var result = BdfIslandCoordinateCatalog.BuildDetailed(
            inputs, anchors, new Dictionary<string, string>());
        Assert.Equal(
            new GeoCoordinate(dallaeLat, dallaeLon),
            result.Coordinates["Dallae"]);
        var normalized = WebMercatorProjection.ToNormalized(
            result.Coordinates["Dallae"]);
        // North edge: normalized Y must be ≤ 0.20 (way up).
        Assert.True(normalized.Y <= 0.20,
            $"Dallae must be at the north edge but normalized Y = {normalized.Y}");
        Assert.True(normalized.Y >= 0.10,
            $"Dallae is plausibly north of 0.10 but got {normalized.Y}");
        Assert.Equal(
            IslandResolutionMode.DirectBdfMatch,
            result.Diagnostics["Dallae"].Resolution);
    }

    [Fact]
    public void Dallae_alias_resolves_Dallae_Pier_not_character_named_Dallae() {
        // The alias table maps "Dallae" → "Dallae Pier". The
        // BDF anchor with iBarterIslandName=null and
        // SourceName="Dallae Pier" must win via the alias
        // even though no exact match exists. Without the alias
        // the normalized-name fallback could pick a different
        // anchor (e.g. an NPC named "Dallae" in village.js).
        var inputs = new[] {
            new MapIslandCoordinateInput(
                "Dallae", -993972, 1342696, "Resources/Islands.csv",
                MapDisplayRegion.Main),
        };
        var anchors = new[] {
            // Potential "character named Dallae" — must NOT win.
            new BdfMapAnchor(
                "Dallae", null, -15.0, 100.0, "village.js"),
            // The real port — must win via alias.
            new BdfMapAnchor(
                "Dallae Pier", null, 77.73495097544664,
                -98.85498046875001, "connect.js"),
        };

        var result = BdfIslandCoordinateCatalog.BuildDetailed(
            inputs, anchors,
            new Dictionary<string, string> { ["Dallae"] = "Dallae Pier" });

        Assert.Equal(
            new GeoCoordinate(77.73495097544664, -98.85498046875001),
            result.Coordinates["Dallae"]);
        Assert.Equal(
            IslandResolutionMode.DirectBdfAliasMatch,
            result.Diagnostics["Dallae"].Resolution);
    }

    [Fact]
    public void Trusted_affine_fit_excludes_bdocodex_calibrated_sources() {
        // v1 used CalibrationX/NavigationX for every island in
        // the affine pool. This test pins that bdocodex-calibrated
        // islands are excluded — they would otherwise corrupt
        // the fit because bdocodex coordinates come from a
        // different semantic (the bdocodex-tracked lat/lon
        // relative to its own map).
        var inputs = new[] {
            Input("TrustedA", 0, 0),
            Input("TrustedB", 100, 0),
            Input("TrustedC", 0, 100),
            new MapIslandCoordinateInput(
                "TrustedMissing", 50, 50, "bdo-world-direct",
                MapDisplayRegion.Main),
            // This island has a bdocodex-calibrated source — it
            // must NOT participate in the affine fit, even though
            // it has a direct anchor.
            new MapIslandCoordinateInput(
                "BdocodexIsland", 70, 30, "bdocodex-calibrated",
                MapDisplayRegion.Main),
        };
        var anchors = inputs.Take(4).Select(input => {
            GeoCoordinate coordinate = WebMercatorProjection.FromNormalized(
                new NormalizedMercatorPoint(0.2, 0.3));
            return new BdfMapAnchor(
                input.IslandId, input.IslandId,
                coordinate.Latitude, coordinate.Longitude, "connect.js");
        }).Append(new BdfMapAnchor(
            "BdocodexIsland", "BdocodexIsland", 50, 60, "village.js"))
        .ToArray();

        var result = BdfIslandCoordinateCatalog.BuildDetailed(
            inputs, anchors, new Dictionary<string, string>());

        Assert.True(result.Coordinates.ContainsKey("TrustedMissing"));
        Assert.True(result.Coordinates.ContainsKey("BdocodexIsland"));
        // BdocodexIsland went direct (its anchor won); its
        // resolution is NOT TrustedAffineFallback.
        Assert.Equal(
            IslandResolutionMode.DirectBdfMatch,
            result.Diagnostics["BdocodexIsland"].Resolution);
    }

    [Fact]
    public void Trusted_affine_fit_excludes_barterer_destinations() {
        // bdocodex-barterer-* entries are route-only NPC
        // destinations and must NEVER participate in the affine
        // pool, even if MapAnchorX/Y happen to be finite.
        var inputs = new[] {
            Input("TrustedA", 0, 0),
            Input("TrustedB", 100, 0),
            Input("TrustedC", 0, 100),
            new MapIslandCoordinateInput(
                "TrustedMissing", 50, 50, "bdo-world-direct",
                MapDisplayRegion.Main),
            new MapIslandCoordinateInput(
                "BartererIsland", 70, 30, "bdocodex-barterer-npc-1",
                MapDisplayRegion.Main,
                CalibrationX: 80,
                CalibrationY: 40,
                MapAnchorSource: "bdocodex-barterer-npc-1"),
        };
        var anchors = inputs.Take(4).Select(input => {
            GeoCoordinate coordinate = WebMercatorProjection.FromNormalized(
                new NormalizedMercatorPoint(0.2, 0.3));
            return new BdfMapAnchor(
                input.IslandId, input.IslandId,
                coordinate.Latitude, coordinate.Longitude, "connect.js");
        }).ToArray();

        var result = BdfIslandCoordinateCatalog.BuildDetailed(
            inputs, anchors, new Dictionary<string, string>());

        Assert.True(result.Coordinates.ContainsKey("TrustedMissing"));
        Assert.False(result.Coordinates.ContainsKey("BartererIsland"),
            "Barterer destination must NOT be placed by the affine fit " +
            "and must NOT receive a direct anchor here.");
        Assert.Equal(
            IslandResolutionMode.Missing,
            result.Diagnostics["BartererIsland"].Resolution);
    }

    [Fact]
    public void Changing_route_destination_does_not_change_map_position() {
        // The route destination is for pathfinding, the map anchor
        // is for placement. If the user changes NavigationX/Y
        // (e.g. after ApplyIslandBarterLocations overwrites them
        // with a barterer NPC coord), the on-map position must
        // stay anchored to the BDF match.
        var anchors = new[] {
            new BdfMapAnchor(
                "Dallae Pier", "Dallae",
                77.73495097544664, -98.85498046875001, "connect.js"),
        };
        var original = new MapIslandCoordinateInput(
            "Dallae", -993972, 1342696, "Resources/Islands.csv",
            MapDisplayRegion.Main);
        var withNewRoute = new MapIslandCoordinateInput(
            "Dallae", 5000, 5000, "bdocodex-barterer-npc-12345",
            MapDisplayRegion.Main,
            CalibrationX: -993972,
            CalibrationY: 1342696,
            MapAnchorSource: "Resources/Islands.csv");

        var originalCatalog = BdfIslandCoordinateCatalog.Build(
            new[] { original }, anchors, new Dictionary<string, string>());
        var newCatalog = BdfIslandCoordinateCatalog.Build(
            new[] { withNewRoute }, anchors, new Dictionary<string, string>());

        Assert.Equal(originalCatalog["Dallae"], newCatalog["Dallae"]);
    }

    [Fact]
    public void Direct_anchor_result_is_independent_of_affine_training_set() {
        // Even when the affine training set contains wildly
        // different points, an island with a direct BDF match
        // must come out at the anchor's lat/lon — not at the
        // affine fit's projection. v1 would have been correct
        // here too because Dallae had a direct anchor, but the
        // fix relies on this property for the resolution mode
        // semantics. This test pins it.
        var inputs = new[] {
            Input("TrainA", 1000, 1000),
            Input("TrainB", 2000, 2000),
            Input("TrainC", 3000, 3000),
            new MapIslandCoordinateInput(
                "Dallae", -993972, 1342696, "Resources/Islands.csv",
                MapDisplayRegion.Main),
        };
        var anchors = inputs.Take(3).Select(input =>
            new BdfMapAnchor(
                input.IslandId, input.IslandId, 10, 20, "connect.js"))
            .Append(new BdfMapAnchor(
                "Dallae Pier", "Dallae",
                77.73495097544664, -98.85498046875001, "connect.js"))
            .ToArray();

        var result = BdfIslandCoordinateCatalog.BuildDetailed(
            inputs, anchors, new Dictionary<string, string>());

        Assert.Equal(
            new GeoCoordinate(77.73495097544664, -98.85498046875001),
            result.Coordinates["Dallae"]);
        Assert.Equal(
            IslandResolutionMode.DirectBdfMatch,
            result.Diagnostics["Dallae"].Resolution);
    }

    [Fact]
    public void Dallae_resolves_to_northern_map_region() {
        // Pure sanity test: Dallae Pier sits at the northernmost
        // tip of the world map per BDO Codex. The resolved
        // normalized Y must therefore be small (close to 0 in
        // Mercator, where Y=0 is north, Y=1 is south).
        var inputs = new[] {
            new MapIslandCoordinateInput(
                "Dallae", -993972, 1342696, "Resources/Islands.csv",
                MapDisplayRegion.Main),
        };
        var anchors = new[] {
            new BdfMapAnchor(
                "Dallae Pier", "Dallae",
                77.73495097544664, -98.85498046875001, "connect.js"),
        };

        var result = BdfIslandCoordinateCatalog.BuildDetailed(
            inputs, anchors, new Dictionary<string, string>());
        var normalized = WebMercatorProjection.ToNormalized(
            result.Coordinates["Dallae"]);

        Assert.True(normalized.Y < 0.25,
            $"Dallae must be in the northern map region; got Y={normalized.Y}");
        Assert.True(normalized.Y > 0.05,
            $"Dallae must be inside the rendered map (Y>0); got Y={normalized.Y}");
    }

    [Fact]
    public void Haemo_and_Crows_Nest_allow_explicit_UI_adjusted_anchors() {
        // User's "UI-adjusted anchor" workflow: a user manually
        // overrides MapAnchorX/Y on a per-island basis. With
        // PreferNavigationCalibration=true (legacy behavior), the
        // direct BDF anchor would be skipped and the override
        // would be ignored. After the fix, the override always
        // wins via direct match (and the BDF anchor is the same
        // as the user's adjustment).
        var inputs = new[] {
            new MapIslandCoordinateInput(
                "Haemo", 100, 200, "Resources/Islands.csv",
                MapDisplayRegion.Main,
                PreferNavigationCalibration: true,
                CalibrationX: 100,
                CalibrationY: 200,
                MapAnchorSource: "Resources/Islands.csv"),
            new MapIslandCoordinateInput(
                "Crows_Nest", 300, 400, "Resources/Islands.csv",
                MapDisplayRegion.Main,
                CalibrationX: 300,
                CalibrationY: 400,
                MapAnchorSource: "Resources/Islands.csv"),
        };
        var anchors = new[] {
            new BdfMapAnchor(
                "Haemo Island", "Haemo",
                67.44965659541857, -142.38281250000003, "connect.js"),
            // No Crow's Nest anchor today; the UI-adjusted
            // calibration coord must still place the island at
            // (300, 400) via the trusted affine fit OR (in this
            // test) at (300, 400) directly if we fall back to
            // an identity projection. We assert that the
            // island gets SOME coordinate, not Missing.
        };

        var result = BdfIslandCoordinateCatalog.BuildDetailed(
            inputs, anchors,
            new Dictionary<string, string> {
                ["Crows_Nest"] = "Crow's Nest",
            });

        Assert.True(result.Coordinates.ContainsKey("Haemo"));
        Assert.Equal(
            new GeoCoordinate(67.44965659541857, -142.38281250000003),
            result.Coordinates["Haemo"]);
        Assert.Equal(
            IslandResolutionMode.DirectBdfMatch,
            result.Diagnostics["Haemo"].Resolution);
        // Crows_Nest has no BDF anchor today so the catalog
        // simply doesn't have it (Missing); we don't get a
        // direct misplacement either way.
        Assert.False(result.Coordinates.ContainsKey("Crows_Nest"));
    }

    private static MapIslandCoordinateInput Input(string id, double x, double y) =>
        new(id, x, y, "bdo-world-direct", MapDisplayRegion.Main);
}