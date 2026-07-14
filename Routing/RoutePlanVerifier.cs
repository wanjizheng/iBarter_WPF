namespace iBarter.Routing;

public sealed record RouteVerificationResult(
    bool Success,
    RouteDiagnostic? Diagnostic,
    RoutePlan? VerifiedPlan);

public static class RoutePlanVerifier {
    public static RouteVerificationResult Verify(
        AutomaticRoutePlanningRequest request,
        RoutePlan plan) {
        try {
            if (!StringComparer.Ordinal.Equals(plan.InputFingerprint, RoutePlanFingerprint.Compute(request)))
                return Mismatch("fingerprint");
            var state = RouteSimulationState.CreateInitial(request);
            int expectedRouteNumber = 1;
            foreach (var route in plan.Routes) {
                if (route.Number != expectedRouteNumber++) return Mismatch("route-number");
                int finishedBefore = state.FinishedRoutes.Count;
                for (int stepIndex = 0; stepIndex < route.Steps.Count; stepIndex++) {
                    var expectedStep = route.Steps[stepIndex];
                    RouteTransitionResult actual = expectedStep switch {
                        WarehousePickupStep pickup => RouteStateTransition.TryPickup(
                            request, state, pickup.WarehouseId, pickup.Items),
                        BarterStep barter => ReplayBarter(request, state, barter.RowId),
                        WarehouseUnloadStep unload => RouteStateTransition.TryUnload(
                            request, state, unload.WarehouseId, unload.Items,
                            finishRoute: !route.Steps.Skip(stepIndex + 1).OfType<WarehouseUnloadStep>().Any()),
                        _ => new RouteTransitionResult(false, state, null,
                            new RouteDiagnostic("verification-mismatch", Detail: "step-type")),
                    };
                    if (!actual.Success || actual.Step is null || !StepEquals(expectedStep, actual.Step))
                        return Mismatch(expectedStep is BarterStep b ? b.RowId : expectedStep.IslandId);
                    state = actual.State;
                }
                if (state.FinishedRoutes.Count != finishedBefore + 1)
                    return Mismatch("route-not-unloaded");
                if (!RouteEquals(route, state.FinishedRoutes[^1]))
                    return Mismatch("route-summary");
            }

            ulong fullMask = request.Tasks.Count == 64 ? ulong.MaxValue : (1UL << request.Tasks.Count) - 1;
            if (state.CompletedMask != fullMask || state.CurrentRouteSteps.Count != 0 || state.OnBoard.Count != 0)
                return Mismatch("incomplete");
            var verified = RoutePlanFactory.FromState(request, state, plan.Status, plan.Diagnostics);
            if (plan.Objective is null || verified.Objective is null ||
                plan.Objective.Value.CompareTo(verified.Objective.Value) != 0)
                return Mismatch("objective");
            return new RouteVerificationResult(true, null, verified);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) {
            return Mismatch(ex.GetType().Name);
        }
    }

    private static RouteTransitionResult ReplayBarter(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        string rowId) {
        int index = request.Tasks.ToList().FindIndex(x => x.RowId == rowId);
        return index < 0
            ? new RouteTransitionResult(false, state, null,
                new RouteDiagnostic("verification-mismatch", rowId, Detail: "row"))
            : RouteStateTransition.TryBarter(request, state, index);
    }

    private static bool StepEquals(RouteStep expected, RouteStep actual) {
        if (expected.GetType() != actual.GetType() || expected.IslandId != actual.IslandId || expected.Load != actual.Load)
            return false;
        return (expected, actual) switch {
            (WarehousePickupStep a, WarehousePickupStep b) =>
                a.WarehouseId == b.WarehouseId && a.Items.SequenceEqual(b.Items),
            (WarehouseUnloadStep a, WarehouseUnloadStep b) =>
                a.WarehouseId == b.WarehouseId && a.Items.SequenceEqual(b.Items),
            (BarterStep a, BarterStep b) =>
                a.RowId == b.RowId && a.Consumed == b.Consumed && a.Produced == b.Produced,
            _ => false,
        };
    }

    private static bool RouteEquals(PlannedRoute expected, PlannedRoute actual) =>
        expected.Number == actual.Number &&
        expected.StartWarehouseId == actual.StartWarehouseId &&
        expected.EndWarehouseId == actual.EndWarehouseId &&
        expected.Distance.Equals(actual.Distance) &&
        expected.InitialLT == actual.InitialLT &&
        expected.CurrentLT == actual.CurrentLT &&
        expected.PeakLT == actual.PeakLT &&
        expected.Steps.Count == actual.Steps.Count &&
        expected.Steps.Zip(actual.Steps).All(x => StepEquals(x.First, x.Second));

    private static RouteVerificationResult Mismatch(string detail) =>
        new(false, new RouteDiagnostic("verification-mismatch", Detail: detail), null);
}

internal static class RoutePlanFactory {
    public static RoutePlan FromState(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        RoutePlanStatus status,
        IReadOnlyList<RouteDiagnostic> diagnostics) {
        var routes = state.FinishedRoutes;
        var objective = new RoutePlanObjective(
            routes.Count,
            state.TotalDistance,
            state.PickupStopCount,
            routes.Count == 0 ? request.ExtraLT : routes.Max(x => x.PeakLT),
            StableRouteKey(routes, []));
        return new RoutePlan(
            status, routes, objective, diagnostics, RoutePlanFingerprint.Compute(request));
    }

    public static string StableRouteKey(
        IEnumerable<PlannedRoute> routes,
        IEnumerable<RouteStep> currentSteps) =>
        string.Join("|", routes.SelectMany(x => x.Steps).Concat(currentSteps).Select(step => step switch {
            WarehousePickupStep pickup => $"P:{pickup.WarehouseId}:{ItemsKey(pickup.Items)}",
            BarterStep barter => $"B:{barter.RowId}",
            WarehouseUnloadStep unload => $"U:{unload.WarehouseId}",
            _ => step.IslandId,
        }));

    private static string ItemsKey(IEnumerable<RouteItemQuantity> items) =>
        string.Join(",", items.Select(x => $"{x.ItemId}={x.Quantity}"));
}
