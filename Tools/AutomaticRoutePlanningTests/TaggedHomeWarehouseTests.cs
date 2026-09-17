using iBarter.Routing;
using Xunit;

public class TaggedHomeWarehouseTests {
    [Fact] public void ForeignPortKeepsFinishedGoodsOnCharactersAndFinalVoyageReturnsHome() {
        var r = new TaggedTransportRequest {
            ShipLimitLT = 3000,
            Items = new() { ["a"] = new("a", "Input", 5, 1000), ["b"] = new("b", "Quartz", 5, 1000), ["c"] = new("c", "Output", 5, 1000) },
            Points = new() { ["Iliya"] = new(0, 0), ["Epheria"] = new(10000, 0), ["A"] = new(9000, 100), ["B"] = new(11000, 100) },
            Warehouses = new() { ["Iliya"] = new() { ["a"] = 1 }, ["Epheria"] = new() { ["a"] = 1 } },
            Trades = [new("first", "A", "a", 1, "b", 2, 1), new("second", "B", "a", 1, "c", 2, 1)],
            Settings = new() { StartIsland = "Iliya", HomeWarehouseId = "Iliya", Ports = [
                new() { IslandId = "Iliya", WarehouseId = "Iliya", Enabled = true },
                new() { IslandId = "Epheria", WarehouseId = "Epheria", Enabled = true }] }
        };
        var sim = new TaggedTransportSimulator(r); var compiler = new TaggedVoyageCompiler(sim, () => false);
        var first = compiler.Compile(sim.Initial(), [new(0, 1)], "Epheria");
        Assert.NotNull(first);
        Assert.Equal(2, sim.Count(first, "main", "b") + sim.Count(first, "alt", "b"));
        Assert.Equal(0, sim.Count(first, "warehouse:Epheria", "b"));
        var final = compiler.Compile(first, [new(1, 1)], "Epheria");
        Assert.NotNull(final); Assert.True(sim.Complete(final)); Assert.Equal("Iliya", final.Location);
        Assert.Equal(2, sim.Count(final, "warehouse:Iliya", "b")); Assert.Equal(2, sim.Count(final, "warehouse:Iliya", "c"));
    }

    [Fact] public void LateExchangeRebuildsLoadingInsteadOfReplayingAnImpossiblePrefix() {
        var r = new TaggedTransportRequest {
            ShipLimitLT = 3000,
            Items = new() { ["a"] = new("a", "Input", 5, 1000), ["b"] = new("b", "Output", 5, 1000), ["coin"] = new("coin", "Coin", -1, 0) },
            Points = new() { ["Velia"] = new(0, 0), ["A"] = new(1000, 0), ["B"] = new(2000, 0) },
            Warehouses = new() { ["Velia"] = new() { ["a"] = 3 } },
            Trades = [new("first", "A", "a", 2, "coin", 1, 1), new("second", "B", "a", 1, "b", 4, 1)],
            Settings = new() { Ports = [new() { IslandId = "Velia", WarehouseId = "Velia", Enabled = true }] }
        };
        var sim = new TaggedTransportSimulator(r); var state = sim.Initial();
        foreach (var a in new TaggedAction[] {
            new(TaggedActionKind.Transfer, "Velia", "warehouse:Velia", "ship", "a", 3),
            new(TaggedActionKind.Sail, "A", "Velia"), new(TaggedActionKind.Barter, "A", Quantity: 1, TradeIndex: 0),
            new(TaggedActionKind.Sail, "B", "A"), new(TaggedActionKind.Barter, "B", Quantity: 1, TradeIndex: 1),
            new(TaggedActionKind.Sail, "Velia", "B"), new(TaggedActionKind.Transfer, "Velia", "ship", "warehouse:Velia", "b", 4) })
            Assert.True(sim.TryApply(state, a, out state, out _));
        var plan = new TaggedTransportPlan(r.Fingerprint(), state.Steps.ToArray(), state.Seconds, state.SailingSeconds,
            state.Seconds - state.SailingSeconds, state.Distance, "BestKnownWithinLimit");
        Assert.True(sim.Verify(plan, out _, out _));
        var request = TaggedCompletionReplanner.AtCompletedExchange(new(1, r, plan, 0), 4);
        Assert.Equal("first", Assert.Single(request.Trades).RowId);
        Assert.Equal(4, request.Settings.InitialCargo.Where(c => c.ItemId == "b").Sum(c => c.Quantity));
        Assert.Equal(2, request.Warehouses["Velia"]["a"]);
        Assert.Equal("B", request.Settings.StartIsland);
    }
}
