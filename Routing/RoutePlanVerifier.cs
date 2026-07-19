namespace iBarter.Routing;

public sealed record RouteVerificationResult(
    bool Success,
    RouteDiagnostic? Diagnostic,
    RoutePlan? VerifiedPlan);

public static class RoutePlanVerifier {
    public static RouteVerificationResult Verify(
        AutomaticRoutePlanningRequest request,
        RoutePlan plan) {
        if (RouteSearchProfiler.Current is { } p) p.VerifyCalls++;
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

            // The cross-route leak check (route-redundant-cargo-roundtrip) is
            // intentionally NOT inlined here so the planner's internal
            // VerificationResult.Success can stay a pure simulator-replay
            // signal. The publish path runs the leak detector after this
            // call to gate persistence; see
            // AutomaticRouteCoordinator.PublishGeneratedPlan.
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
        int index = -1;
        for (int i = 0; i < request.Tasks.Count; i++)
            if (StringComparer.Ordinal.Equals(request.Tasks[i].RowId, rowId)) { index = i; break; }
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

    /// <summary>
    /// Round #3 invariant: within a single <see cref="PlannedRoute"/>,
    /// if a <see cref="WarehousePickupStep"/> at warehouse <c>W</c> carries
    /// an item <c>X</c> that no <see cref="BarterStep"/> in the same route
    /// consumes (and that no other barter produces either), AND the route
    /// unloads <c>X</c> at the same <c>W</c>, then <c>X</c> is a pure
    /// zero-value round-trip — 1000 LT per unit, several thousand in the
    /// user's 800061 case. Report it so the normalizer can fix it OR the
    /// caller can refuse to persist.
    /// <para>
    /// Output detail format (single-line, greppable):
    /// <c>route=&lt;N&gt; warehouse=&lt;W&gt; item=&lt;X&gt; picked=&lt;N&gt;
    /// routeLocalConsumed=&lt;N&gt; sameWarehouseReturned=&lt;N&gt;</c>.
    /// </para>
    /// </summary>
    public static bool TryDetectRouteRedundantCargoRoundTrip(RoutePlan plan, out string detail) {
        foreach (var route in plan.Routes) {
            var pickedAt = new Dictionary<(string Wh, string ItemId), int>(/* relaxed comparer ok */);
            var consumedByBarter = new Dictionary<string, int>(StringComparer.Ordinal);
            var producedByBarter = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var step in route.Steps) {
                switch (step) {
                    case WarehousePickupStep pickup:
                        foreach (var it in pickup.Items) {
                            var key = (pickup.WarehouseId, it.ItemId);
                            pickedAt[key] = pickedAt.GetValueOrDefault(key) + it.Quantity;
                        }
                        break;
                    case BarterStep b:
                        consumedByBarter[b.Consumed.ItemId] =
                            consumedByBarter.GetValueOrDefault(b.Consumed.ItemId) + b.Consumed.Quantity;
                        producedByBarter[b.Produced.ItemId] =
                            producedByBarter.GetValueOrDefault(b.Produced.ItemId) + b.Produced.Quantity;
                        break;
                }
            }
            foreach (var step in route.Steps) {
                if (step is not WarehouseUnloadStep unload) continue;
                foreach (var it in unload.Items) {
                    var key = (unload.WarehouseId, it.ItemId);
                    if (!pickedAt.TryGetValue(key, out int pickedQty)) continue;
                    int consumed = consumedByBarter.GetValueOrDefault(it.ItemId);
                    int produced = producedByBarter.GetValueOrDefault(it.ItemId);
                    // A round-trip exists iff: we picked X at W, no barter
                    // in the route consumes X (and no other barter produces
                    // X either, otherwise the on-board X count could be
                    // higher than what was picked), and we unload X at the
                    // SAME W.
                    if (pickedQty > 0 && it.Quantity > 0 && consumed == 0 && produced == 0) {
                        detail = $"route={route.Number} warehouse={unload.WarehouseId} " +
                                 $"item={it.ItemId} picked={pickedQty} " +
                                 $"routeLocalConsumed={consumed} sameWarehouseReturned={it.Quantity}";
                        return true;
                    }
                }
            }
        }
        detail = "";
        return false;
    }
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
        IEnumerable<RouteStep> currentSteps) {
        if (RouteSearchProfiler.Current is { } p) p.StableKeyCalls++;
        return string.Join("|", routes.SelectMany(x => x.Steps).Concat(currentSteps).Select(step => step switch {
            WarehousePickupStep pickup => $"P:{pickup.WarehouseId}:{ItemsKey(pickup.Items)}",
            BarterStep barter => $"B:{barter.RowId}",
            WarehouseUnloadStep unload => $"U:{unload.WarehouseId}",
            _ => step.IslandId,
        }));
    }

    private static string ItemsKey(IEnumerable<RouteItemQuantity> items) =>
        string.Join(",", items.Select(x => $"{x.ItemId}={x.Quantity}"));
}
