using iBarter.Mapping;
using Xunit;

namespace IslandNavigationTests;

public sealed class XyzViewportCameraBoundsTests {
    private static readonly MapGeoBounds MapBounds = new(
        South: -60,
        North: 60,
        West: -90,
        East: 90);

    [Fact]
    public void Constrain_to_map_bounds_raises_zoom_when_viewport_would_be_wider_than_map() {
        var camera = new XyzViewportCamera(
            new NormalizedMercatorPoint(0.5, 0.5), 2, 2, 7);

        camera.ConstrainTo(MapBounds, viewportWidth: 800, viewportHeight: 600, tileSize: 256);

        Assert.True(camera.Zoom > 2);
        AssertViewportIsWithinBounds(camera, viewportWidth: 800, viewportHeight: 600);
    }

    [Fact]
    public void Constrain_to_map_bounds_prevents_dragging_past_left_or_top_edge() {
        var camera = new XyzViewportCamera(
            new NormalizedMercatorPoint(0.5, 0.5), 4, 2, 7);

        camera.PanByScreen(deltaX: 1_000_000, deltaY: 1_000_000, tileSize: 256);
        camera.ConstrainTo(MapBounds, viewportWidth: 800, viewportHeight: 600, tileSize: 256);

        AssertViewportIsWithinBounds(camera, viewportWidth: 800, viewportHeight: 600);
    }

    [Fact]
    public void Constrain_to_map_bounds_reacts_to_a_larger_viewport_after_resize() {
        var camera = new XyzViewportCamera(
            new NormalizedMercatorPoint(0.5, 0.5), 2, 2, 7);
        camera.ConstrainTo(MapBounds, viewportWidth: 500, viewportHeight: 400, tileSize: 256);
        double zoomBeforeResize = camera.Zoom;

        camera.ConstrainTo(MapBounds, viewportWidth: 1100, viewportHeight: 800, tileSize: 256);

        Assert.True(camera.Zoom > zoomBeforeResize);
        AssertViewportIsWithinBounds(camera, viewportWidth: 1100, viewportHeight: 800);
    }

    private static void AssertViewportIsWithinBounds(
        XyzViewportCamera camera,
        double viewportWidth,
        double viewportHeight) {
        var topLeft = WebMercatorProjection.ToNormalized(
            new GeoCoordinate(MapBounds.North, MapBounds.West));
        var bottomRight = WebMercatorProjection.ToNormalized(
            new GeoCoordinate(MapBounds.South, MapBounds.East));
        double worldSize = WebMercatorProjection.WorldSize(camera.Zoom, 256);
        double halfWidth = viewportWidth / (2 * worldSize);
        double halfHeight = viewportHeight / (2 * worldSize);

        Assert.True(camera.Center.X - halfWidth >= topLeft.X - 0.000001);
        Assert.True(camera.Center.X + halfWidth <= bottomRight.X + 0.000001);
        Assert.True(camera.Center.Y - halfHeight >= topLeft.Y - 0.000001);
        Assert.True(camera.Center.Y + halfHeight <= bottomRight.Y + 0.000001);
    }
}
