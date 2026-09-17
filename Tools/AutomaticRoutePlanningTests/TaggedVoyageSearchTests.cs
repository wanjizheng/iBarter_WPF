using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public sealed class TaggedVoyageSearchTests {
    private static TaggedTransportRequest Request(bool wharf = true) => new() {
        ShipLimitLT = 2000,
        Items = new() { ["a"] = new("a", "First input", 4, 1000), ["c"] = new("c", "Later input", 4, 1000),
            ["b"] = new("b", "Output", 5, 1000), ["d"] = new("d", "Final output", 5, 1000) },
        Points = new() { ["Velia"] = new(0, 0), ["One"] = new(100000, 0), ["Wharf"] = new(110000, 0), ["Two"] = new(120000, 0) },
        Warehouses = new() { ["Velia"] = new() { ["a"] = 2, ["c"] = 2 } },
        Trades = [new("first", "One", "a", 1, "b", 1, 2), new("later", "Two", "c", 1, "d", 1, 2)],
        Settings = new() { AllowOverloadedSailing = false, MaxStates = 2000, SearchSeconds = 5,
            Ports = [new() { IslandId = "Velia", WarehouseId = "Velia", Enabled = true }, new() { IslandId = "Wharf", Enabled = wharf }] }
    };

    [Fact] public void WholeVoyageSearchPreloadsLaterInputAndMergesTwoShipTrips() {
        var r = Request(); var result = new TaggedTransportPlanner().Plan(r, TestContext.Current.CancellationToken);
        Assert.NotNull(result.Plan); var plan = result.Plan;
        Assert.True(new TaggedTransportSimulator(result.PlannedRequest!).Verify(plan, out var final, out var error), error);
        Assert.Single(TaggedTransportRoutes.Build(result.PlannedRequest!, plan));
        Assert.Equal(2400, plan.Distance, 6); // Out to the farthest stop and back: the geometric lower bound.
        Assert.True(plan.Distance < plan.ShipOnlyDistance);
        int firstSail = Array.FindIndex(plan.Steps, s => s.Action.Kind == TaggedActionKind.Sail);
        Assert.Contains(plan.Steps.Take(firstSail), s => s.Action.ItemId == "c" && s.Action.To is "main" or "alt");
        Assert.Contains(plan.Steps, s => s.Action.Location == "Wharf" && s.Action.Kind == TaggedActionKind.Transfer);
        Assert.Contains(plan.Steps, s => s.Action.Location == "Wharf" && s.Action.From is "main" or "alt" && s.Action.To == "ship");
        Assert.All(plan.Steps.Where(s => s.Action.Kind == TaggedActionKind.Sail), s => Assert.True(s.ShipLT <= r.ShipLimitLT));
        Assert.All(final.Remaining, n => Assert.Equal(0, n));
        Assert.Equal(2, final.Cargo["warehouse:Velia"]["b"]); Assert.Equal(2, final.Cargo["warehouse:Velia"]["d"]);
    }

    [Fact] public void DisabledWharfCannotBeUsedToPretendThatCarriedInputIsOnShip() {
        var r = Request(false); var result = new TaggedTransportPlanner().Plan(r, TestContext.Current.CancellationToken);
        Assert.NotNull(result.Plan);
        Assert.Equal(2, TaggedTransportRoutes.Build(result.PlannedRequest!, result.Plan).Length);
        Assert.DoesNotContain(result.Plan.Steps, s => s.Action.Location == "Wharf");
        Assert.True(new TaggedTransportSimulator(result.PlannedRequest!).Verify(result.Plan, out _, out _));
    }

    [Fact] public void FullCharactersDoNotGivePlannerFictitiousCargoSpace() {
        var r = Request();
        foreach (var c in r.Settings.Carriers) c.OccupiedLT = c.LimitLT * r.Settings.CharacterReceiveRatio;
        var result = new TaggedTransportPlanner().Plan(r, TestContext.Current.CancellationToken);
        Assert.NotNull(result.Plan);
        Assert.Equal(2, TaggedTransportRoutes.Build(result.PlannedRequest!, result.Plan).Length);
        Assert.True(new TaggedTransportSimulator(result.PlannedRequest!).Verify(result.Plan, out _, out _));
    }

    [Fact] public void CompilerAllowsTaggedBarterOverweightButNeverUsesDisabledWharfs() {
        var r = Request(false) with { Trades = [new("heavy", "One", "a", 1, "b", 3, 1)] };
        r.Settings.AllowOverloadedSailing = true;
        var sim = new TaggedTransportSimulator(r);
        var state = new TaggedVoyageCompiler(sim, () => false).Compile(sim.Initial(), [new(0, 1)], "Velia", 3);
        Assert.NotNull(state); Assert.True(sim.Complete(state));
        Assert.Contains(state.Steps, s => s.Action.Kind == TaggedActionKind.Sail && s.ShipLT > r.ShipLimitLT);
        r.Settings.AllowOverloadedSailing = false;
        sim = new TaggedTransportSimulator(r);
        Assert.Null(new TaggedVoyageCompiler(sim, () => false).Compile(sim.Initial(), [new(0, 1)], "Velia", 3));
    }

    [Fact] public void TaggedSearchRecombinesQuantitiesSplitByNormalOutputLimit() {
        var r = Request(false) with { Trades = [new("bulk", "One", "a", 1, "b", 1, 3)] };
        r.Items["a"] = new("a", "Input", 3, 500); r.Warehouses["Velia"]["a"] = 3;
        r.Settings.AllowOverloadedSailing = true;
        var result = new TaggedTransportPlanner().Plan(r, TestContext.Current.CancellationToken);
        Assert.NotNull(result.Plan);
        Assert.Single(TaggedTransportRoutes.Build(result.PlannedRequest!, result.Plan));
        Assert.Equal(3, Assert.Single(result.Plan.Steps, s => s.Action.Kind == TaggedActionKind.Barter).Action.Quantity);
        Assert.True(result.Plan.Distance < result.Plan.ShipOnlyDistance);
        Assert.Contains(result.Plan.Steps, s => s.Action.Kind == TaggedActionKind.Sail && s.ShipLT == 3000);
        Assert.True(new TaggedTransportSimulator(result.PlannedRequest!).Verify(result.Plan, out _, out _));
    }

    [Fact] public void TaggedSearchCanStartWithoutAnyFeasibleOrdinarySeed() {
        var r = Request(false) with { Trades = [new("heavy", "One", "a", 1, "b", 3, 1)] };
        r.Settings.AllowOverloadedSailing = true;
        var result = new TaggedTransportPlanner().Plan(r, TestContext.Current.CancellationToken);
        Assert.NotNull(result.Plan); Assert.Null(result.Plan.ShipOnlyDistance);
        Assert.True(new TaggedTransportSimulator(result.PlannedRequest!).Verify(result.Plan, out _, out _));
    }

    [Fact] public void RerunRetainsVerifiedTaggedTourAndRejectsItWhenWharfCloses() {
        var r = Request(); var sim = new TaggedTransportSimulator(r);
        var state = new TaggedVoyageCompiler(sim, () => false).Compile(sim.Initial(), [new(0, 2), new(1, 2)], "Velia");
        Assert.NotNull(state); Assert.True(sim.Complete(state));
        var preferred = new TaggedTransportPlan(r.Fingerprint(), state.Steps.ToArray(), state.Seconds, state.SailingSeconds,
            state.Seconds - state.SailingSeconds, state.Distance, "BestKnownWithinLimit");
        r.Settings.MaxStates = 1;
        var result = new TaggedTransportPlanner().Plan(r, TestContext.Current.CancellationToken, preferredTaggedPlan: preferred);
        Assert.NotNull(result.Plan); Assert.True(result.Plan.Distance <= preferred.Distance);
        r.Settings.Ports[1].Enabled = false;
        result = new TaggedTransportPlanner().Plan(r, TestContext.Current.CancellationToken, preferredTaggedPlan: preferred);
        Assert.NotNull(result.Plan); Assert.Equal(2, TaggedTransportRoutes.Build(result.PlannedRequest!, result.Plan).Length);
        Assert.True(new TaggedTransportSimulator(result.PlannedRequest!).Verify(result.Plan, out _, out _));
    }
}
