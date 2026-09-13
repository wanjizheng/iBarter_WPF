using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public sealed class RoutePlanVerifierTests {
    [Fact]
    public void Rejects_tampered_step_quantity_without_repairing_it() {
        var request = RouteTestData.SingleTask();
        var valid = new AutomaticRoutePlanner().Plan(request, TestContext.Current.CancellationToken);
        var route = valid.Routes.Single();
        var steps = route.Steps.ToArray();
        var pickup = (WarehousePickupStep)steps[0];
        steps[0] = new WarehousePickupStep(
            pickup.WarehouseId, pickup.IslandId, [new RouteItemQuantity("IN", 1)], pickup.Load);
        var tamperedRoute = new PlannedRoute(
            route.Number, route.StartWarehouseId, route.EndWarehouseId, steps,
            route.Distance, route.InitialLT, route.CurrentLT, route.PeakLT);
        var tampered = new RoutePlan(
            valid.Status, [tamperedRoute], valid.Objective, valid.Diagnostics, valid.InputFingerprint);

        var verification = RoutePlanVerifier.Verify(request, tampered);

        Assert.False(verification.Success);
        Assert.Equal("verification-mismatch", verification.Diagnostic?.Code);
        Assert.Null(verification.VerifiedPlan);
    }

    [Fact]
    public void Rejects_wrong_fingerprint_objective_and_incomplete_plan() {
        var request = RouteTestData.SingleTask();
        var valid = new AutomaticRoutePlanner().Plan(request, TestContext.Current.CancellationToken);
        Assert.False(RoutePlanVerifier.Verify(request,
            new RoutePlan(valid.Status, valid.Routes, valid.Objective, [], "wrong")).Success);
        var wrongObjective = valid.Objective!.Value with {
            TotalDistance = valid.Objective.Value.TotalDistance + 1,
        };
        Assert.False(RoutePlanVerifier.Verify(request,
            new RoutePlan(valid.Status, valid.Routes, wrongObjective, [], valid.InputFingerprint)).Success);
        Assert.False(RoutePlanVerifier.Verify(request,
            new RoutePlan(RoutePlanStatus.Optimal, [], new RoutePlanObjective(0, 0, 0, 0, ""),
                [], valid.InputFingerprint)).Success);
    }
}
