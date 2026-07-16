using iBarter.Mapping;
using Xunit;

namespace IslandNavigationTests;

public sealed class VisibleTileEnumeratorTests {
    [Fact]
    public void Centered_512_pixel_view_enumerates_only_four_visible_tiles() {
        var camera = new XyzViewportCamera(
            center: new NormalizedMercatorPoint(0.5, 0.5),
            zoom: 3,
            minZoom: 2,
            maxZoom: 7);

        var layout = VisibleTileEnumerator.Enumerate(
            camera, viewportWidth: 512, viewportHeight: 512, tileSize: 256);

        Assert.Equal(3, layout.TileZoom);
        Assert.Equal(
            [
                new TileAddress(3, 3, 3),
                new TileAddress(3, 4, 3),
                new TileAddress(3, 3, 4),
                new TileAddress(3, 4, 4),
            ],
            layout.Tiles.Select(tile => tile.Address).ToArray());
    }

    [Fact]
    public void Tile_enumeration_clamps_to_the_xyz_world_bounds() {
        var camera = new XyzViewportCamera(
            center: new NormalizedMercatorPoint(0, 0),
            zoom: 2,
            minZoom: 2,
            maxZoom: 7);

        var layout = VisibleTileEnumerator.Enumerate(
            camera, viewportWidth: 1024, viewportHeight: 1024, tileSize: 256);

        Assert.All(layout.Tiles, tile => {
            Assert.InRange(tile.Address.X, 0, 3);
            Assert.InRange(tile.Address.Y, 0, 3);
        });
    }
}
