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

    private static string TempPath() => Path.Combine(
        Path.GetTempPath(), "iBarter-route-" + Guid.NewGuid().ToString("N") + ".json");
}
