using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public class TaggedPackingOrderTests {
    [Fact] public void RebalanceDoesNotSwitchToMainBetweenEachTagTransfer() {
        var r = new TaggedTransportRequest {
            ShipLimitLT = 5000,
            Settings = new() { StartIsland = "Port", InitialCargo = [
                new("ship", "flower", 5), new("alt", "box", 3) ], Ports = [
                new() { IslandId = "Port", Enabled = true },
                new() { IslandId = "Iliya", WarehouseId = "Iliya", Enabled = true } ] },
            Items = new() { ["flower"] = new("flower", "Flower", 5, 1000),
                ["box"] = new("box", "Box", 4, 1000), ["coin"] = new("coin", "Coin", -1, 0) },
            Points = new() { ["Port"] = new(0, 0), ["Iliya"] = new(200, 0) },
            Trades = [new("trade", "Port", "box", 1, "coin", 1, 3)]
        };
        var alt = r.Settings.Carriers.Single(c => c.Id == "alt"); alt.LimitLT = 10000; alt.Slots = 30;
        var sim = new TaggedTransportSimulator(r);
        var result = new TaggedVoyageCompiler(sim, () => false).Compile(sim.Initial(), [new(0, 3)], "Iliya",
            packing: 3, prepareAtWarehouse: false, stopAfterFirstExchange: true);
        Assert.NotNull(result);
        var portSwitches = result.Steps.Where(s => s.Action is { Kind: TaggedActionKind.Switch, Location: "Port" })
            .Select(s => s.Action.To).ToArray();
        Assert.Equal(new[] { "alt", "main" }, portSwitches);
        Assert.Equal(3, result.Steps.Count(s => s.Action is { Kind: TaggedActionKind.Transfer, Location: "Port", From: "alt", To: "ship", ItemId: "box" }));
        var replay = sim.Initial();
        foreach (var step in result.Steps) Assert.True(sim.TryApply(replay, step.Action, out replay, out var error), error);
    }

    [Fact] public void MidVoyagePackingFinishesTagBeforeSwitchingToMain() {
        var r = new TaggedTransportRequest {
            ShipLimitLT = 12000,
            Settings = new() { StartIsland = "Start", HomeWarehouseId = "Iliya", Ports = [
                new() { IslandId = "Port", Enabled = true },
                new() { IslandId = "Iliya", WarehouseId = "Iliya", Enabled = true } ],
                InitialCargo = [new("ship", "input", 1), new("ship", "drug", 5),
                    new("ship", "orange", 2), new("ship", "octopus", 2)] },
            Items = new() { ["input"] = new("input", "Input", 4, 1000),
                ["output"] = new("output", "Output", 5, 15000),
                ["drug"] = new("drug", "Drug", 4, 1000),
                ["orange"] = new("orange", "Orange", 4, 1000),
                ["octopus"] = new("octopus", "Octopus", 4, 1000) },
            Points = new() { ["Start"] = new(0, 0), ["Port"] = new(100, 0),
                ["Trade"] = new(200, 0), ["Iliya"] = new(300, 0) },
            Trades = [new("trade", "Trade", "input", 1, "output", 1, 1)]
        };
        r.Settings.Carriers.Single(c => c.Id == "alt").LimitLT = 3000;
        r.Settings.Carriers.Single(c => c.Id == "main").LimitLT = 3000;
        var sim = new TaggedTransportSimulator(r);
        var result = new TaggedVoyageCompiler(sim, () => false).Compile(sim.Initial(), [new(0, 1)], "Iliya",
            packing: 3, prepareAtWarehouse: false, stopAfterFirstExchange: true);
        Assert.NotNull(result);
        var parked = result.Steps.Where(s => s.Action is { Kind: TaggedActionKind.Transfer, From: "ship", To: "alt" or "main" }).ToArray();
        Assert.True(parked.Length > 0, string.Join("; ", result.Steps.Select(s =>
            $"{s.Action.Kind}:{s.Action.Location}:{s.Action.From}->{s.Action.To}:{s.Action.ItemId}x{s.Action.Quantity}")));
        Assert.Equal("alt", parked[0].Action.To);
        int firstMain = Array.FindIndex(parked, s => s.Action.To == "main");
        if (firstMain >= 0) Assert.DoesNotContain(parked.Skip(firstMain + 1), s => s.Action.To == "alt");
        var switches = result.Steps.Where(s => s.Action.Kind == TaggedActionKind.Switch && s.Action.Location == "Port")
            .Select(s => s.Action.To).ToArray();
        Assert.Equal(switches.Distinct(), switches);
        var replay = sim.Initial();
        foreach (var step in result.Steps) Assert.True(sim.TryApply(replay, step.Action, out replay, out var error), error);
    }

    [Fact] public void WarehouseLoadingKeepsTheFinalStackTogetherAfterPrefilling() {
        var r = new TaggedTransportRequest {
            ShipLimitLT = 26410, ShipOccupiedLT = 2411,
            Settings = new() { StartIsland = "Iliya", Ports = [new() { IslandId = "Iliya", WarehouseId = "Iliya", Enabled = true }] },
            Items = new() { ["slab"] = new("slab", "Slabs", 2, 400), ["book"] = new("book", "Books", 4, 1000),
                ["input"] = new("input", "Input", 2, 400), ["coin"] = new("coin", "Coins", -1, 0) },
            Warehouses = new() { ["Iliya"] = new() { ["input"] = 7, ["slab"] = 30, ["book"] = 20 } },
            Points = new() { ["Iliya"] = new(0, 0), ["A"] = new(100, 0), ["B"] = new(200, 0), ["C"] = new(300, 0) },
            Trades = [new("a", "A", "input", 1, "coin", 1, 7), new("b", "B", "slab", 1, "coin", 1, 30), new("c", "C", "book", 1, "coin", 1, 20)]
        };
        var alt = r.Settings.Carriers.Single(c => c.Id == "alt"); alt.LimitLT = 3463; alt.OccupiedLT = 120;
        var sim = new TaggedTransportSimulator(r);
        var result = new TaggedVoyageCompiler(sim, () => false).Compile(sim.Initial(), [new(0, 7), new(1, 30), new(2, 20)], "Iliya", stopAfterFirstExchange: true);
        Assert.NotNull(result);
        var loads = result.Steps.Where(s => s.Action is { Kind: TaggedActionKind.Transfer, To: "alt" }).ToArray();
        Assert.Equal(new[] { "slab", "book" }, loads.Select(s => s.Action.ItemId));
        Assert.Equal(new[] { 14, 20 }, loads.Select(s => s.Action.Quantity));
        var replay = sim.Initial();
        foreach (var step in result.Steps) Assert.True(sim.TryApply(replay, step.Action, out replay, out var error), error);
    }
    [Theory]
    [InlineData("main", 67)]
    [InlineData("alt", 67)]
    [InlineData("alt", 1)]
    public void PrefillOtherGoodsBeforeTheFinalStackAndReserveTheImmediateExchange(string owner, int slots) {
        var r = new TaggedTransportRequest {
            ShipLimitLT = 26410, ShipOccupiedLT = 2411,
            Settings = new() { StartIsland = "Start", Ports = [new() { IslandId = "Lema", Enabled = true }],
                InitialCargo = [new("ship", "slab", 30), new("ship", "book", 20), new("ship", "input", 7)] },
            Items = new() { ["slab"] = new("slab", "Slabs", 2, 400), ["book"] = new("book", "Boatman's Manual", 4, 1000),
                ["input"] = new("input", "Input", 2, 400), ["output"] = new("output", "Output", 3, 900) },
            Points = new() { ["Start"] = new(0, 0), ["Lema"] = new(100, 0), ["Island"] = new(200, 0) },
            Trades = [new("exchange", "Island", "input", 1, "output", 3, 7)]
        };
        foreach (var carrier in r.Settings.Carriers) carrier.Slots = 0;
        var c = r.Settings.Carriers.Single(c => c.Id == owner);
        c.Slots = slots; c.LimitLT = 3463; c.OccupiedLT = 120;
        var sim = new TaggedTransportSimulator(r);
        var result = new TaggedVoyageCompiler(sim, () => false).Compile(sim.Initial(), [new(0, 7)], "Lema",
            prepareAtWarehouse: false, stopAfterFirstExchange: true);
        Assert.NotNull(result);
        var transfers = result.Steps.Where(s => s.Action.Kind == TaggedActionKind.Transfer && s.Action.To == owner).ToArray();
        if (slots > 1) {
            Assert.Equal("slab", transfers[0].Action.ItemId);
            Assert.Equal(14, transfers[0].Action.Quantity);
            Assert.True((owner == "main" ? transfers[0].MainLT : transfers[0].AltLT) < 3463 * 1.7);
        }
        Assert.Equal("book", transfers[^1].Action.ItemId);
        Assert.Equal(20, transfers[^1].Action.Quantity);
        Assert.DoesNotContain(transfers, s => s.Action.ItemId == "input");
        var replay = sim.Initial();
        foreach (var step in result.Steps) Assert.True(sim.TryApply(replay, step.Action, out replay, out var error), error);
    }
}
