using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public class TaggedPortDepartureTests {
    private static TaggedTransportRequest Request(bool midnightEnabled = true) => new() {
        ShipLimitLT = 2000,
        Settings = new() { SearchSeconds = 1, MaxStates = 1, Ports = [
            new() { IslandId = "Velia", WarehouseId = "Velia", Enabled = true },
            new() { IslandId = "A", Enabled = midnightEnabled }, new() { IslandId = "B", Enabled = true }] },
        Items = new() { ["a"] = new("a", "Input", 4, 1000), ["b"] = new("b", "Output", 5, 1000) },
        Points = new() { ["Velia"] = new(0, 0), ["A"] = new(100000, 0), ["B"] = new(200000, 0) },
        Warehouses = new() { ["Velia"] = new() { ["a"] = 2 } },
        Trades = [new("first", "A", "a", 1, "b", 2, 1), new("second", "B", "a", 1, "b", 1, 1)]
    };
    private static TaggedTransportState Original(TaggedTransportSimulator sim) {
        var state = sim.Initial();
        foreach (var action in new TaggedAction[] {
            new(TaggedActionKind.Transfer, "Velia", "warehouse:Velia", "ship", "a", 2),
            new(TaggedActionKind.Sail, "A", "Velia"), new(TaggedActionKind.Barter, "A", Quantity: 1, TradeIndex: 0),
            new(TaggedActionKind.Sail, "B", "A"), new(TaggedActionKind.Transfer, "B", "ship", "main", "b", 1),
            new(TaggedActionKind.Barter, "B", Quantity: 1, TradeIndex: 1), new(TaggedActionKind.Sail, "Velia", "B"),
            new(TaggedActionKind.Transfer, "Velia", "ship", "warehouse:Velia", "b", 2),
            new(TaggedActionKind.Transfer, "Velia", "main", "warehouse:Velia", "b", 1)
        }) Assert.True(sim.TryApply(state, action, out state, out _));
        Assert.True(sim.Complete(state));
        return state;
    }

    [Fact] public void ExistingRouteMovesHandlingBeforeDepartureWithoutChangingTradesOrDistance() {
        var sim = new TaggedTransportSimulator(Request()); var original = Original(sim);
        Assert.True(original.OverloadedDistance > 0);
        var next = TaggedPortDepartureOptimizer.Improve(sim, original, TestContext.Current.CancellationToken);
        Assert.Equal(original.Distance, next.Distance);
        Assert.Equal(0, next.OverloadedDistance);
        Assert.Equal(original.Steps.Count, next.Steps.Count);
        Assert.Equal(original.Steps.Where(s => s.Action.Kind == TaggedActionKind.Barter).Select(s => s.Action),
            next.Steps.Where(s => s.Action.Kind == TaggedActionKind.Barter).Select(s => s.Action));
        foreach (var cargo in original.Cargo) Assert.Equal(cargo.Value.OrderBy(i => i.Key), next.Cargo[cargo.Key].OrderBy(i => i.Key));
        Assert.Contains(next.Steps, s => s.Action is { Kind: TaggedActionKind.Transfer, Location: "A", From: "ship", To: "main" });
        Assert.True(sim.Complete(next));
    }
    [Fact] public void UnavailableDeparturePortKeepsTheLegalArrivalHandling() {
        var sim = new TaggedTransportSimulator(Request(false)); var original = Original(sim);
        Assert.Same(original, TaggedPortDepartureOptimizer.Improve(sim, original, TestContext.Current.CancellationToken));
    }
    [Fact] public void FreshCompilerChoosesTheDeparturePortOnEqualDistance() {
        var sim = new TaggedTransportSimulator(Request());
        var state = new TaggedVoyageCompiler(sim, () => false).Compile(sim.Initial(), [new(0, 1), new(1, 1)], "Velia", packing: 3);
        Assert.NotNull(state); Assert.True(sim.Complete(state));
        Assert.True(state.OverloadedDistance == 0, string.Join("; ", state.Steps.Select(s => $"{s.Action.Kind}:{s.Action.Location}:{s.Action.From}->{s.Action.To}:{s.Action.Quantity}:{s.ShipLT}")));
        Assert.Contains(state.Steps, s => s.Action is { Kind: TaggedActionKind.Transfer, Location: "A", From: "ship", To: "main" or "alt" });
    }
    [Fact] public void SavedIncumbentReceivesTheSameFixEvenWithMinimalSearchBudget() {
        var r = Request(); var old = Original(new(r));
        var plan = new TaggedTransportPlan(r.Fingerprint(), old.Steps.ToArray(), old.Seconds, old.SailingSeconds,
            old.Seconds - old.SailingSeconds, old.Distance, "BestKnownWithinLimit");
        var result = new TaggedTransportPlanner().Plan(r, TestContext.Current.CancellationToken, preferredTaggedPlan: plan);
        Assert.NotNull(result.Plan);
        Assert.True(new TaggedTransportSimulator(result.PlannedRequest!).Verify(result.Plan, out var state, out _));
        Assert.True(state.Distance <= old.Distance); Assert.Equal(0, state.OverloadedDistance);
    }
}
