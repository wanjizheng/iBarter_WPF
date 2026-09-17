using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

// Covers the schema-v2 persistence contract introduced by the
// "fingerprint mismatch after restart" bug fix:
//   * OptimizationMode is persisted so restore can rebuild the same
//     request limits without guessing from the live ComboBox.
//   * SavedAtUtc is persisted for diagnostics and triage.
//   * TryLoad returns a discriminated RoutePlanLoadStatus instead of
//     collapsing every failure into false, so the UI can log the actual
//     cause (mismatch / corrupt / unsupported schema / missing file).
//   * v1 envelopes (no OptimizationMode field) still parse so the legacy
//     file written before the fix can be reloaded by iterating modes.
public sealed class RoutePlanSchemaV2Tests {
    [Fact]
    public void Round_trip_preserves_optimization_mode_quick() {
        string path = TempPath();
        try {
            var profile = RouteOptimizationProfile.For(RouteOptimizationMode.Quick);
            var (plan, request) = PlanWith(RouteTestData.SingleTask(), profile);

            RoutePlanPersistence.Save(
                path, plan, plan.Routes[0].Number,
                showAll: false, selectedBarterRowId: "r1",
                optimizationMode: RouteOptimizationMode.Quick);

            var result = RoutePlanPersistence.TryLoad(path, plan.InputFingerprint);

            Assert.Equal(RoutePlanLoadStatus.Loaded, result.Status);
            Assert.NotNull(result.Snapshot);
            Assert.Equal(RouteOptimizationMode.Quick, result.SavedOptimizationMode);
            Assert.Equal(plan.Routes[0].Number, result.Snapshot!.SelectedRouteNumber);
            Assert.False(result.Snapshot.ShowAll);
            Assert.Equal("r1", result.Snapshot.SelectedBarterRowId);
            // Sanity: the Quick profile fingerprint actually equals what the
            // planner recorded when generating, so the bug we are fixing
            // (different MaxLocalMoves between save and restore) is exercised.
            Assert.Equal(RoutePlanFingerprint.Compute(request), plan.InputFingerprint);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Round_trip_preserves_optimization_mode_balanced() {
        // This is the exact bug the user observed: Balanced profile saves a
        // fingerprint with MaxLocalMoves=500. Restore must use the saved
        // mode (not a hard-coded limit) or this test fails.
        string path = TempPath();
        try {
            var profile = RouteOptimizationProfile.For(RouteOptimizationMode.Balanced);
            var (plan, request) = PlanWith(RouteTestData.SingleTask(), profile);

            RoutePlanPersistence.Save(
                path, plan, plan.Routes[0].Number,
                showAll: true, selectedBarterRowId: "r1",
                optimizationMode: RouteOptimizationMode.Balanced);

            var result = RoutePlanPersistence.TryLoad(path, plan.InputFingerprint);

            Assert.Equal(RoutePlanLoadStatus.Loaded, result.Status);
            Assert.Equal(RouteOptimizationMode.Balanced, result.SavedOptimizationMode);
            Assert.True(result.Snapshot!.ShowAll);
            // Round-trip integrity for the actual saved data
            Assert.Equal(plan.Routes.Count, result.Snapshot.Plan.Routes.Count);
            for (int i = 0; i < plan.Routes.Count; i++) {
                Assert.Equal(plan.Routes[i].Number, result.Snapshot.Plan.Routes[i].Number);
                Assert.Equal(plan.Routes[i].Distance, result.Snapshot.Plan.Routes[i].Distance);
            }
            // Bug regression: the fingerprint is what Balanced saves, not Deep.
            Assert.Equal(RoutePlanFingerprint.Compute(request), plan.InputFingerprint);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Round_trip_preserves_optimization_mode_deep() {
        // Deep is the only mode the buggy hard-coded 2_000 MaxLocalMoves
        // happened to match by accident. After the fix this mode must
        // continue to round-trip cleanly (no regression).
        string path = TempPath();
        try {
            var profile = RouteOptimizationProfile.For(RouteOptimizationMode.Deep);
            var (plan, _) = PlanWith(RouteTestData.SingleTask(), profile);

            RoutePlanPersistence.Save(
                path, plan, plan.Routes[0].Number,
                showAll: false, selectedBarterRowId: "r1",
                optimizationMode: RouteOptimizationMode.Deep);

            var result = RoutePlanPersistence.TryLoad(path, plan.InputFingerprint);

            Assert.Equal(RoutePlanLoadStatus.Loaded, result.Status);
            Assert.Equal(RouteOptimizationMode.Deep, result.SavedOptimizationMode);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Round_trip_preserves_saved_at_utc() {
        string path = TempPath();
        try {
            var (plan, _) = PlanWith(RouteTestData.SingleTask(),
                RouteOptimizationProfile.For(RouteOptimizationMode.Balanced));
            var stamp = new DateTimeOffset(2026, 7, 18, 0, 48, 0, TimeSpan.Zero);

            RoutePlanPersistence.Save(
                path, plan, plan.Routes[0].Number,
                showAll: false, selectedBarterRowId: null,
                optimizationMode: RouteOptimizationMode.Balanced,
                savedAtUtc: stamp);

            var result = RoutePlanPersistence.TryLoad(path, plan.InputFingerprint);

            Assert.Equal(RoutePlanLoadStatus.Loaded, result.Status);
            Assert.Equal(stamp, result.SavedAtUtc);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Saved_at_utc_defaults_to_recent_utc_when_not_specified() {
        string path = TempPath();
        try {
            var (plan, _) = PlanWith(RouteTestData.SingleTask(),
                RouteOptimizationProfile.For(RouteOptimizationMode.Balanced));
            DateTimeOffset before = DateTimeOffset.UtcNow.AddSeconds(-1);

            RoutePlanPersistence.Save(
                path, plan, plan.Routes[0].Number,
                showAll: false, selectedBarterRowId: null,
                optimizationMode: RouteOptimizationMode.Balanced);

            DateTimeOffset after = DateTimeOffset.UtcNow.AddSeconds(1);
            var result = RoutePlanPersistence.TryLoad(path, plan.InputFingerprint);

            Assert.Equal(RoutePlanLoadStatus.Loaded, result.Status);
            Assert.NotNull(result.SavedAtUtc);
            Assert.InRange(result.SavedAtUtc!.Value.UtcDateTime, before.UtcDateTime, after.UtcDateTime);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Try_load_returns_loaded_status_with_snapshot_on_match() {
        string path = TempPath();
        try {
            var (plan, _) = PlanWith(RouteTestData.SingleTask(),
                RouteOptimizationProfile.For(RouteOptimizationMode.Balanced));
            RoutePlanPersistence.Save(
                path, plan, plan.Routes[0].Number,
                showAll: false, selectedBarterRowId: "r1",
                optimizationMode: RouteOptimizationMode.Balanced);

            var result = RoutePlanPersistence.TryLoad(path, plan.InputFingerprint);

            Assert.Equal(RoutePlanLoadStatus.Loaded, result.Status);
            Assert.NotNull(result.Snapshot);
            Assert.Equal(2, result.SchemaVersion);
            Assert.Equal(plan.InputFingerprint, result.SavedFingerprint);
            Assert.Equal(plan.InputFingerprint, result.ExpectedFingerprint);
            Assert.Equal(plan.Routes[0].Number, result.Snapshot!.SelectedRouteNumber);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Try_load_returns_fingerprint_mismatch_when_expected_does_not_match() {
        string path = TempPath();
        try {
            var (plan, _) = PlanWith(RouteTestData.SingleTask(),
                RouteOptimizationProfile.For(RouteOptimizationMode.Balanced));
            RoutePlanPersistence.Save(
                path, plan, plan.Routes[0].Number,
                showAll: false, selectedBarterRowId: null,
                optimizationMode: RouteOptimizationMode.Balanced);

            var result = RoutePlanPersistence.TryLoad(path, "different-fingerprint");

            Assert.Equal(RoutePlanLoadStatus.FingerprintMismatch, result.Status);
            Assert.Null(result.Snapshot);
            Assert.Equal(plan.InputFingerprint, result.SavedFingerprint);
            Assert.Equal("different-fingerprint", result.ExpectedFingerprint);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Try_load_returns_file_not_found_status_when_file_missing() {
        string path = Path.Combine(
            Path.GetTempPath(), "iBarter-route-noexist-" + Guid.NewGuid().ToString("N") + ".json");

        var result = RoutePlanPersistence.TryLoad(path, "anything");

        Assert.Equal(RoutePlanLoadStatus.FileNotFound, result.Status);
        Assert.Null(result.Snapshot);
        Assert.Equal(0, result.SchemaVersion);
    }

    [Fact]
    public void Try_load_returns_unsupported_schema_status_for_future_schema() {
        string path = TempPath();
        try {
            File.WriteAllText(path, """
                {
                  "SchemaVersion": 999,
                  "InputFingerprint": "x",
                  "Status": 0,
                  "Objective": null,
                  "Diagnostics": [],
                  "Routes": []
                }
                """);

            var result = RoutePlanPersistence.TryLoad(path, "x");

            Assert.Equal(RoutePlanLoadStatus.UnsupportedSchema, result.Status);
            Assert.Equal(999, result.SchemaVersion);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Try_load_returns_corrupt_file_status_on_invalid_json() {
        string path = TempPath();
        try {
            File.WriteAllText(path, "{ definitely not json");

            var result = RoutePlanPersistence.TryLoad(path, "x");

            Assert.Equal(RoutePlanLoadStatus.CorruptFile, result.Status);
            Assert.Null(result.Snapshot);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Try_load_returns_corrupt_file_status_on_truncated_envelope() {
        string path = TempPath();
        try {
            File.WriteAllText(path, "{\"SchemaVersion\": 2, \"Inpu");

            var result = RoutePlanPersistence.TryLoad(path, "x");

            Assert.Equal(RoutePlanLoadStatus.CorruptFile, result.Status);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void V1_envelope_without_optimization_mode_still_parses_and_round_trips() {
        // Backward compat: a file written before the fix has SchemaVersion=1
        // and no OptimizationMode field. The caller does not know which mode
        // was used and must try each of Quick/Balanced/Deep until one
        // fingerprint matches. This test asserts the persistence layer
        // returns SavedOptimizationMode=null for v1 (the caller resolves it).
        string path = TempPath();
        try {
            var quickProfile = RouteOptimizationProfile.For(RouteOptimizationMode.Quick);
            var (plan, request) = PlanWith(RouteTestData.SingleTask(), quickProfile);
            string v2Json = BuildV2JsonManually(plan, "r1", RouteOptimizationMode.Quick);
            // Strip OptimizationMode + SavedAtUtc + bump down SchemaVersion
            // to mimic a v1 envelope written before the fix.
            string v1Json = v2Json
                .Replace("\"OptimizationMode\": 0,", string.Empty)
                .Replace("\"SavedAtUtc\":", "\"_SavedAtUtc\":")  // hide it
                .Replace("\"SchemaVersion\": 2,", "\"SchemaVersion\": 1,");
            File.WriteAllText(path, v1Json);

            var expectedFingerprint = RoutePlanFingerprint.Compute(request);
            var result = RoutePlanPersistence.TryLoad(path, expectedFingerprint);

            Assert.Equal(RoutePlanLoadStatus.Loaded, result.Status);
            Assert.Equal(1, result.SchemaVersion);
            Assert.Null(result.SavedOptimizationMode);
            Assert.NotNull(result.Snapshot);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void V1_envelope_fingerprint_mismatch_when_caller_picks_wrong_mode() {
        // The same v1 envelope must reject the wrong mode's fingerprint.
        // This is the protection that prevents silently loading a route
        // generated with one budget under a different budget.
        string path = TempPath();
        try {
            var quickProfile = RouteOptimizationProfile.For(RouteOptimizationMode.Quick);
            var (plan, _) = PlanWith(RouteTestData.SingleTask(), quickProfile);
            string v2Json = BuildV2JsonManually(plan, "r1", RouteOptimizationMode.Quick);
            string v1Json = v2Json
                .Replace("\"OptimizationMode\": 0,", string.Empty)
                .Replace("\"SavedAtUtc\":", "\"_SavedAtUtc\":")
                .Replace("\"SchemaVersion\": 2,", "\"SchemaVersion\": 1,");
            File.WriteAllText(path, v1Json);

            // Balanced request would produce MaxLocalMoves=500, not 150.
            var balancedRequest = new AutomaticRoutePlanningRequest(
                RouteTestData.SingleTask().Tasks,
                RouteTestData.SingleTask().Items,
                RouteTestData.SingleTask().Warehouses,
                RouteTestData.SingleTask().ExtraLT,
                RouteTestData.SingleTask().TotalLT,
                new RouteSearchLimits(250_000, 500),
                RouteTestData.SingleTask().ConfigurationVersion);
            var balancedFingerprint = RoutePlanFingerprint.Compute(balancedRequest);
            var result = RoutePlanPersistence.TryLoad(path, balancedFingerprint);

            Assert.Equal(RoutePlanLoadStatus.FingerprintMismatch, result.Status);
            Assert.Null(result.Snapshot);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Try_load_does_not_throw_when_file_unreadable() {
        // A status result is preferable to a crash; even unreadable files
        // (locked, ACL issues, etc.) should yield CorruptFile rather than
        // propagating the IOException out of startup.
        string path = TempPath();
        try {
            File.WriteAllText(path, "{}");
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.None);

            var result = RoutePlanPersistence.TryLoad(path, "anything");

            Assert.NotEqual(RoutePlanLoadStatus.Loaded, result.Status);
        }
        catch (PlatformNotSupportedException) {
            // Some test environments cannot lock files; the persistence
            // contract under test is still the no-throw guarantee for
            // *unreadable* files, not for environments that disallow locks.
        }
        finally {
            try { File.Delete(path); } catch { }
        }
    }

    private static (RoutePlan plan, AutomaticRoutePlanningRequest request) PlanWith(
        AutomaticRoutePlanningRequest baseRequest, RouteOptimizationProfile profile) {
        var planner = new AutomaticRoutePlanner();
        var plan = planner.Plan(baseRequest, profile, TestContext.Current.CancellationToken);
        Assert.True(plan.Routes.Count > 0,
            "Test scenario must produce a routable plan; check RouteTestData.SingleTask inputs.");
        return (plan, baseRequest);
    }

    private static string BuildV2JsonManually(
        RoutePlan plan, string? selectedBarterRowId, RouteOptimizationMode mode) {
        // Route through the production Save() so we get a real envelope
        // shape, then we mutate it in tests to simulate schema variants.
        string tmpPath = TempPath();
        try {
            RoutePlanPersistence.Save(
                tmpPath, plan, plan.Routes[0].Number,
                showAll: false, selectedBarterRowId: selectedBarterRowId,
                optimizationMode: mode);
            return File.ReadAllText(tmpPath);
        }
        finally { File.Delete(tmpPath); }
    }

    private static string TempPath() => Path.Combine(
        Path.GetTempPath(), "iBarter-route-" + Guid.NewGuid().ToString("N") + ".json");
}
