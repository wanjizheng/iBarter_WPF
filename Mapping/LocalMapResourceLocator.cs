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

    public static bool IsUsable(string root) =>
        Directory.Exists(Path.Combine(root, "base"))
        && File.Exists(Path.Combine(root, "manifest.json"))
        && File.Exists(Path.Combine(root, "regions.json"))
        && File.Exists(Path.Combine(root, "bdf-anchors.json"));
}
