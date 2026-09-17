using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

// Proves CanBarterFast agrees with the full TryBarter feasibility verdict across
// a range of onboard/weight/mask situations, so the fast probe can safely
// replace the full transition inside HasExecutableBarter.
public sealed class CanBarterFastTests {
    private static AutomaticRoutePlanningRequest Request(int totalLT) {
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["IN"] = new("IN", "In", 1, 100),
            ["OUT"] = new("OUT", "Out", 2, 400),
            ["HEAVY"] = new("HEAVY", "Heavy", 3, 900),
        };
        var tasks = new[] {
            new RouteBarterTask("r0", "A", new RoutePoint(1, 0), "IN", 2, "OUT", 1),
            new RouteBarterTask("r1", "B", new RoutePoint(2, 0), "IN", 3, "HEAVY", 2),
            new RouteBarterTask("r2", "C", new RoutePoint(3, 0), "OUT", 1, "IN", 1),
        };
        var warehouse = new RouteWarehouse("W", "W", new RoutePoint(0, 0),
            new Dictionary<string, int>(StringComparer.Ordinal) { ["IN"] = 100 });
        return new AutomaticRoutePlanningRequest(
            tasks, items, [warehouse], extraLT: 50, totalLT: totalLT,
            new RouteSearchLimits(100_000, 100), "canbarterfast-test");
    }

    [Theory]
    [InlineData(10_000, 0, 0)]
    [InlineData(10_000, 5, 0)]
    [InlineData(10_000, 1, 0)]     // insufficient IN for r0/r1
    [InlineData(10_000, 3, 2)]     // some OUT onboard enables r2
    [InlineData(600, 5, 0)]        // tight LT triggers overweight rejection
    [InlineData(500, 10, 0)]       // very tight LT
    public void Fast_probe_matches_full_transition(int totalLT, int inStock, int outStock) {
        var request = Request(totalLT);
        var onBoard = new Dictionary<string, int>(StringComparer.Ordinal);
        if (inStock > 0) onBoard["IN"] = inStock;
        if (outStock > 0) onBoard["OUT"] = outStock;

        // Seed a state carrying the specified onboard cargo consistently
        // (CargoLT and OnBoard populated by the real transition path).
        var manual = SeedState(request, onBoard);

        for (int i = 0; i < request.Tasks.Count; i++) {
            bool fast = RouteStateTransition.CanBarterFast(request, manual, i);
            bool full = RouteStateTransition.TryBarter(request, manual, i).Success;
            Assert.True(fast == full,
                $"task {i} totalLT={totalLT} in={inStock} out={outStock}: fast={fast} full={full}");
        }
    }

    // Builds a state whose OnBoard/CargoLT reflect the given cargo, by pulling it
    // from an oversized warehouse in a single pickup (weights recomputed by the
    // real transition path).
    private static RouteSimulationState SeedState(
        AutomaticRoutePlanningRequest request, IReadOnlyDictionary<string, int> onBoard) {
        var items = onBoard.Where(x => x.Value > 0)
            .Select(x => new RouteItemQuantity(x.Key, x.Value)).ToArray();
        var initial = RouteSimulationState.CreateInitial(request);
        if (items.Length == 0) return initial;
        var stock = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pair in onBoard) stock[pair.Key] = pair.Value + 10;
        var big = new AutomaticRoutePlanningRequest(
            request.Tasks, request.Items,
            [new RouteWarehouse("W", "W", new RoutePoint(0, 0), stock)],
            request.ExtraLT, 1_000_000, request.Limits, request.ConfigurationVersion);
        var pickup = RouteStateTransition.TryPickup(big, RouteSimulationState.CreateInitial(big), "W", items);
        Assert.True(pickup.Success);
        return pickup.State;
    }
}
