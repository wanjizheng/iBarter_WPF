using System.Diagnostics;
using iBarter.Routing;
using Xunit;
namespace AutomaticRoutePlanningTests;
public class TaggedIntegrationTests {
    private static TaggedTransportRequest Request() => new() {
        ShipLimitLT = 2000,
        Settings = new() { SearchSeconds = 1, MaxStates = 1, Ports = [new() { IslandId = "Iliya", WarehouseId = "Iliya", Enabled = true }] },
        Points = new() { ["Velia"] = new(0, 0), ["Iliya"] = new(100000, 0), ["Island"] = new(101000, 0) },
        Items = new() { ["a"] = new("a", "Input", 4, 1000), ["b"] = new("b", "Output", 5, 1000) },
        Warehouses = new() { ["Iliya"] = new() { ["a"] = 1 } },
        Trades = [new("row", "Island", "a", 1, "b", 1, 1)]
    };
    [Fact] public void EmptyDepartureStartsAtLoadingWarehouseAndFixedDepartureIsRespected() {
        var r = Request();
        var auto = new TaggedTransportPlanner().Plan(r, TestContext.Current.CancellationToken);
        Assert.NotNull(auto.Plan); Assert.Equal("Iliya", auto.PlannedRequest!.Settings.StartIsland);
        Assert.Equal(TaggedActionKind.Transfer, auto.Plan.Steps[0].Action.Kind);
        Assert.True(new TaggedTransportSimulator(auto.PlannedRequest).Verify(auto.Plan, out _, out _));
        r.Settings.StartFromSelectedLocation = true;
        var fixedPlan = new TaggedTransportPlanner().Plan(r, TestContext.Current.CancellationToken);
        Assert.NotNull(fixedPlan.Plan); Assert.Equal("Velia", fixedPlan.Plan.Steps[0].Action.From);
        Assert.True(fixedPlan.Plan.Distance > auto.Plan.Distance);
    }
    [Fact] public void CustomSearchUsesSharedDeadlineInsteadOfLegacyStateCap() {
        var r = Request();
        r.Settings.SearchProfile = RouteOptimizationProfile.For(RouteOptimizationMode.Custom) with { TotalTarget = TimeSpan.FromSeconds(1.2), MaxBeamParents = 2 };
        var watch = Stopwatch.StartNew();
        var result = new TaggedTransportPlanner().Plan(r, TestContext.Current.CancellationToken);
        Assert.NotNull(result.Plan); Assert.True(watch.Elapsed.TotalSeconds >= 1.15);
    }
    [Fact] public void TenMinuteProfileReachesSearchAndStopRetainsVerifiedBest() {
        var r = Request(); r.Settings.SearchProfile = RouteOptimizationProfile.For(RouteOptimizationMode.Custom, TimeSpan.FromMinutes(10));
        using var cancel = new CancellationTokenSource(); double budget = 0;
        var result = new TaggedTransportPlanner().Plan(r, cancel.Token, progress: p => { budget = p.BudgetSeconds; cancel.Cancel(); });
        Assert.Equal(600, budget); Assert.NotNull(result.Plan);
        Assert.True(new TaggedTransportSimulator(result.PlannedRequest!).Verify(result.Plan, out _, out _));
    }
    [Fact] public void TaggedMapUsesNormalLabelsWithStableStepIdentityAndCompletionFiltering() {
        var result = new TaggedTransportPlanner().Plan(Request(), TestContext.Current.CancellationToken);
        var session = new TaggedTransportSession(1, result.PlannedRequest!, result.Plan!, 0);
        var labels = RouteStepLabelPlanner.PlanLabels(TaggedRoutePresentation.Build(session), false, 1, new Dictionary<string, string>());
        var label = Assert.Single(labels); Assert.Contains("× 1", label.DisplayText);
        Assert.Equal("row", RouteTaskIdentity.PlannerRowId(label.BarterRowId!));
        Assert.Empty(RouteStepLabelPlanner.PlanLabels(TaggedRoutePresentation.Build(session), false, 2, new Dictionary<string, string>()));
        session = session with { CompletedSteps = label.StepIndex + 1 };
        Assert.Empty(RouteStepLabelPlanner.PlanLabels(TaggedRoutePresentation.Build(session), true, null, new Dictionary<string, string>()));
    }
    [Fact] public void SearchBranchesPreserveParentAndSiblingInventories() {
        var request = Request(); request.Settings.StartIsland = "Iliya";
        var sim = new TaggedTransportSimulator(request); var root = sim.Initial();
        Assert.True(sim.TryApply(root, new(TaggedActionKind.Transfer, "Iliya", "warehouse:Iliya", "ship", "a", 1), out var loaded, out _));
        Assert.True(sim.TryApply(root, new(TaggedActionKind.Switch, "Iliya", "main", "alt"), out var sibling, out _));
        Assert.True(sim.TryApply(loaded, new(TaggedActionKind.Sail, "Island", "Iliya"), out var sailed, out _));
        Assert.True(sim.TryApply(sailed, new(TaggedActionKind.Barter, "Island", Quantity: 1, TradeIndex: 0), out var traded, out _));
        Assert.Equal(1, sim.Count(root, "warehouse:Iliya", "a"));
        Assert.Equal(1, sim.Count(sibling, "warehouse:Iliya", "a"));
        Assert.Equal(0, sim.Count(root, "ship", "a"));
        Assert.Equal(1, sim.Count(loaded, "ship", "a"));
        Assert.Equal(1, sim.Count(sailed, "ship", "a"));
        Assert.Equal(1, sim.Count(traded, "ship", "b"));
        Assert.Equal(1, loaded.Remaining[0]); Assert.Equal(0, traded.Remaining[0]);
        Assert.Empty(root.Steps); Assert.Single(loaded.Steps);
    }
}
