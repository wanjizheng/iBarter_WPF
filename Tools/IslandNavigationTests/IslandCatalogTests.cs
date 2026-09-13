using System.Globalization;
using System.IO;
using Xunit;

namespace IslandNavigationTests;

public sealed class IslandCatalogTests {
    [Fact]
    public void Every_island_has_finite_navigation_coordinates_and_source() {
        var rows = LoadRows();
        Assert.NotEmpty(rows);
        foreach (var row in rows) {
            Assert.Equal(9, row.Length);
            Assert.True(double.TryParse(row[6], NumberStyles.Float, CultureInfo.InvariantCulture, out double x));
            Assert.True(double.TryParse(row[7], NumberStyles.Float, CultureInfo.InvariantCulture, out double y));
            Assert.True(double.IsFinite(x));
            Assert.True(double.IsFinite(y));
            Assert.False(string.IsNullOrWhiteSpace(row[8]));
        }
    }

    [Fact]
    public void Authoritative_remote_coordinates_match_calibrated_sources() {
        var rows = LoadRows().ToDictionary(row => row[0], StringComparer.Ordinal);
        AssertCoordinate(rows, "Midnight", -321_664, -598_912, 1);
        AssertCoordinate(rows, "Rickun", -816_036, 669_629, 2);
        AssertCoordinate(rows, "Cox_Pirate", -747_393, 504_292, 2);
        AssertCoordinate(rows, "Halmad", 558_999, 333_684, 1);
        AssertCoordinate(rows, "Hakoven", 1_252_450, 547_567, 1);
    }

    [Fact]
    public void Barter_location_catalog_uses_unique_finite_npc_destinations() {
        string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "Resources",
            "IslandBarterLocations.csv"));
        var rows = File.ReadAllLines(path)
            .Skip(1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split(','))
            .ToArray();

        Assert.Equal(68, rows.Length);
        Assert.Equal(rows.Length, rows.Select(row => row[0]).Distinct().Count());
        Assert.Equal(rows.Length, rows.Select(row => row[3]).Distinct().Count());
        foreach (string[] row in rows) {
            Assert.Equal(6, row.Length);
            Assert.True(double.TryParse(
                row[1], NumberStyles.Float, CultureInfo.InvariantCulture,
                out double x));
            Assert.True(double.TryParse(
                row[2], NumberStyles.Float, CultureInfo.InvariantCulture,
                out double y));
            Assert.True(double.IsFinite(x));
            Assert.True(double.IsFinite(y));
            Assert.True(int.TryParse(row[3], out int npcId) && npcId > 0);
            Assert.Equal($"https://bdocodex.com/us/npc/{npcId}/", row[5]);
        }
    }

    [Fact]
    public void Padix_destination_is_keshao_on_the_north_coast() {
        string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "Resources",
            "IslandBarterLocations.csv"));
        string[] row = File.ReadAllLines(path)
            .Skip(1)
            .Select(line => line.Split(','))
            .Single(row => row[0] == "Padix");

        Assert.Equal("-349081", row[1]);
        Assert.Equal("347470", row[2]);
        Assert.Equal("58915", row[3]);
        Assert.Equal("Keshao", row[4]);

        var islandRows = LoadRows().ToDictionary(entry => entry[0], StringComparer.Ordinal);
        double nodeY = double.Parse(islandRows["Padix"][7], CultureInfo.InvariantCulture);
        double barterY = double.Parse(row[2], CultureInfo.InvariantCulture);
        Assert.True(barterY > nodeY + 50_000);
    }

    internal static List<string[]> LoadRows() {
        string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "Resources", "Islands.csv"));
        return File.ReadAllLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split(','))
            .ToList();
    }

    private static void AssertCoordinate(
        IReadOnlyDictionary<string, string[]> rows,
        string name,
        double expectedX,
        double expectedY,
        double tolerance) {
        Assert.True(rows.TryGetValue(name, out string[]? row));
        double actualX = double.Parse(row![6], CultureInfo.InvariantCulture);
        double actualY = double.Parse(row[7], CultureInfo.InvariantCulture);
        Assert.InRange(actualX, expectedX - tolerance, expectedX + tolerance);
        Assert.InRange(actualY, expectedY - tolerance, expectedY + tolerance);
    }
}
