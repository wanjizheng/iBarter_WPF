using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public class TaggedTransportTests {
    private static TaggedTransportRequest Request() => new() {
        ShipLimitLT = 2000,
        Items = new() { ["a"] = new("a", "A", 4, 1000), ["b"] = new("b", "B", 5, 1000), ["t7"] = new("t7", "T7", 7, 2000) },
        Points = new() { ["Velia"] = new(0, 0), ["Kuit"] = new(10000, 0), ["Island"] = new(20000, 0) },
        Warehouses = new() { ["Velia"] = new() { ["a"] = 4 } },
        Trades = [new("row", "Island", "a", 1, "b", 1, 4)],
        Settings = new() { MaxStates = 8000, SearchSeconds = 5, Ports = [
            new() { IslandId = "Velia", Enabled = true, WarehouseId = "Velia" },
            new() { IslandId = "Kuit", Enabled = true } ] }
    };

    [Fact] public void ShipLoadingAndBarterOutputHaveDifferentLimits() {
        var r = Request() with { Trades = [new("row", "Velia", "a", 1, "b", 2, 1)] };
        var sim = new TaggedTransportSimulator(r); var s = sim.Initial();
        Assert.False(sim.TryApply(s, new(TaggedActionKind.Transfer, "Velia", "warehouse:Velia", "ship", "a", 3), out _, out _));
        Assert.True(sim.TryApply(s, new(TaggedActionKind.Transfer, "Velia", "warehouse:Velia", "ship", "a", 2), out s, out _));
        Assert.True(sim.TryApply(s, new(TaggedActionKind.Barter, "Velia", Quantity: 1, TradeIndex: 0), out s, out _));
        Assert.Equal(3000, sim.Weight(s, "ship"));
        Assert.False(sim.TryApply(s, new(TaggedActionKind.Transfer, "Velia", "warehouse:Velia", "ship", "a", 1), out _, out _));
    }

    [Fact] public void NoWharfAndWrongCharacterCannotTransfer() {
        var r = Request(); var sim = new TaggedTransportSimulator(r); var s = sim.Initial();
        Assert.True(sim.TryApply(s, new(TaggedActionKind.Transfer, "Velia", "warehouse:Velia", "ship", "a", 2), out s, out _));
        Assert.False(sim.TryApply(s, new(TaggedActionKind.Transfer, "Velia", "ship", "alt", "a", 1), out _, out _));
        Assert.True(sim.TryApply(s, new(TaggedActionKind.Sail, "Island"), out s, out _));
        Assert.False(sim.TryApply(s, new(TaggedActionKind.Transfer, "Island", "ship", "main", "a", 1), out _, out _));
    }

    [Fact] public void CharacterSingleStackDoesNotMeanInfiniteSubsequentReceives() {
        var sim = new TaggedTransportSimulator(Request()); var s = sim.Initial();
        Assert.False(sim.TryApply(s, new(TaggedActionKind.Transfer, "Velia", "warehouse:Velia", "main", "a", 4), out _, out _));
        Assert.True(sim.TryApply(s, new(TaggedActionKind.SummonElephant, "Velia", "main", "main-elephant"), out s, out _));
        Assert.True(sim.TryApply(s, new(TaggedActionKind.StackAtWarehouse, "Velia", "warehouse:Velia", "main-elephant", "a", 4), out s, out _));
        Assert.True(sim.TryApply(s, new(TaggedActionKind.Transfer, "Velia", "main-elephant", "main", "a", 4), out s, out _));
        Assert.False(sim.TryApply(s, new(TaggedActionKind.Transfer, "Velia", "ship", "main", "b", 1), out _, out _));
        Assert.False(sim.TryApply(s, new(TaggedActionKind.Transfer, "Velia", "main", "alt", "a", 4), out _, out _));
    }

    [Fact] public void NonStackableGoodsAreReceivedOneAtATime() {
        var r = Request(); r.Settings.InitialCargo = [new("ship", "t7", 2)];
        var sim = new TaggedTransportSimulator(r); var s = sim.Initial();
        Assert.False(sim.TryApply(s, new(TaggedActionKind.Transfer, "Velia", "ship", "main", "t7", 2), out _, out _));
        Assert.True(sim.TryApply(s, new(TaggedActionKind.Transfer, "Velia", "ship", "main", "t7", 1), out s, out _));
        Assert.False(sim.TryApply(s, new(TaggedActionKind.Transfer, "Velia", "ship", "main", "t7", 1), out _, out _));
    }

    [Fact] public void WhistleMovesElephantWithCargoAndRequiresItsOwner() {
        var r = Request(); var sim = new TaggedTransportSimulator(r); var s = sim.Initial();
        Assert.False(sim.TryApply(s, new(TaggedActionKind.SummonElephant, "Velia", "main", "alt-elephant"), out _, out _));
        Assert.True(sim.TryApply(s, new(TaggedActionKind.SummonElephant, "Velia", "main", "main-elephant"), out s, out _));
        Assert.True(sim.TryApply(s, new(TaggedActionKind.StackAtWarehouse, "Velia", "warehouse:Velia", "main-elephant", "a", 4), out s, out _));
        Assert.Equal(80 + r.Settings.ElephantSummonSeconds, s.Seconds);
        Assert.True(sim.TryApply(s, new(TaggedActionKind.Sail, "Kuit"), out s, out _));
        Assert.False(sim.TryApply(s, new(TaggedActionKind.Transfer, "Kuit", "main-elephant", "main", "a", 4), out _, out _));
        Assert.True(sim.TryApply(s, new(TaggedActionKind.SummonElephant, "Kuit", "main", "main-elephant"), out s, out _));
        Assert.True(sim.TryApply(s, new(TaggedActionKind.Transfer, "Kuit", "main-elephant", "main", "a", 4), out s, out _));
        Assert.Equal(4, sim.Count(s, "main", "a"));
        Assert.False(sim.TryApply(s, new(TaggedActionKind.StackAtWarehouse, "Kuit", "warehouse:Velia", "main-elephant", "a", 1), out _, out _));
    }

    [Fact] public void PlannerCompletesAllExchangesAndUnloadsEveryCarrier() {
        var r = Request(); var result = new TaggedTransportPlanner().Plan(r, TestContext.Current.CancellationToken);
        Assert.NotNull(result.Plan);
        Assert.True(new TaggedTransportSimulator(r).Verify(result.Plan!, out var state, out var error), error);
        Assert.Equal(0, state.Remaining[0]);
        Assert.Equal(4, state.Cargo["warehouse:Velia"].GetValueOrDefault("b"));
    }

    [Fact] public void SaveRestoresExactProgressAndRejectsTampering() {
        var r = Request(); var plan = new TaggedTransportPlanner().Plan(r, TestContext.Current.CancellationToken).Plan!;
        Assert.NotNull(plan);
        var session = new TaggedTransportSession(1, r, plan, 2);
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try {
            TaggedTransportStorage.Save(file, session);
            var loaded = TaggedTransportStorage.Load<TaggedTransportSession>(file)!;
            Assert.Equal(session.Current().Seconds, loaded.Current().Seconds);
            var bad = plan with { TotalSeconds = plan.TotalSeconds + 10 };
            Assert.False(new TaggedTransportSimulator(r).Verify(bad, out _, out _));
            var remaining = session.RemainingRequest();
            Assert.Equal(session.Current().Location, remaining.Settings.StartIsland);
        }
        finally { File.Delete(file); }
    }

    [Fact] public void TimeAssumptionsChangeFingerprintAndOverweightTravelCost() {
        var r = Request(); r.Settings.InitialCargo = [new("ship", "b", 3)];
        var sim = new TaggedTransportSimulator(r); var start = sim.Initial();
        Assert.True(sim.TryApply(start, new(TaggedActionKind.Sail, "Kuit"), out var slow, out _));
        string fingerprint = r.Fingerprint(); r.Settings.OverloadedMetersPerSecond *= 2;
        Assert.NotEqual(fingerprint, r.Fingerprint());
        Assert.True(sim.TryApply(start, new(TaggedActionKind.Sail, "Kuit"), out var faster, out _));
        Assert.True(faster.Seconds < slow.Seconds);
        r.Settings.AllowOverloadedSailing = false;
        Assert.False(sim.TryApply(start, new(TaggedActionKind.Sail, "Kuit"), out _, out _));
    }

    [Fact] public void MidrouteFullShipRequiresSwitchBeforeReloading() {
        var r = Request();
        r.Settings.StartIsland = "Kuit";
        r.Settings.Carriers[0].LimitLT = 1000;
        r.Settings.InitialCargo = [new("ship", "b", 2), new("main", "a", 2)];
        var sim = new TaggedTransportSimulator(r); var s = sim.Initial();
        Assert.False(sim.TryApply(s, new(TaggedActionKind.Transfer, "Kuit", "main", "ship", "a", 2), out _, out _));
        Assert.False(sim.TryApply(s, new(TaggedActionKind.Transfer, "Kuit", "ship", "main", "b", 2), out _, out _));
        Assert.True(sim.TryApply(s, new(TaggedActionKind.Switch, "Kuit", "main", "alt"), out s, out _));
        Assert.True(sim.TryApply(s, new(TaggedActionKind.Transfer, "Kuit", "ship", "alt", "b", 1), out s, out _));
        Assert.True(sim.TryApply(s, new(TaggedActionKind.Transfer, "Kuit", "ship", "alt", "b", 1), out s, out _));
        Assert.True(sim.TryApply(s, new(TaggedActionKind.Switch, "Kuit", "alt", "main"), out s, out _));
        Assert.True(sim.TryApply(s, new(TaggedActionKind.Transfer, "Kuit", "main", "ship", "a", 2), out s, out _));
    }

    [Fact] public void PlannerUsesTagOrWhistleForDistantWharfInsteadOfExtraWarehouseTrip() {
        var r = Request();
        r.Points["Kuit"] = new(1000000, 0); r.Points["Island"] = new(1000100, 0);
        r.Settings.Carriers[0].LimitLT = 1000;
        r.Settings.Carriers[1].LimitLT = 1000;
        r.Settings.MaxStates = 30000; r.Settings.SearchSeconds = 10;
        var result = new TaggedTransportPlanner().Plan(r, TestContext.Current.CancellationToken);
        Assert.NotNull(result.Plan);
        Assert.Contains(result.Plan.Steps, x => x.Action.Kind is TaggedActionKind.Switch or TaggedActionKind.SummonElephant);
        Assert.Contains(result.Plan.Steps, x => x.Action.Location == "Kuit" && x.Action.Kind == TaggedActionKind.Transfer);
        Assert.True(result.Plan.Distance < 30000, $"Distance {result.Plan.Distance}");
    }

    [Fact] public void SettlementKeepsOriginalStockAcrossReplanning() {
        var r = Request(); var plan = new TaggedTransportPlanner().Plan(r, TestContext.Current.CancellationToken).Plan!;
        var first = new TaggedTransportSession(1, r, plan, 1);
        var remaining = first.RemainingRequest();
        var replacement = new TaggedTransportPlanner().Plan(remaining, TestContext.Current.CancellationToken).Plan!;
        Assert.NotNull(replacement);
        var final = new TaggedTransportSession(1, remaining, replacement, replacement.Steps.Length, r);
        var changes = final.SettlementChanges();
        Assert.Contains(changes, x => x.ItemId == "a" && x.Before == 4 && x.After == 0);
        Assert.Contains(changes, x => x.ItemId == "b" && x.Before == 0 && x.After == 4);
        Assert.Throws<InvalidOperationException>(() => first.SettlementChanges());
    }

    [Fact] public void SearchHandlesManyTasksWithoutDroppingSelectedWork() {
        var r = Request();
        r.Warehouses["Velia"]["a"] = 20;
        r = r with { Trades = Enumerable.Range(0, 20).Select(i => new TaggedTrade($"row{i}", "Island", "a", 1, "b", 1, 1)).ToArray() };
        r.Settings.SearchSeconds = 1; r.Settings.MaxStates = 3000;
        var result = new TaggedTransportPlanner().Plan(r, TestContext.Current.CancellationToken);
        Assert.NotNull(result.Plan);
        Assert.Equal(20, result.Plan.Steps.Where(x => x.Action.Kind == TaggedActionKind.Barter).Sum(x => x.Action.Quantity));
        Assert.True(result.Plan.Distance <= result.Plan.ShipOnlyDistance);
    }

    [Fact] public void SettingsRoundTripRestoresWhistleAndCarrierValuesWithoutLocationId() {
        var settings = new TaggedTransportSettings { Enabled = true, ElephantSummonSeconds = 7.5 };
        settings.Carriers[0].LimitLT = 2345.5; settings.Carriers[1].Slots = 42;
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try {
            TaggedTransportStorage.Save(path, settings);
            var restored = TaggedTransportStorage.Load<TaggedTransportSettings>(path)!;
            Assert.True(restored.Enabled);
            Assert.Equal(7.5, restored.ElephantSummonSeconds);
            Assert.Equal(2345.5, restored.Carriers[0].LimitLT);
            Assert.Equal(42, restored.Carriers[1].Slots);
            Assert.DoesNotContain("\"Location\"", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact] public void LegacySessionMigrationPreservesCargoAndProgress() {
        var r = Request();
        var plan = new TaggedTransportPlanner().Plan(r, TestContext.Current.CancellationToken).Plan!;
        var old = new TaggedTransportSession(1, r, plan, 1);
        var json = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(old))!;
        var config = json["Request"]!["Settings"]!.AsObject();
        config.Remove("ElephantSummonSeconds");
        foreach (var carrier in config["Carriers"]!.AsArray()) carrier!["Location"] = "Velia";
        string fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(json["Request"]!.ToJsonString())));
        json["Plan"]!["Fingerprint"] = fingerprint;
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try {
            File.WriteAllText(path, json.ToJsonString());
            var restored = TaggedTransportStorage.Load<TaggedTransportSession>(path)!;
            Assert.Equal(old.Current().Cargo["ship"], restored.Current().Cargo["ship"]);
            Assert.Equal(old.CompletedSteps, restored.CompletedSteps);
            Assert.Equal(restored.Request.Fingerprint(), restored.Plan.Fingerprint);
        }
        finally { File.Delete(path); }
    }
}
