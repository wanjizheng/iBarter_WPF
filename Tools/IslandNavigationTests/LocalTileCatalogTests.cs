using iBarter.Mapping;
using System.IO;
using Xunit;

namespace IslandNavigationTests;

public sealed class LocalTileCatalogTests {
    [Fact]
    public void Missing_tile_resolves_to_null_without_throwing() {
        string root = TempDirectory();
        try {
            var catalog = new LocalTileCatalog(root, ".jpg");

            Assert.Null(catalog.TryResolve(new TileAddress(3, 1, 2)));
        }
        finally {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Existing_tile_uses_the_canonical_z_x_y_layout() {
        string root = TempDirectory();
        try {
            string expected = Path.Combine(root, "base", "z3", "x1", "y2.jpg");
            Directory.CreateDirectory(Path.GetDirectoryName(expected)!);
            File.WriteAllBytes(expected, [1, 2, 3]);
            var catalog = new LocalTileCatalog(root, ".jpg");

            Assert.Equal(expected, catalog.TryResolve(new TileAddress(3, 1, 2)));
        }
        finally {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string TempDirectory() {
        string path = Path.Combine(Path.GetTempPath(), "ibarter-maptiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
