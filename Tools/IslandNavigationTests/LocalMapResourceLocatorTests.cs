using iBarter.Mapping;
using System.IO;
using Xunit;

namespace IslandNavigationTests;

public sealed class LocalMapResourceLocatorTests {
    [Fact]
    public void Runtime_resource_directory_wins_over_development_fallback() {
        string root = TempDirectory();
        try {
            string runtimeBase = Path.Combine(root, "app");
            string runtime = CreateMapRoot(Path.Combine(runtimeBase, "Resource", "MapTiles"));
            string development = CreateMapRoot(Path.Combine(root, "BDOMap", "Resource", "MapTiles"));

            string? resolved = LocalMapResourceLocator.Find(runtimeBase, [development]);

            Assert.Equal(runtime, resolved);
        }
        finally {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Missing_runtime_tiles_fall_back_to_development_cache() {
        string root = TempDirectory();
        try {
            string runtimeBase = Path.Combine(root, "app");
            Directory.CreateDirectory(Path.Combine(runtimeBase, "Resource", "MapTiles"));
            string development = CreateMapRoot(Path.Combine(root, "BDOMap", "Resource", "MapTiles"));

            string? resolved = LocalMapResourceLocator.Find(runtimeBase, [development]);

            Assert.Equal(development, resolved);
        }
        finally {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateMapRoot(string path) {
        Directory.CreateDirectory(Path.Combine(path, "base"));
        File.WriteAllText(Path.Combine(path, "manifest.json"), "{}");
        File.WriteAllText(Path.Combine(path, "regions.json"), "{}");
        File.WriteAllText(Path.Combine(path, "bdf-anchors.json"), "{}");
        return path;
    }

    private static string TempDirectory() {
        string path = Path.Combine(Path.GetTempPath(), "ibarter-maproot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
