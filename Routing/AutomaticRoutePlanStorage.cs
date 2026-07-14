namespace iBarter.Routing;

using System.IO;

public static class AutomaticRoutePlanStorage {
    public static string UserDataPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "iBarter",
        "automatic-route-plan.json");

    public static string LegacyOutputPath => Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "Resources",
        "automatic-route-plan.json");

    public static bool TryMigrate(string legacyPath, string currentPath) {
        try {
            if (File.Exists(currentPath) || !File.Exists(legacyPath)) return false;
            string? directory = Path.GetDirectoryName(currentPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.Copy(legacyPath, currentPath, overwrite: false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            return false;
        }
    }

    public static void MigrateLegacyOutputIfNeeded() => TryMigrate(LegacyOutputPath, UserDataPath);
}
