namespace iBarter.Mapping;

using System.IO;
using System.Text.Json;

public sealed record BdfMapMetadata(
    IReadOnlyList<BdfMapAnchor> Anchors,
    IReadOnlyDictionary<string, string> Aliases);

public static class BdfMapMetadataLoader {
    private static readonly IReadOnlyDictionary<string, string> BuiltInAliases =
        new Dictionary<string, string>(StringComparer.Ordinal) {
            ["Iliya"] = "Iliyia Island",
            ["Kuit"] = "Kuit Islands",
            ["Sausan"] = "Sausan Garrison Wharf",
            ["Midnight"] = "Starry Midnight Port",
            // The barter catalog enum is historically named "Olvia", but its
            // localized display name and exchange point are Olvia Coast. Without
            // this explicit alias, normalized matching selects Olvia village.
            ["Olvia"] = "Olvia Coast",
            // Forward-declared: BDF has no "Crow's Nest" anchor today;
            // the alias keeps the iBarter → BDF mapping explicit so a
            // future source added to bdf-anchors.json is matched
            // without re-deriving the lookup.
            ["Crows_Nest"] = "Crow's Nest",
        };

    public static bool TryLoad(string root, out BdfMapMetadata? metadata) {
        metadata = null;
        try {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var anchorsDto = JsonSerializer.Deserialize<AnchorsDto>(
                File.ReadAllText(Path.Combine(root, "bdf-anchors.json")), options);
            if (anchorsDto?.Anchors is null) return false;

            var anchors = anchorsDto.Anchors
                .Where(anchor => !string.IsNullOrWhiteSpace(anchor.SourceName)
                    && double.IsFinite(anchor.Lat)
                    && double.IsFinite(anchor.Lon))
                .Select(anchor => new BdfMapAnchor(
                    anchor.SourceName!,
                    anchor.IBarterIslandName,
                    anchor.Lat,
                    anchor.Lon,
                    anchor.SourceUrl ?? string.Empty))
                .ToArray();
            var aliases = new Dictionary<string, string>(
                BuiltInAliases, StringComparer.Ordinal);
            string aliasPath = Path.Combine(root, "ibarter-bdf-aliases.json");
            if (File.Exists(aliasPath)) {
                var aliasesDto = JsonSerializer.Deserialize<AliasesDto>(
                    File.ReadAllText(aliasPath), options);
                foreach (var alias in aliasesDto?.Aliases ?? []) {
                    if (!string.IsNullOrWhiteSpace(alias.IBarter)
                        && !string.IsNullOrWhiteSpace(alias.BdfSourceName))
                        aliases[alias.IBarter!] = alias.BdfSourceName!;
                }
            }
            metadata = new BdfMapMetadata(anchors, aliases);
            return true;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException) {
            return false;
        }
    }

    private sealed class AnchorsDto {
        public AnchorDto[]? Anchors { get; set; }
    }

    private sealed class AnchorDto {
        public string? SourceName { get; set; }
        public string? IBarterIslandName { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
        public string? SourceUrl { get; set; }
    }

    private sealed class AliasesDto {
        public AliasDto[]? Aliases { get; set; }
    }

    private sealed class AliasDto {
        public string? IBarter { get; set; }
        public string? BdfSourceName { get; set; }
    }
}
