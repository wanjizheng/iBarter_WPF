using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public class TaggedTerminalSalesTests {
    private static TaggedTransportRequest Request(int level = 7, bool portEnabled = true) => new() {
        ShipLimitLT = 2000,
        Settings = new() { SearchSeconds = 1, MaxStates = 1, Ports = [
            new() { IslandId = "Velia", WarehouseId = "Velia", Enabled = true },
            new() { IslandId = "A", Enabled = portEnabled }, new() { IslandId = "B", Enabled = true }] },
        Items = new() { ["a"] = new("a", "Input", 4, 1000), ["b"] = new("b", "Terminal goods", level, 1000) },
        Points = new() { ["Velia"] = new(0, 0), ["A"] = new(100000, 0), ["B"] = new(200000, 0) },
        Warehouses = new() { ["Velia"] = new() { ["a"] = 2, ["b"] = 10 } },
        Trades = [new("first", "A", "a", 1, "b", 2, 1), new("second", "B", "a", 1, "b", 1, 1)]
    };
    private static TaggedTransportState AtFirstExchange(TaggedTransportSimulator sim) {
        var state = sim.Initial();
        foreach (var action in new TaggedAction[] {
            new(TaggedActionKind.Transfer, "Velia", "warehouse:Velia", "ship", "a", 2),
            new(TaggedActionKind.Sail, "A", "Velia"), new(TaggedActionKind.Barter, "A", Quantity: 1, TradeIndex: 0)
        }) Assert.True(sim.TryApply(state, action, out state, out _));
        return state;
    }
    [Fact] public void SaleFreesShipWithoutDepositingAndPreservesStockAndConservation() {
        var sim = new TaggedTransportSimulator(Request()); var state = AtFirstExchange(sim);
        var next = TaggedTerminalSales.SellAvailable(sim, state);
        Assert.Equal(2, next.SoldItems["b"]); Assert.Empty(state.SoldItems);
        Assert.Equal(1000, sim.Weight(next, "ship"));
        Assert.Equal(10, sim.Count(next, "warehouse:Velia", "b"));
        Assert.Equal(0, sim.Count(next, "main", "b") + sim.Count(next, "alt", "b"));
        Assert.Equal(TaggedActionKind.Sell, next.Steps.Last().Action.Kind);
        Assert.Single(TaggedOperationGroups.Build(sim.Request, new(sim.Request.Fingerprint(), next.Steps.ToArray(),
            next.Seconds, next.SailingSeconds, next.Seconds - next.SailingSeconds, next.Distance, "test")).Where(g => g.Kind == "selling"));
    }
    [Theory] [InlineData(6, true)] [InlineData(7, false)]
    public void OtherLevelsAndIslandsWithoutEnabledPortsCannotSell(int level, bool enabled) {
        var sim = new TaggedTransportSimulator(Request(level, enabled)); var state = AtFirstExchange(sim);
        Assert.False(sim.TryApply(state, new(TaggedActionKind.Sell, "A", "ship", "shop", "b", 2), out _, out _));
        Assert.Same(state, TaggedTerminalSales.SellAvailable(sim, state));
    }
    [Fact] public void FutureInputsAreNotSold() {
        var request = Request() with { Trades = [new("first", "A", "a", 1, "b", 2, 1), new("second", "B", "b", 1, "a", 1, 1)] };
        var sim = new TaggedTransportSimulator(request); var state = AtFirstExchange(sim);
        Assert.Same(state, TaggedTerminalSales.SellAvailable(sim, state));
    }
    [Fact] public void SaleNeedsAReceivingSlotAndCanUseTheOtherCharacter() {
        var request = Request(); request.Settings.Carriers[0].Slots = 1; request.Settings.Carriers[1].Slots = 1;
        request.Settings.InitialCargo = [new("main", "a", 1), new("alt", "a", 1)];
        var sim = new TaggedTransportSimulator(request); var state = AtFirstExchange(sim);
        Assert.Same(state, TaggedTerminalSales.SellAvailable(sim, state));
        request = Request(); request.Settings.Carriers[0].OccupiedLT = 3000;
        sim = new(request); state = AtFirstExchange(sim);
        var sold = TaggedTerminalSales.SellAvailable(sim, state);
        Assert.Equal(2, sold.SoldItems["b"]); Assert.Equal("main", sold.Active);
        Assert.Contains(sold.Steps, s => s.Action is { Kind: TaggedActionKind.Switch, To: "alt" });
    }
    [Fact] public void CompilerSellsAtDepartureInsteadOfParkingOrOverloadedSailing() {
        var sim = new TaggedTransportSimulator(Request());
        var state = new TaggedVoyageCompiler(sim, () => false).Compile(sim.Initial(), [new(0, 1), new(1, 1)], "Velia", packing: 3);
        Assert.NotNull(state); Assert.True(sim.Complete(state)); Assert.Equal(0, state.OverloadedDistance);
        Assert.Equal(3, state.SoldItems["b"]); Assert.Equal(10, sim.Count(state, "warehouse:Velia", "b"));
        Assert.DoesNotContain(state.Steps, s => s.Action is { Kind: TaggedActionKind.Transfer, ItemId: "b" });
        Assert.Contains(state.Steps, s => s.Action is { Kind: TaggedActionKind.Sell, Location: "A", Quantity: 2 });
        var exchangeOnly = new TaggedVoyageCompiler(sim, () => false).Compile(sim.Initial(), [new(0, 1), new(1, 1)], "Velia", packing: 3, stopAfterFirstExchange: true);
        Assert.NotNull(exchangeOnly); Assert.Empty(exchangeOnly.SoldItems);
        Assert.Equal(TaggedActionKind.Barter, exchangeOnly.Steps.Last().Action.Kind);
    }
    [Fact] public void OlderSavedPlanIsStillVerifiableAndNewPlanningReplacesItsStorage() {
        var r = Request(); var sim = new TaggedTransportSimulator(r); var state = AtFirstExchange(sim);
        foreach (var action in new TaggedAction[] {
            new(TaggedActionKind.Sail, "B", "A"), new(TaggedActionKind.Transfer, "B", "ship", "main", "b", 1),
            new(TaggedActionKind.Barter, "B", Quantity: 1, TradeIndex: 1), new(TaggedActionKind.Sail, "Velia", "B"),
            new(TaggedActionKind.Transfer, "Velia", "ship", "warehouse:Velia", "b", 2),
            new(TaggedActionKind.Transfer, "Velia", "main", "warehouse:Velia", "b", 1)
        }) Assert.True(sim.TryApply(state, action, out state, out _));
        var old = new TaggedTransportPlan(r.Fingerprint(), state.Steps.ToArray(), state.Seconds, state.SailingSeconds,
            state.Seconds - state.SailingSeconds, state.Distance, "BestKnownWithinLimit");
        Assert.True(sim.Verify(old, out _, out _));
        var result = new TaggedTransportPlanner().Plan(r, TestContext.Current.CancellationToken, preferredTaggedPlan: old);
        Assert.NotNull(result.Plan); Assert.True(new TaggedTransportSimulator(result.PlannedRequest!).Verify(result.Plan, out var final, out _));
        Assert.Equal(3, final.SoldItems["b"]);
        Assert.DoesNotContain(final.Steps, s => s.Action is { Kind: TaggedActionKind.Transfer, ItemId: "b" });
        Assert.Equal(10, final.Cargo["warehouse:Velia"]["b"]);
    }
    [Fact] public void FullCharactersUnloadOtherGoodsBeforeSellingInsteadOfDepositingLevelSeven() {
        var request = Request() with { Trades = [] };
        request.Settings.InitialCargo = [new("ship", "b", 2), new("main", "a", 3), new("alt", "a", 3)];
        var sim = new TaggedTransportSimulator(request); var state = sim.Initial();
        foreach (var action in new TaggedAction[] {
            new(TaggedActionKind.Transfer, "Velia", "ship", "warehouse:Velia", "b", 2),
            new(TaggedActionKind.Transfer, "Velia", "main", "warehouse:Velia", "a", 3),
            new(TaggedActionKind.Switch, "Velia", "main", "alt"),
            new(TaggedActionKind.Transfer, "Velia", "alt", "warehouse:Velia", "a", 3)
        }) Assert.True(sim.TryApply(state, action, out state, out _));
        var next = TaggedTerminalSales.Normalize(sim, state, TestContext.Current.CancellationToken);
        Assert.True(sim.Complete(next)); Assert.Equal(2, next.SoldItems["b"]);
        Assert.Equal(8, next.Cargo["warehouse:Velia"]["a"]); Assert.Equal(10, next.Cargo["warehouse:Velia"]["b"]);
        Assert.DoesNotContain(next.Steps, s => s.Action is { Kind: TaggedActionKind.Transfer, ItemId: "b" });
        Assert.Equal("a", next.Steps.First().Action.ItemId);
    }
}
