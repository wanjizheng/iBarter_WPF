using iBarter.Routing;
using System.Text.Json.Nodes;
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
                showAll: true, selectedBarterRowId: "r1",
                showRouteGuides: false,
                completedTaskRowIds: new HashSet<string>(
                    [RouteTaskIdentity.CreateSegmentId("br-parent", 0, 2)],
                    StringComparer.Ordinal));
            var result = RoutePlanPersistence.TryLoad(path, plan.InputFingerprint);

            Assert.Equal(RoutePlanLoadStatus.Loaded, result.Status);
            Assert.NotNull(result.Snapshot);
            Assert.Equal(plan.InputFingerprint, result.Snapshot!.Plan.InputFingerprint);
            Assert.Equal(plan.Objective, result.Snapshot.Plan.Objective);
            Assert.True(RoutePlanVerifier.Verify(request, result.Snapshot.Plan).Success);
            Assert.Equal(plan.Routes[0].Number, result.Snapshot.SelectedRouteNumber);
            Assert.True(result.Snapshot.ShowAll);
            Assert.Equal("r1", result.Snapshot.SelectedBarterRowId);
            Assert.False(result.Snapshot.ShowRouteGuides);
            Assert.Single(result.Snapshot.CompletedTaskRowIds!);
            Assert.Equal(
                RouteTaskIdentity.CreateSegmentId("br-parent", 0, 2),
                result.Snapshot.CompletedTaskRowIds![0]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Legacy_plan_without_route_guide_setting_defaults_to_visible() {
        string path = TempPath();
        try {
            var request = RouteTestData.SingleTask();
            var plan = new AutomaticRoutePlanner().Plan(request, CancellationToken.None);
            RoutePlanPersistence.Save(
                path, plan, plan.Routes[0].Number,
                showAll: false, showRouteGuides: false);

            var legacyEnvelope = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            Assert.True(legacyEnvelope.Remove("ShowRouteGuides"));
            File.WriteAllText(path, legacyEnvelope.ToJsonString());

            var result = RoutePlanPersistence.TryLoad(path, plan.InputFingerprint);

            Assert.Equal(RoutePlanLoadStatus.Loaded, result.Status);
            Assert.True(result.Snapshot!.ShowRouteGuides);
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

            var mismatchResult = RoutePlanPersistence.TryLoad(path, "different");
            Assert.Equal(RoutePlanLoadStatus.FingerprintMismatch, mismatchResult.Status);
            Assert.Null(mismatchResult.Snapshot);

            File.WriteAllText(path, "{ definitely not json");
            var corruptResult = RoutePlanPersistence.TryLoad(path, plan.InputFingerprint);
            Assert.Equal(RoutePlanLoadStatus.CorruptFile, corruptResult.Status);
            Assert.Null(corruptResult.Snapshot);
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
            RoutePlanPersistence.Save(
                path, plan, plan.Routes[0].Number,
                showAll: false, showRouteGuides: false);

            var remaining = new AutomaticRoutePlanningRequest(
                original.Tasks.Where(task => task.RowId != "r1").ToArray(),
                original.Items,
                original.Warehouses,
                original.ExtraLT,
                original.TotalLT,
                original.Limits,
                original.ConfigurationVersion);

            var directResult = RoutePlanPersistence.TryLoad(
                path, RoutePlanFingerprint.Compute(remaining));
            Assert.Equal(RoutePlanLoadStatus.FingerprintMismatch, directResult.Status);

            bool restored = RoutePlanPersistence.TryLoadAfterProgress(
                path,
                remaining,
                new HashSet<string>(["r1"], StringComparer.Ordinal),
                out var snapshot);
            Assert.True(restored);
            Assert.Equal(plan.InputFingerprint, snapshot?.Plan.InputFingerprint);
            Assert.False(snapshot?.ShowRouteGuides);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Saved_partial_segment_progress_restores_from_reduced_planner_quantity() {
        string path = TempPath();
        try {
            string plannerRowId = "br-restore-partial";
            string first = RouteTaskIdentity.CreateSegmentId(plannerRowId, 0, 2);
            string second = RouteTaskIdentity.CreateSegmentId(plannerRowId, 1, 2);
            var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
                ["IN"] = new("IN", "Input", 2, 400),
                ["OUT"] = new("OUT", "Output", 3, 900),
            };
            var warehouses = new[] {
                new RouteWarehouse("Iliya", "Iliya", new RoutePoint(0, 0),
                    new Dictionary<string, int>(StringComparer.Ordinal) { ["IN"] = 10 }),
            };
            var original = new AutomaticRoutePlanningRequest(
                [
                    new(first, "Ostra", new RoutePoint(10, 0), "IN", 2, "OUT", 6),
                    new(second, "Ostra", new RoutePoint(10, 0), "IN", 8, "OUT", 24),
                ],
                items, warehouses, 0, 24_000,
                new RouteSearchLimits(10_000, 100), "partial-restore");
            var zero = new RouteLoadSnapshot(0, 0, 0);
            var skeleton = new RoutePlan(
                RoutePlanStatus.Optimal,
                [
                    new PlannedRoute(1, "Iliya", "Iliya", [
                        new WarehousePickupStep("Iliya", "Iliya", [new("IN", 2)], zero),
                        new BarterStep(first, "Ostra", new("IN", 2), new("OUT", 6), zero),
                        new WarehouseUnloadStep("Iliya", "Iliya", [new("OUT", 6)], zero),
                    ], 0, 0, 0, 0),
                    new PlannedRoute(2, "Iliya", "Iliya", [
                        new WarehousePickupStep("Iliya", "Iliya", [new("IN", 8)], zero),
                        new BarterStep(second, "Ostra", new("IN", 8), new("OUT", 24), zero),
                        new WarehouseUnloadStep("Iliya", "Iliya", [new("OUT", 24)], zero),
                    ], 0, 0, 0, 0),
                ],
                null, [], RoutePlanFingerprint.Compute(original));
            var replayed = RouteReplay.ReplayPlan(original, skeleton);
            Assert.True(replayed.Success, replayed.Failure?.FailureDetail);
            RoutePlanPersistence.Save(
                path, replayed.Plan!, 2, false,
                completedTaskRowIds: new HashSet<string>([first], StringComparer.Ordinal));

            var remaining = new AutomaticRoutePlanningRequest(
                [new(plannerRowId, "Ostra", new RoutePoint(10, 0),
                    "IN", 8, "OUT", 24)],
                items, warehouses, 0, 24_000,
                original.Limits, original.ConfigurationVersion);

            Assert.True(RoutePlanPersistence.TryLoadAfterProgress(
                path,
                remaining,
                new HashSet<string>(StringComparer.Ordinal),
                out var restored));
            Assert.Equal(first, Assert.Single(restored!.CompletedTaskRowIds!));
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
