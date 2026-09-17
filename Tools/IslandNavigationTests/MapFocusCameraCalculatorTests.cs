using iBarter.Mapping;
using Xunit;

namespace IslandNavigationTests;

public sealed class MapFocusCameraCalculatorTests {
    [Fact]
    public void Focus_camera_centers_the_selected_segment_and_fits_both_ends() {
        var from = new GeoCoordinate(0, -20);
        var to = new GeoCoordinate(10, 20);

        var focus = MapFocusCameraCalculator.Calculate(
            from, to, 1200, 700, 256, 2, 7);
        var fromPoint = MapViewportProjection.Project(
            from,
            new XyzViewportCamera(focus.Center, focus.Zoom, 2, 7),
            1200, 700, 256);
        var toPoint = MapViewportProjection.Project(
            to,
            new XyzViewportCamera(focus.Center, focus.Zoom, 2, 7),
            1200, 700, 256);

        Assert.Equal(600, (fromPoint.X + toPoint.X) / 2, 6);
        Assert.Equal(350, (fromPoint.Y + toPoint.Y) / 2, 6);
        Assert.InRange(fromPoint.X, 100, 1100);
        Assert.InRange(toPoint.X, 100, 1100);
        Assert.InRange(fromPoint.Y, 70, 630);
        Assert.InRange(toPoint.Y, 70, 630);
    }
}
