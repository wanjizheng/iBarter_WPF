using iBarter.Mapping;
using System.IO;
using Xunit;

namespace IslandNavigationTests;

public sealed class LocalMapConfigurationTests {
    [Fact]
    public void Verified_web_mercator_config_loads_main_and_right_inset_only() {
        string root = TempDirectory();
        try {
            File.WriteAllText(Path.Combine(root, "manifest.json"), """
                {
                  "projection": { "name": "WebMercator", "verified": true },
                  "tileScheme": "XYZ",
                  "tileSize": 256,
                  "tileFormat": "jpg",
                  "minZoom": 2,
                  "maxZoom": 7
                }
                """);
            File.WriteAllText(Path.Combine(root, "regions.json"), """
                {
                  "regions": [
                    {
                      "id": "main",
                      "worldLatRange": [-65, 79],
                      "worldLonRange": [-142, 150],
                      "defaultCenterLat": 0,
                      "defaultCenterLon": 0,
                      "defaultZoom": 3,
                      "zoomRange": [2, 7]
                    },
                    {
                      "id": "north-west-inset",
                      "defaultZoom": 3,
                      "zoomRange": [3, 6]
                    },
                    {
                      "id": "north-east-inset",
                      "worldLatRange": [-3, 41],
                      "worldLonRange": [63, 158],
                      "defaultCenterLat": 19,
                      "defaultCenterLon": 111,
                      "defaultZoom": 3,
                      "zoomRange": [3, 6],
                      "containingIslands": ["Hakoven"]
                    }
                  ]
                }
                """);

            Assert.True(LocalMapConfiguration.TryLoad(root, out var config));
            Assert.NotNull(config);
            Assert.Equal(256, config.TileSize);
            Assert.Equal(".jpg", config.TileExtension);
            Assert.Equal("main", config.MainRegion.Id);
            Assert.Equal("north-east-inset", config.RightInsetRegion.Id);
            Assert.Equal(["Hakoven"], config.RightInsetRegion.ContainingIslands);
        }
        finally {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Unverified_projection_is_rejected_for_static_map_fallback() {
        string root = TempDirectory();
        try {
            File.WriteAllText(Path.Combine(root, "manifest.json"), """
                {
                  "projection": { "name": "WebMercator", "verified": false },
                  "tileScheme": "XYZ",
                  "tileSize": 256,
                  "tileFormat": "jpg",
                  "minZoom": 2,
                  "maxZoom": 7
                }
                """);
            File.WriteAllText(Path.Combine(root, "regions.json"), """{"regions":[]}""");

            Assert.False(LocalMapConfiguration.TryLoad(root, out _));
        }
        finally {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string TempDirectory() {
        string path = Path.Combine(Path.GetTempPath(), "ibarter-mapconfig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
