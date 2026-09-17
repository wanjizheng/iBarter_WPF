using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;
public class TaggedTransportBaselineTests {
    private static TaggedTransportRequest Cluster() => new() {
        ShipLimitLT = 6000,
        Settings = new() { MaxStates = 1, SearchSeconds = 1, Ports = [new() { IslandId = "Velia", WarehouseId = "Velia", Enabled = true }] },
        Points = new() { ["Velia"] = new(0, 0), ["A"] = new(100000, 0), ["B"] = new(110000, 0), ["C"] = new(120000, 0) },
        Items = new() { ["in"] = new("in", "Input", 4, 1000), ["out"] = new("out", "Output", 5, 1000) },
        Warehouses = new() { ["Velia"] = new() { ["in"] = 3 } },
        Trades = [new("a", "A", "in", 1, "out", 1, 1), new("b", "B", "in", 1, "out", 1, 1), new("c", "C", "in", 1, "out", 1, 1)]
    };

    [Fact] public void TinyTagSearchKeepsMultiIslandShipRoute() {
        var request = Cluster();
        var result = new TaggedTransportPlanner().Plan(request, TestContext.Current.CancellationToken);
        Assert.NotNull(result.Plan);
        Assert.Single(TaggedTransportRoutes.Build(request, result.Plan));
        Assert.Equal(3, result.Plan.Steps.Count(s => s.Action.Kind == TaggedActionKind.Barter));
        Assert.True(new TaggedTransportSimulator(request).Verify(result.Plan, out _, out _));
    }

    private static RoutePlan OrdinaryPlan(TaggedTransportRequest r) {
        var request = new AutomaticRoutePlanningRequest(r.Trades.Select(t => new RouteBarterTask(t.RowId, t.IslandId,
            r.Points[t.IslandId], t.InputId, t.InputPerExchange * t.Exchanges, t.OutputId, t.OutputPerExchange * t.Exchanges)).ToArray(),
            r.Items, [new("Velia", "Velia", r.Points["Velia"], r.Warehouses["Velia"])], 0, (int)r.ShipLimitLT,
            new(250000, 150), "baseline-test");
        return new AutomaticRoutePlanner().Plan(request, RouteOptimizationProfile.For(RouteOptimizationMode.Quick), TestContext.Current.CancellationToken);
    }

    [Fact] public void ExistingOrdinaryPlanIsRetainedWhenTagBudgetIsTiny() {
        var request = Cluster(); var ordinary = OrdinaryPlan(request);
        var sim = new TaggedTransportSimulator(request);
        var baseline = TaggedShipRouteBaseline.Replay(sim, sim.Initial(), ordinary);
        Assert.NotNull(baseline);
        var result = new TaggedTransportPlanner().Plan(request, TestContext.Current.CancellationToken, ordinary);
        Assert.True(result.Search!.OrdinaryReferenceAccepted);
        Assert.NotNull(result.Plan);
        Assert.True(result.Plan.Distance <= baseline.Distance);
        Assert.True(TaggedTransportRoutes.Build(request, result.Plan).Length <= ordinary.Routes.Count);
    }

    [Fact] public void OrdinaryPlanMustMatchTasksStockAndPortAccess() {
        var request = Cluster(); var ordinary = OrdinaryPlan(request);
        var more = request with { Trades = request.Trades.Select(t => t with { Exchanges = 2 }).ToArray() };
        var sim = new TaggedTransportSimulator(more);
        Assert.Null(TaggedShipRouteBaseline.Replay(sim, sim.Initial(), ordinary));
        request.Warehouses["Velia"]["in"] = 0;
        sim = new TaggedTransportSimulator(request);
        Assert.Null(TaggedShipRouteBaseline.Replay(sim, sim.Initial(), ordinary));
        request.Warehouses["Velia"]["in"] = 3;
        request.Settings.Ports[0].Enabled = false;
        sim = new TaggedTransportSimulator(request);
        Assert.Null(TaggedShipRouteBaseline.Replay(sim, sim.Initial(), ordinary));
    }

    [Fact] public void OrdinaryBaselineIncludesRealDepartureTravelWithoutAnExtraEmptyRoute() {
        var request = Cluster(); var ordinary = OrdinaryPlan(request);
        request.Settings.StartIsland = "Start"; request.Points["Start"] = new(-100000, 0);
        var sim = new TaggedTransportSimulator(request);
        var baseline = TaggedShipRouteBaseline.Replay(sim, sim.Initial(), ordinary);
        Assert.NotNull(baseline);
        Assert.Equal("Start", baseline.Steps[0].Action.From);
        Assert.Equal("Velia", baseline.Steps[0].Action.Location);
        var plan = new TaggedTransportPlan(request.Fingerprint(), baseline.Steps.ToArray(), baseline.Seconds,
            baseline.SailingSeconds, baseline.Seconds - baseline.SailingSeconds, baseline.Distance, "BestKnownWithinLimit");
        Assert.Single(TaggedTransportRoutes.Build(request, plan));
    }
}
