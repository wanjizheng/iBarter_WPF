using iBarter.Mapping;
using Xunit;

namespace IslandNavigationTests;

public sealed class WebMercatorProjectionTests {
    [Fact]
    public void Zero_latitude_and_longitude_project_to_world_center() {
        var normalized = WebMercatorProjection.ToNormalized(new GeoCoordinate(0, 0));
        var pixels = WebMercatorProjection.ToWorldPixels(new GeoCoordinate(0, 0), 2, 256);

        Assert.Equal(0.5, normalized.X, 12);
        Assert.Equal(0.5, normalized.Y, 12);
        Assert.Equal(new WorldPixelPoint(512, 512), pixels);
    }

    [Fact]
    public void Latitude_is_clamped_to_the_web_mercator_limit() {
        var north = WebMercatorProjection.ToNormalized(new GeoCoordinate(90, 0));
        var south = WebMercatorProjection.ToNormalized(new GeoCoordinate(-90, 0));

        Assert.InRange(north.Y, 0, 0.0000001);
        Assert.InRange(south.Y, 0.9999999, 1);
    }
}
