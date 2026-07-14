using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public sealed class RoutePlanPersistenceTests {
    [Fact]
    public void Saved_plan_and_route_selection_round_trip() {
        string path = TempPath();
        try {
            var request = RouteTestData.SingleTask();
            var plan = new AutomaticRoutePlanner().Plan(request, CancellationToken.None);
            Assert.True(plan.Routes.Count > 0);

            RoutePlanPersistence.Save(path, plan, plan.Routes[0].Number, showAll: true);
            bool loaded = RoutePlanPersistence.TryLoad(path, plan.InputFingerprint, out var snapshot);

            Assert.True(loaded);
            Assert.NotNull(snapshot);
            Assert.Equal(plan.InputFingerprint, snapshot.Plan.InputFingerprint);
            Assert.Equal(plan.Objective, snapshot.Plan.Objective);
            Assert.True(RoutePlanVerifier.Verify(request, snapshot.Plan).Success);
            Assert.Equal(plan.Routes[0].Number, snapshot.SelectedRouteNumber);
            Assert.True(snapshot.ShowAll);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Wrong_fingerprint_or_corrupt_json_is_ignored() {
        string path = TempPath();
        try {
            var request = RouteTestData.SingleTask();
            var plan = new AutomaticRoutePlanner().Plan(request, CancellationToken.None);
            RoutePlanPersistence.Save(path, plan, 1, false);

            Assert.False(RoutePlanPersistence.TryLoad(path, "different", out _));

            File.WriteAllText(path, "{ definitely not json");
            Assert.False(RoutePlanPersistence.TryLoad(path, plan.InputFingerprint, out _));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Legacy_output_plan_is_migrated_once_to_the_user_data_location() {
        string root = Path.Combine(Path.GetTempPath(), "iBarter-route-migration-" + Guid.NewGuid().ToString("N"));
        string legacy = Path.Combine(root, "app", "Resources", "automatic-route-plan.json");
        string current = Path.Combine(root, "user", "automatic-route-plan.json");
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
            File.WriteAllText(legacy, "legacy-plan");

            Assert.True(AutomaticRoutePlanStorage.TryMigrate(legacy, current));
            Assert.Equal("legacy-plan", File.ReadAllText(current));

            File.WriteAllText(legacy, "newer-legacy-plan");
            Assert.False(AutomaticRoutePlanStorage.TryMigrate(legacy, current));
            Assert.Equal("legacy-plan", File.ReadAllText(current));
        }
        finally {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string TempPath() => Path.Combine(
        Path.GetTempPath(), "iBarter-route-" + Guid.NewGuid().ToString("N") + ".json");
}
