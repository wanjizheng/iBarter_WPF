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
    public void Progress_restore_rejects_a_stale_route_that_skips_the_required_warehouse_pickup() {
        var load = new RouteLoadSnapshot(0, 0, 0);
        var stale = new RoutePlan(
            RoutePlanStatus.BestKnownWithinLimit,
            [
                new PlannedRoute(1, "Iliya", "Iliya", [
                    new BarterStep("old", "OldIsland", new("OLD-IN", 1), new("OLD-OUT", 1), load),
                    new WarehouseUnloadStep("Iliya", "Iliya", [new("OLD-OUT", 1)], load),
                ], 1, 0, 0, 0),
                new PlannedRoute(2, "Iliya", "Iliya", [
                    new BarterStep("necklace", "Iliya", new("CACTUS", 5), new("NECKLACE", 5), load),
                    new WarehouseUnloadStep("Iliya", "Iliya", [new("NECKLACE", 5)], load),
                ], 0, 0, 0, 0),
            ],
            new RoutePlanObjective(2, 1, 0, 0, ""), [], "old-fingerprint");
        var request = new AutomaticRoutePlanningRequest(
            [new("necklace", "Iliya", new RoutePoint(10, 0), "CACTUS", 5, "NECKLACE", 5)],
            new Dictionary<string, RouteItem> {
                ["CACTUS"] = new("CACTUS", "Golden Cactus", 5, 1_000),
                ["NECKLACE"] = new("NECKLACE", "Seashell Necklace", 7, 1_000),
            },
            [
                new RouteWarehouse("Velia", "Velia", new RoutePoint(0, 0),
                    new Dictionary<string, int> { ["CACTUS"] = 5 }),
                new RouteWarehouse("Iliya", "Iliya", new RoutePoint(10, 0),
                    new Dictionary<string, int>()),
            ],
            0, 30_000, new RouteSearchLimits(5_000, 100), "current");

        Assert.False(RoutePlanRestoreCompatibility.IsCompatibleAfterProgress(
            request, stale, new HashSet<string>(["old"], StringComparer.Ordinal)));
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
