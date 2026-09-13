using iBarter.Mapping;
using Xunit;

namespace IslandNavigationTests;

public sealed class MapViewportProjectionTests {
    [Fact]
    public void Camera_center_projects_to_viewport_center() {
        var camera = new XyzViewportCamera(
            WebMercatorProjection.ToNormalized(new GeoCoordinate(0, 0)),
            3,
            2,
            7);

        var point = MapViewportProjection.Project(
            new GeoCoordinate(0, 0), camera, 800, 600, 256);

        Assert.Equal(new ViewportPoint(400, 300), point);
    }

    [Fact]
    public void Eastward_longitude_projects_to_the_right() {
        var camera = new XyzViewportCamera(
            WebMercatorProjection.ToNormalized(new GeoCoordinate(0, 0)),
            3,
            2,
            7);

        var point = MapViewportProjection.Project(
            new GeoCoordinate(0, 10), camera, 800, 600, 256);

        Assert.True(point.X > 400);
        Assert.Equal(300, point.Y, 8);
    }
}
