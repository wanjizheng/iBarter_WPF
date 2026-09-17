using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public class TaggedCompletionTests {
    [Fact] public void LegacyPujaraFixtureWithStackedLevelFiveGoodsIsRejected() {
        var saved = System.Text.Json.JsonSerializer.Deserialize<TaggedTransportSession>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "TestData", "tagged-pujara-completion.json")))!;
        saved = saved with { Plan = saved.Plan with { Fingerprint = saved.Request.Fingerprint() } };
        Assert.False(new TaggedTransportSimulator(saved.Request).Verify(saved.Plan, out _, out _));
    }
    [Fact] public void CompletionRetainsVerifiedRemainingOperationsWhenRepackingCannotFindAPlan() {
        var resultBefore = new TaggedTransportPlanner().Plan(Request(), TestContext.Current.CancellationToken);
        Assert.NotNull(resultBefore.Plan);
        var saved = new TaggedTransportSession(1, resultBefore.PlannedRequest!, resultBefore.Plan, 0);
        Assert.True(new TaggedTransportSimulator(saved.Request).Verify(saved.Plan, out var originalEnd, out var originalError), originalError);
        int index = Array.FindIndex(saved.Plan.Steps, s => s.Action.Kind == TaggedActionKind.Barter);
        Assert.True(index >= 0);
        var completed = saved.Request.Trades[saved.Plan.Steps[index].Action.TradeIndex];
        var remaining = TaggedCompletionReplanner.AtCompletedExchange(saved, index);
        Assert.Equal(completed.IslandId, remaining.Settings.StartIsland);
        Assert.Equal(saved.Request.Trades.Length - 1, remaining.Trades.Length);
        Assert.DoesNotContain(remaining.Trades, t => t.RowId == completed.RowId);
        var seed = TaggedCompletionReplanner.Seed(saved, index, remaining, TestContext.Current.CancellationToken);
        Assert.NotNull(seed);
        var sim = new TaggedTransportSimulator(remaining);
        var expectedActions = saved.Plan.Steps.Skip(index + 1).Select(s => s.Action.Kind == TaggedActionKind.Barter
            ? s.Action with { TradeIndex = Array.FindIndex(remaining.Trades, t => t.RowId == saved.Request.Trades[s.Action.TradeIndex].RowId) }
            : s.Action);
        Assert.Equal(expectedActions, seed.Steps.Select(s => s.Action));
        Assert.True(sim.Verify(seed, out var final, out var error), error);
        foreach (var cargo in originalEnd.Cargo)
            Assert.Equal(cargo.Value.OrderBy(i => i.Key), final.Cargo[cargo.Key].OrderBy(i => i.Key));
        var limited = remaining with { Settings = remaining.Settings with { SearchProfile = null, SearchSeconds = 1, MaxStates = 1 } };
        var result = new TaggedTransportPlanner().Plan(limited, TestContext.Current.CancellationToken, preferredTaggedPlan: seed);
        Assert.NotNull(result.Plan);
        Assert.True(new TaggedTransportSimulator(result.PlannedRequest!).Verify(result.Plan, out _, out error), error);
    }

    private static TaggedTransportRequest Request() => new() {
        ShipLimitLT = 8000,
        Settings = new() { Enabled = true, SearchSeconds = 1, MaxStates = 1,
            Ports = [new() { IslandId = "Velia", WarehouseId = "Velia", Enabled = true }] },
        Points = new() { ["Velia"] = new(0, 0), ["A"] = new(10000, 0), ["B"] = new(20000, 0), ["C"] = new(30000, 0) },
        Items = new() { ["a"] = new("a", "Input", 4, 1000), ["b"] = new("b", "Output", 5, 1000) },
        Warehouses = new() { ["Velia"] = new() { ["a"] = 3 } },
        Trades = [new("one", "A", "a", 1, "b", 1, 1), new("two", "B", "a", 1, "b", 1, 1), new("three", "C", "a", 1, "b", 1, 1)]
    };
    [Fact] public void CompletingMiddleExchangePreservesEarlierAndLaterTasksAndReplays() {
        var result = new TaggedTransportPlanner().Plan(Request(), TestContext.Current.CancellationToken);
        var session = new TaggedTransportSession(1, result.PlannedRequest!, result.Plan!, 0);
        int index = Array.FindIndex(session.Plan.Steps, s => s.Action.Kind == TaggedActionKind.Barter
            && session.Request.Trades[s.Action.TradeIndex].RowId == "two");
        var remaining = TaggedCompletionReplanner.AtCompletedExchange(session, index);
        Assert.Equal(new[] { "one", "three" }, remaining.Trades.Select(t => t.RowId));
        Assert.Equal(0, session.CompletedSteps);
        Assert.Equal(3, session.Request.Trades.Length);
        Assert.Equal("B", remaining.Settings.StartIsland);
        Assert.Equal(2, remaining.Settings.InitialCargo.Where(c => c.ItemId == "a").Sum(c => c.Quantity));
        Assert.Equal(1, remaining.Settings.InitialCargo.Where(c => c.ItemId == "b").Sum(c => c.Quantity));
        Assert.Equal(3, session.Request.Warehouses["Velia"]["a"]);
        var seed = TaggedCompletionReplanner.Seed(session, index, remaining, TestContext.Current.CancellationToken);
        Assert.NotNull(seed);
        var replanned = new TaggedTransportPlanner().Plan(remaining, TestContext.Current.CancellationToken, preferredTaggedPlan: seed);
        Assert.NotNull(replanned.Plan);
        Assert.True(new TaggedTransportSimulator(replanned.PlannedRequest!).Verify(replanned.Plan, out _, out _));
        Assert.DoesNotContain(replanned.Plan.Steps, s => s.Action.Location == "B");
        Assert.NotEqual("Velia", replanned.Plan.Steps[0].Action.Location);
    }
    [Fact] public void CompletedExchangeOutputRemainsOnShipForItsDependentExchange() {
        var r = Request(); r = r with { Trades = [r.Trades[0], r.Trades[1] with { InputId = "b" }] };
        var result = new TaggedTransportPlanner().Plan(r, TestContext.Current.CancellationToken);
        var session = new TaggedTransportSession(1, result.PlannedRequest!, result.Plan!, 0);
        int index = Array.FindIndex(session.Plan.Steps, s => s.Action.Kind == TaggedActionKind.Barter && s.Action.TradeIndex == 0);
        var remaining = TaggedCompletionReplanner.AtCompletedExchange(session, index);
        Assert.Equal(1, remaining.Settings.InitialCargo.Single(c => c.Container == "ship" && c.ItemId == "b").Quantity);
        var seed = TaggedCompletionReplanner.Seed(session, index, remaining, TestContext.Current.CancellationToken);
        Assert.NotNull(seed);
        Assert.True(new TaggedTransportSimulator(remaining).Verify(seed, out _, out _));
    }
    [Fact] public void CompletingOneSplitSegmentOnlySubtractsThatQuantity() {
        var r = Request(); r = r with { Trades = [r.Trades[0] with { Exchanges = 3 }] };
        var plan = new TaggedTransportPlan("", [new(new(TaggedActionKind.Barter, "A", Quantity: 1, TradeIndex: 0), 0, 0, 0, 0, 0)], 0, 0, 0, 0, "");
        var remaining = TaggedCompletionReplanner.Remaining(new(1, r, plan, 0), 0);
        Assert.Equal(2, Assert.Single(remaining.Trades).Exchanges);
        Assert.Equal(3, r.Trades[0].Exchanges);
    }
    [Fact] public void CompletionDoesNotInventAnOutputForDependentExchanges() {
        var r = Request(); r = r with { Trades = [r.Trades[0], r.Trades[1] with { InputId = "b" }] };
        var plan = new TaggedTransportPlan("", [new(new(TaggedActionKind.Barter, "A", Quantity: 1, TradeIndex: 0), 0, 0, 0, 0, 0)], 0, 0, 0, 0, "");
        var remaining = TaggedCompletionReplanner.Remaining(new(1, r, plan, 0), 0);
        Assert.Empty(remaining.Settings.InitialCargo);
        Assert.Null(new TaggedTransportPlanner().Plan(remaining, TestContext.Current.CancellationToken).Plan);
    }
    [Fact] public void BothDistanceRepresentationsProduceTheSameKilometres() {
        Assert.Equal(RouteDistanceDisplay.OrdinaryKilometres(18_341_955.4), RouteDistanceDisplay.TaggedKilometres(183_419.554), 8);
        Assert.Equal(1, RouteDistanceDisplay.OrdinaryKilometres(100_000));
        Assert.Equal(1, RouteDistanceDisplay.TaggedKilometres(1000));
    }
}
