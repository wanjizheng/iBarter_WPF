namespace iBarter.Routing;

using System.IO;

public static class AutomaticRoutePlanStorage {
    public static string RuntimeResourcesPath =>
        BuildRuntimeResourcesPath(AppDomain.CurrentDomain.BaseDirectory);

    public static string BuildRuntimeResourcesPath(string baseDirectory) => Path.Combine(
        baseDirectory,
        "Resources",
        "automatic-route-plan.json");
}
