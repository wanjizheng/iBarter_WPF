using System.Globalization;
using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace iBarter.Routing;

public static class CargoWeightTable {
    private const string CatalogResource = "iBarter.Items.csv";
    private static readonly Lazy<IReadOnlyDictionary<string, double>> Weights = new(Load);

    public static double GetWeight(string itemId, int level) =>
        Weights.Value.TryGetValue(itemId, out double weight) ? weight : LegacyWeight(level);

    // This compatibility copy is built from the same CSV, not a second weight list.
    private static IReadOnlyDictionary<string, double> Load() {
        using var bundled = typeof(CargoWeightTable).Assembly.GetManifestResourceStream(CatalogResource)
            ?? throw new InvalidDataException("Missing bundled Items.csv.");
        using var reader = new StreamReader(bundled);
        var defaults = ReadWeights(reader);
        string path = Path.Combine(AppContext.BaseDirectory, "Resources", "Items.csv");
        if (!File.Exists(path)) return defaults;
        using var runtime = File.OpenText(path);
        return ReadWeights(runtime, defaults);
    }

    public static IReadOnlyDictionary<string, double> ReadWeights(TextReader reader,
        IReadOnlyDictionary<string, double>? legacyDefaults = null) {
        var weights = new Dictionary<string, double>(StringComparer.Ordinal);
        using var parser = new TextFieldParser(reader) { HasFieldsEnclosedInQuotes = true };
        parser.SetDelimiters(",");
        while (!parser.EndOfData) {
            var fields = parser.ReadFields();
            if (fields is null || fields.Length < 4) continue;
            string id = fields[1].Trim();
            if (!int.TryParse(fields[2], out int level)) continue;
            double weight;
            if (fields.Length < 5 || string.IsNullOrWhiteSpace(fields[4])) {
                weight = legacyDefaults?.GetValueOrDefault(id, LegacyWeight(level)) ?? LegacyWeight(level);
            }
            else if (!double.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture, out weight)
                || !double.IsFinite(weight) || weight < 0 || Math.Round(weight, 2) != weight) {
                throw new InvalidDataException($"Items.csv: invalid WeightLT for item {id}: '{fields[4]}'. Use a non-negative number with at most two decimal places.");
            }
            weights[id] = weight;
        }
        return weights;
    }

    private static double LegacyWeight(int level) => level == 0 ? 0.1 : GetWeightForLevel(level);

    public static int GetWeightForLevel(int level) => level switch {
        1 => 100,
        2 => 400,
        3 => 900,
        4 or 5 => 1000,
        6 or 7 => 2000,
        _ => 0,
    };
}
