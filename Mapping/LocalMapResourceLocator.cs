namespace iBarter.Mapping;

using System.IO;

public static class LocalMapResourceLocator {
    public const string DisableEnvironmentVariable = "IBARTER_DISABLE_HD_MAP";

    public static bool IsDisabled() =>
        string.Equals(
            Environment.GetEnvironmentVariable(DisableEnvironmentVariable),
            "1",
            StringComparison.OrdinalIgnoreCase)
        || string.Equals(
            Environment.GetEnvironmentVariable(DisableEnvironmentVariable),
            "true",
            StringComparison.OrdinalIgnoreCase);

    public static string? Find(
        string applicationBaseDirectory,
        IEnumerable<string>? developmentCandidates = null) {
        if (IsDisabled()) return null;

        string runtime = Path.Combine(
            Path.GetFullPath(applicationBaseDirectory), "Resource", "MapTiles");
        if (IsUsable(runtime)) return runtime;

        foreach (string candidate in developmentCandidates ?? []) {
            string fullPath = Path.GetFullPath(candidate);
            if (IsUsable(fullPath)) return fullPath;
        }
        return null;
    }

    /// <summary>
    /// Finds a sibling BDOMap cache only while running from a source/build tree.
    /// This deliberately derives candidates from the executable's location rather
    /// than baking a developer-specific drive or user profile into release code.
    /// A normally installed application has no such sibling directory, so it
    /// still relies exclusively on Resource\MapTiles next to the executable.
    /// </summary>
    public static IEnumerable<string> EnumerateDevelopmentCandidates(
        string applicationBaseDirectory) {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DirectoryInfo? directory = new(Path.GetFullPath(applicationBaseDirectory));
        while (directory is not null) {
            string sibling = Path.Combine(
                directory.FullName, "BDOMap", "Resource", "MapTiles");
            if (seen.Add(sibling)) yield return sibling;
            directory = directory.Parent;
        }
    }

    public static bool IsUsable(string root) =>
        Directory.Exists(Path.Combine(root, "base"))
        && File.Exists(Path.Combine(root, "manifest.json"))
        && File.Exists(Path.Combine(root, "regions.json"))
        && File.Exists(Path.Combine(root, "bdf-anchors.json"));
}
