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
    public void Resources_map_tiles_is_the_preferred_runtime_location() {
        string root = TempDirectory();
        try {
            string runtimeBase = Path.Combine(root, "app");
            string expected = CreateMapRoot(Path.Combine(
                runtimeBase, "Resources", "MapTiles"));
            CreateMapRoot(Path.Combine(runtimeBase, "Resource", "MapTiles"));

            string? resolved = LocalMapResourceLocator.Find(runtimeBase);

            Assert.Equal(expected, resolved);
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

    [Fact]
    public void Development_candidates_find_sibling_bdomap_from_release_output_tree() {
        string root = TempDirectory();
        try {
            string applicationBase = Path.Combine(
                root, "iBarter", "bin", "x86", "Release", "win-x86");
            Directory.CreateDirectory(applicationBase);
            string expected = Path.Combine(root, "BDOMap", "Resource", "MapTiles");

            string[] candidates = LocalMapResourceLocator
                .EnumerateDevelopmentCandidates(applicationBase)
                .ToArray();

            Assert.Contains(expected, candidates, StringComparer.OrdinalIgnoreCase);
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
