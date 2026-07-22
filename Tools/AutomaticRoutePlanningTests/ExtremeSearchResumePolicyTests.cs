using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public sealed class ExtremeSearchResumePolicyTests {
    [Fact]
    public void Restart_round_trip_restores_an_extreme_incumbent_and_offers_the_choice() {
        string path = Path.Combine(Path.GetTempPath(), $"ibarter-extreme-resume-{Guid.NewGuid():N}.json");
        try {
            var saved = Plan("same");
            RoutePlanPersistence.Save(
                path,
                saved,
                selectedRouteNumber: null,
                showAll: false,
                optimizationMode: RouteOptimizationMode.Extreme);

            RoutePlanLoadResult loaded = RoutePlanPersistence.TryLoad(path, "same");

            Assert.Equal(RoutePlanLoadStatus.Loaded, loaded.Status);
            Assert.True(ExtremeSearchResumePolicy.CanOfferContinuation(
                restoredFromDisk: true,
                loaded.SavedOptimizationMode!.Value,
                loaded.Snapshot!.Plan,
                "same"));
        }
        finally {
            File.Delete(path);
            File.Delete(path + ".bak");
        }
    }

    [Fact]
    public void Restored_matching_extreme_plan_offers_continue_or_restart_choice() {
        var plan = Plan("same");

        Assert.True(ExtremeSearchResumePolicy.CanOfferContinuation(
            restoredFromDisk: true,
            RouteOptimizationMode.Extreme,
            plan,
            "same"));
    }

    [Theory]
    [InlineData(false, RouteOptimizationMode.Extreme, "same")]
    [InlineData(true, RouteOptimizationMode.Balanced, "same")]
    [InlineData(true, RouteOptimizationMode.Extreme, "different")]
    public void New_session_prompt_is_not_shown_for_ineligible_plan(
        bool restoredFromDisk,
        RouteOptimizationMode mode,
        string requestFingerprint) {
        Assert.False(ExtremeSearchResumePolicy.CanOfferContinuation(
            restoredFromDisk,
            mode,
            Plan("same"),
            requestFingerprint));
    }

    private static RoutePlan Plan(string fingerprint) => new(
        RoutePlanStatus.BestKnownWithinLimit,
        [],
        new RoutePlanObjective(1, 100, 1, 1_000, "seed"),
        [],
        fingerprint);
}
