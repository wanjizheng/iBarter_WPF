using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public sealed class RouteSearchIncumbentTests {
    [Theory]
    [InlineData(RouteOptimizationMode.Quick)]
    [InlineData(RouteOptimizationMode.Balanced)]
    [InlineData(RouteOptimizationMode.Deep)]
    public void ShortSearchRetainsBetterVerifiedTourInEveryOrdinaryMode(RouteOptimizationMode mode) {
        var items = Enumerable.Range(0, 4).ToDictionary(i => $"I{i}", i => new RouteItem($"I{i}", $"I{i}", 1, 100));
        items["OUT"] = new("OUT", "Output", -1, 0);
        RoutePoint[] points = [new(1, 0), new(1, 1), new(0, 2), new(3, 1)];
        var request = new AutomaticRoutePlanningRequest(Enumerable.Range(0, 4).Select(i =>
            new RouteBarterTask($"r{i}", $"P{i}", points[i], $"I{i}", 1, "OUT", 1)).ToArray(), items,
            [new("W", "W", new(0, 0), Enumerable.Range(0, 4).ToDictionary(i => $"I{i}", _ => 1))],
            0, 400, new(1, 0), "incumbent-regression");
        var state = RouteSimulationState.CreateInitial(request);
        state = RouteStateTransition.TryPickup(request, state, "W", Enumerable.Range(0, 4).Select(i => new RouteItemQuantity($"I{i}", 1)).ToArray()).State;
        foreach (int i in new[] { 0, 3, 1, 2 }) state = RouteStateTransition.TryBarter(request, state, i).State;
        state = WarehouseUnloadPlanner.TryCompleteRoute(request, state).State;
        var preferred = RoutePlanFactory.FromState(request, state, RoutePlanStatus.BestKnownWithinLimit, []);
        Assert.True(RoutePlanVerifier.Verify(request, preferred).Success);
        var profile = RouteOptimizationProfile.For(mode);
        var unseeded = new AutomaticRoutePlanner().Plan(request, profile, TestContext.Current.CancellationToken);
        Assert.True(preferred.Objective!.Value.CompareTo(unseeded.Objective!.Value) < 0);
        var retained = new AutomaticRoutePlanner().Plan(request, profile, preferred, TestContext.Current.CancellationToken);
        Assert.True(RoutePlanVerifier.Verify(request, retained).Success);
        Assert.True(retained.Objective!.Value.CompareTo(preferred.Objective.Value) <= 0);
    }

    [Fact] public void OrdinaryModeRejectsExchangeWhoseOutputExceedsShipLimit() {
        var request = RouteTestData.SingleTask(inputQuantity: 1, outputQuantity: 3, inputLevel: 4, outputLevel: 5, totalLT: 2000);
        var plan = new AutomaticRoutePlanner().Plan(request, TestContext.Current.CancellationToken);
        Assert.Equal(RoutePlanStatus.Infeasible, plan.Status);
        Assert.Empty(plan.Routes);
    }

    [Fact] public void ModeBudgetChangesCanReuseTourButPhysicalInputChangesCannot() {
        var request = RouteTestData.SingleTask();
        var plan = new AutomaticRoutePlanner().Plan(request, TestContext.Current.CancellationToken);
        AutomaticRoutePlanningRequest Changed(int limit) => new(request.Tasks, request.Items, request.Warehouses,
            request.ExtraLT, limit, new(1, 0), request.ConfigurationVersion, request.InitialOnBoard);
        var otherBudget = Changed(request.TotalLT);
        Assert.NotEqual(plan.InputFingerprint, RoutePlanFingerprint.Compute(otherBudget));
        var reused = RouteIncumbentReuse.ForSearchBudget(otherBudget, request, plan);
        Assert.NotNull(reused); Assert.True(RoutePlanVerifier.Verify(otherBudget, reused).Success);
        Assert.Null(RouteIncumbentReuse.ForSearchBudget(Changed(request.TotalLT + 1), request, plan));
        var tampered = new RoutePlan(plan.Status, plan.Routes, plan.Objective!.Value with { TotalDistance = 0 }, plan.Diagnostics, plan.InputFingerprint);
        Assert.Null(RouteIncumbentReuse.ForSearchBudget(otherBudget, request, tampered));
    }
}
