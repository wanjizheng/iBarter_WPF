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

            RoutePlanPersistence.Save(
                path, plan, plan.Routes[0].Number,
                showAll: true, selectedBarterRowId: "r1");
            bool loaded = RoutePlanPersistence.TryLoad(path, plan.InputFingerprint, out var snapshot);

            Assert.True(loaded);
            Assert.NotNull(snapshot);
            Assert.Equal(plan.InputFingerprint, snapshot.Plan.InputFingerprint);
            Assert.Equal(plan.Objective, snapshot.Plan.Objective);
            Assert.True(RoutePlanVerifier.Verify(request, snapshot.Plan).Success);
            Assert.Equal(plan.Routes[0].Number, snapshot.SelectedRouteNumber);
            Assert.True(snapshot.ShowAll);
            Assert.Equal("r1", snapshot.SelectedBarterRowId);
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
    public void Saved_plan_restores_when_the_only_input_change_is_a_completed_barter() {
        string path = TempPath();
        try {
            var original = RouteTestData.TwoItemRequest(reverseDictionaryOrder: false);
            var plan = new AutomaticRoutePlanner().Plan(original, CancellationToken.None);
            Assert.True(plan.Routes.Count > 0);
            RoutePlanPersistence.Save(path, plan, plan.Routes[0].Number, showAll: false);

            var remaining = new AutomaticRoutePlanningRequest(
                original.Tasks.Where(task => task.RowId != "r1").ToArray(),
                original.Items,
                original.Warehouses,
                original.ExtraLT,
                original.TotalLT,
                original.Limits,
                original.ConfigurationVersion);

            Assert.False(RoutePlanPersistence.TryLoad(
                path, RoutePlanFingerprint.Compute(remaining), out _));
            Assert.True(RoutePlanPersistence.TryLoadAfterProgress(
                path,
                remaining,
                new HashSet<string>(["r1"], StringComparer.Ordinal),
                out var restored));
            Assert.Equal(plan.InputFingerprint, restored?.Plan.InputFingerprint);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Automatic_route_plan_uses_the_runtime_resources_directory() {
        string baseDirectory = Path.Combine(Path.GetTempPath(), "iBarter-runtime");

        string path = AutomaticRoutePlanStorage.BuildRuntimeResourcesPath(baseDirectory);

        Assert.Equal(
            Path.Combine(baseDirectory, "Resources", "automatic-route-plan.json"),
            path);
        Assert.Equal(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "automatic-route-plan.json"),
            AutomaticRoutePlanStorage.RuntimeResourcesPath);
    }

    private static string TempPath() => Path.Combine(
        Path.GetTempPath(), "iBarter-route-" + Guid.NewGuid().ToString("N") + ".json");
}
