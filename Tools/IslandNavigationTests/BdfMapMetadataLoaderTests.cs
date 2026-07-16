using iBarter.Mapping;
using System.IO;
using Xunit;

namespace IslandNavigationTests;

public sealed class BdfMapMetadataLoaderTests {
    [Fact]
    public void Metadata_loader_reads_anchors_and_merges_runtime_aliases() {
        string root = Path.Combine(
            Path.GetTempPath(), "ibarter-metadata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            File.WriteAllText(Path.Combine(root, "bdf-anchors.json"), """
                {
                  "anchors": [
                    {
                      "sourceName": "Iliyia Island",
                      "lat": 13,
                      "lon": 27,
                      "sourceUrl": "village.js"
                    }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(root, "ibarter-bdf-aliases.json"), """
                {
                  "aliases": [
                    { "iBarter": "Custom", "bdfSourceName": "Custom Island" }
                  ]
                }
                """);

            Assert.True(BdfMapMetadataLoader.TryLoad(root, out var metadata));
            Assert.NotNull(metadata);
            Assert.Single(metadata.Anchors);
            Assert.Equal("Iliyia Island", metadata.Aliases["Iliya"]);
            Assert.Equal("Custom Island", metadata.Aliases["Custom"]);
        }
        finally {
            Directory.Delete(root, recursive: true);
        }
    }
}
