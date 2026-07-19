using Xunit;

namespace AutomaticRoutePlanningTests;

public sealed class RealSavedPlanIsoNormalizerTests {
    [Fact]
    public void RegressionFixtureIsProjectLocalAndDoesNotReferenceUserRuntimePath() {
        string fixturePath = Path.Combine(
            AppContext.BaseDirectory, "TestData", "cross-route-cargo-leak-plan.json");

        Assert.True(File.Exists(fixturePath));
        Assert.StartsWith(AppContext.BaseDirectory, fixturePath, StringComparison.OrdinalIgnoreCase);
        string json = File.ReadAllText(fixturePath);
        Assert.DoesNotContain("automatic-route-plan.json", json, StringComparison.OrdinalIgnoreCase);
    }
}
