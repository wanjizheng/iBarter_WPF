using iBarter.Planning;
using Xunit;

namespace PlannerAutoPlannerTests;

public sealed class PlannerAutoPlannerTests {
    [Fact]
    public void Request_rejects_duplicate_row_ids() {
        var route = Route("r1", 1, "A", 4, 1, "B", 5, 1, 10_000, 5);
        var request = new AutoPlanningRequest(
            [route, route], new Dictionary<string, int> { ["A"] = 10 },
            AutoPlanningStrategy.ProfitFirst, 10, 10, 1_000_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == "duplicate-row-id");
    }

    private static AutoPlanningRoute Route(
        string id, int group, string item1, int lv1, int n1,
        string item2, int lv2, int n2, int parley, int remaining,
        bool crow = false) =>
        new(id, group, item1, lv1, n1, item2, lv2, n2, crow, parley, remaining);
}