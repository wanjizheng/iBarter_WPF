namespace iBarter.Routing;

public sealed record CargoNormalizationChange(
    int RouteNumber,
    int StepIndex,
    string WarehouseId,
    string ItemId,
    int PickedQuantity,
    int RequiredQuantity,
    int RedundantQuantity,
    int ReturnedToSameWarehouseQuantity);

public sealed record CargoNormalizationDiagnostic(
    int RouteNumber,
    string StepKind,
    int? StepIndex,
    string ItemId,
    string Code,
    string Detail);

/// <summary>
/// A failed result deliberately carries no plan. Callers must never recover
/// from a normalization failure by publishing the input candidate.
/// </summary>
public sealed record CargoNormalizationResult(
    bool Success,
    RoutePlan? Plan,
    bool Changed,
    IReadOnlyList<CargoNormalizationChange> Changes,
    CargoNormalizationDiagnostic? Failure);

/// <summary>
/// Removes warehouse cargo that is not required by the route-local ordered
/// barter chain. The calculation is prefix-aware: initial cargo and barter
/// outputs are available only after their actual step, and a later pickup does
/// not satisfy an earlier deficit.
/// </summary>
public static class RouteCargoNormalizer {
    public static CargoNormalizationResult Normalize(
        AutomaticRoutePlanningRequest request,
        RoutePlan plan) {
        if (request is null || plan is null) {
            return Failed(0, "input", null, "", "normalization-bad-input",
                "request or plan is null", changed: false, []);
        }
        if (plan.Routes.Count == 0)
            return new CargoNormalizationResult(true, plan, false, [], null);

        var candidateRoutes = new List<PlannedRoute>(plan.Routes.Count);
        var changes = new List<CargoNormalizationChange>();
        bool changed = false;
        foreach (var route in plan.Routes) {
            // InitialOnBoard belongs to the beginning of the whole plan, not
            // the beginning of every route. A finished route unloads all
            // remaining cargo, so later routes must justify their pickups
            // from an empty ship.
            var routeStartOnBoard = candidateRoutes.Count == 0
                ? request.InitialOnBoard
                : EmptyOnBoard;
            var rewrite = RewriteRoute(routeStartOnBoard, route);
            candidateRoutes.Add(rewrite.Route);
            changes.AddRange(rewrite.Changes);
            changed |= rewrite.Changed;
        }

        if (!changed)
            return new CargoNormalizationResult(true, plan, false, [], null);

        var candidate = new RoutePlan(
            plan.Status,
            candidateRoutes,
            plan.Objective,
            plan.Diagnostics,
            plan.InputFingerprint);
        var replay = RouteReplay.ReplayPlan(request, candidate);
        if (!replay.Success || replay.Plan is null) {
            // A route-local cleanup can remove cargo that is deliberately
            // transferred into a warehouse for a later route.  Retry each
            // route/item cleanup independently against the complete plan so
            // only changes that preserve all cross-route inventory flows are
            // accepted.
            return NormalizeConservatively(request, plan, changes, replay.Failure);
        }

        var normalizedPlan = new RoutePlan(
            replay.Plan.Status,
            replay.Plan.Routes,
            replay.Plan.Objective,
            replay.Plan.Diagnostics,
            plan.InputFingerprint);
        return new CargoNormalizationResult(true, normalizedPlan, true, changes, null);
    }

    public static (PlannedRoute route, bool changed, bool replayFailed,
        CargoNormalizationDiagnostic? replayDiagnostic)
        NormalizeRoute(AutomaticRoutePlanningRequest request, PlannedRoute route) {
        var rewrite = RewriteRoute(request, route);
        if (!rewrite.Changed) return (route, false, false, null);

        var replay = RouteReplay.ReplayRoute(request, rewrite.Route);
        if (!replay.Success || replay.Route is null) {
            return (rewrite.Route, true, true, new CargoNormalizationDiagnostic(
                route.Number,
                replay.FailureStepKind ?? "replay",
                replay.FailureStepIndex,
                replay.RequestedItemId ?? "",
                replay.FailureCode ?? "replay-failure",
                RouteReplay.FormatFailureDetail(replay)));
        }
        return (replay.Route, true, false, null);
    }

    internal static RouteCargoRewriteResult RewriteRoute(
        AutomaticRoutePlanningRequest request,
        PlannedRoute route) => RewriteRoute(request.InitialOnBoard, route);

    internal static RouteCargoRewriteResult RewriteRoute(
        IReadOnlyDictionary<string, int> initialOnBoard,
        PlannedRoute route) => RewriteRoute(initialOnBoard, route, null);

    private static RouteCargoRewriteResult RewriteRoute(
        IReadOnlyDictionary<string, int> initialOnBoard,
        PlannedRoute route,
        IReadOnlySet<string>? targetItemIds) {
        var onboard = initialOnBoard.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        var rewritten = new List<RouteStep>(route.Steps.Count);
        var changes = new List<CargoNormalizationChange>();
        bool changed = false;

        for (int stepIndex = 0; stepIndex < route.Steps.Count; stepIndex++) {
            var step = route.Steps[stepIndex];
            switch (step) {
                case WarehousePickupStep pickup: {
                    var kept = new List<RouteItemQuantity>(pickup.Items.Count);
                    foreach (var item in pickup.Items) {
                        int before = onboard.GetValueOrDefault(item.ItemId);
                        if (targetItemIds is not null
                            && !targetItemIds.Contains(item.ItemId)) {
                            kept.Add(item);
                            SetQuantity(onboard, item.ItemId, before + item.Quantity);
                            continue;
                        }
                        int requiredBeforeNextPickup = RequiredOnBoardUntilNextPickup(
                            route.Steps, stepIndex + 1, item.ItemId);
                        int requiredFromThisPickup = Math.Max(
                            0, requiredBeforeNextPickup - before);
                        int keptQuantity = Math.Min(item.Quantity, requiredFromThisPickup);
                        int redundant = item.Quantity - keptQuantity;
                        if (keptQuantity > 0) {
                            kept.Add(new RouteItemQuantity(item.ItemId, keptQuantity));
                            SetQuantity(onboard, item.ItemId, before + keptQuantity);
                        }
                        if (redundant > 0) {
                            changed = true;
                            int returned = route.Steps
                                .Skip(stepIndex + 1)
                                .OfType<WarehouseUnloadStep>()
                                .Where(unload => StringComparer.Ordinal.Equals(
                                    unload.WarehouseId, pickup.WarehouseId))
                                .SelectMany(unload => unload.Items)
                                .Where(unloaded => StringComparer.Ordinal.Equals(
                                    unloaded.ItemId, item.ItemId))
                                .Sum(unloaded => unloaded.Quantity);
                            changes.Add(new CargoNormalizationChange(
                                route.Number,
                                stepIndex,
                                pickup.WarehouseId,
                                item.ItemId,
                                item.Quantity,
                                keptQuantity,
                                redundant,
                                Math.Min(redundant, returned)));
                        }
                    }
                    if (kept.Count > 0) {
                        rewritten.Add(new WarehousePickupStep(
                            pickup.WarehouseId,
                            pickup.IslandId,
                            kept,
                            pickup.Load));
                    }
                    else if (pickup.Items.Count > 0) {
                        changed = true;
                    }
                    break;
                }

                case BarterStep barter:
                    SetQuantity(
                        onboard,
                        barter.Consumed.ItemId,
                        onboard.GetValueOrDefault(barter.Consumed.ItemId)
                            - barter.Consumed.Quantity);
                    SetQuantity(
                        onboard,
                        barter.Produced.ItemId,
                        onboard.GetValueOrDefault(barter.Produced.ItemId)
                            + barter.Produced.Quantity);
                    rewritten.Add(barter);
                    break;

                case WarehouseUnloadStep unload: {
                    var kept = new List<RouteItemQuantity>(unload.Items.Count);
                    foreach (var item in unload.Items) {
                        int held = Math.Max(0, onboard.GetValueOrDefault(item.ItemId));
                        if (targetItemIds is not null
                            && !targetItemIds.Contains(item.ItemId)) {
                            kept.Add(item);
                            SetQuantity(onboard, item.ItemId, held - item.Quantity);
                            continue;
                        }
                        int requiredAfterUnload = RequiredOnBoardUntilNextPickup(
                            route.Steps, stepIndex + 1, item.ItemId);
                        int canUnload = Math.Max(0, held - requiredAfterUnload);
                        int keptQuantity = Math.Min(item.Quantity, canUnload);
                        if (keptQuantity > 0) {
                            kept.Add(new RouteItemQuantity(item.ItemId, keptQuantity));
                            SetQuantity(onboard, item.ItemId, held - keptQuantity);
                        }
                        if (keptQuantity != item.Quantity) changed = true;
                    }
                    // Empty unload is a valid terminal step for zero-weight rewards
                    // and retains the route's explicit destination.
                    rewritten.Add(new WarehouseUnloadStep(
                        unload.WarehouseId,
                        unload.IslandId,
                        kept,
                        unload.Load));
                    break;
                }
            }
        }

        var candidate = changed
            ? new PlannedRoute(
                route.Number,
                route.StartWarehouseId,
                route.EndWarehouseId,
                rewritten,
                route.Distance,
                0,
                0,
                0)
            : route;
        return new RouteCargoRewriteResult(candidate, changed, changes);
    }

    private static CargoNormalizationResult NormalizeConservatively(
        AutomaticRoutePlanningRequest request,
        RoutePlan plan,
        IReadOnlyList<CargoNormalizationChange> attemptedChanges,
        RouteReplayResult? originalFailure) {
        RoutePlan candidate = plan;
        var acceptedChanges = new List<CargoNormalizationChange>();
        bool changed = false;

        for (int routeIndex = 0; routeIndex < candidate.Routes.Count; routeIndex++) {
            var route = candidate.Routes[routeIndex];
            var routeStartOnBoard = routeIndex == 0
                ? request.InitialOnBoard
                : EmptyOnBoard;
            RouteCargoRewriteResult routeRewrite =
                RewriteRoute(routeStartOnBoard, route);
            // A route can deliberately ferry cargo between warehouses for a
            // later route without consuming it locally. Route-local cleanup
            // sees that cargo as redundant. During completion progress the
            // remaining route may also need several obsolete prefix pickups
            // removed as one batch before it fits again, so item-by-item
            // retries cannot recover. Protect the explicit warehouse handoff
            // first, then retry the other cleanup candidates coherently.
            HashSet<string> protectedHandoffItemIds =
                FindLaterRouteWarehouseHandoffItems(candidate, routeIndex);
            HashSet<string> coherentItemIds = routeRewrite.Changes
                .Select(change => change.ItemId)
                .Where(itemId => !protectedHandoffItemIds.Contains(itemId))
                .ToHashSet(StringComparer.Ordinal);
            RouteCargoRewriteResult coherentRewrite =
                protectedHandoffItemIds.Count == 0
                    ? routeRewrite
                    : RewriteRoute(routeStartOnBoard, route, coherentItemIds);
            if (coherentRewrite.Changed) {
                var routes = candidate.Routes.ToArray();
                routes[routeIndex] = coherentRewrite.Route;
                var trial = new RoutePlan(
                    candidate.Status,
                    routes,
                    candidate.Objective,
                    candidate.Diagnostics,
                    candidate.InputFingerprint);
                var trialReplay = RouteReplay.ReplayPlan(request, trial);
                if (trialReplay.Success && trialReplay.Plan is not null) {
                    candidate = new RoutePlan(
                        trialReplay.Plan.Status,
                        trialReplay.Plan.Routes,
                        trialReplay.Plan.Objective,
                        plan.Diagnostics,
                        plan.InputFingerprint);
                    acceptedChanges.AddRange(coherentRewrite.Changes);
                    changed = true;
                    continue;
                }
            }

            // Some progress projections need several obsolete pickup items
            // removed together before the route becomes valid again (for
            // example, each individual cleanup still leaves the ship
            // overweight). Try that coherent route-local batch first. If the
            // batch would break a deliberate cross-route warehouse handoff,
            // fall back to the existing item-by-item safety checks.
            string[] itemIds = routeRewrite.Changes
                .Select(change => change.ItemId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(itemId => itemId, StringComparer.Ordinal)
                .ToArray();

            foreach (string itemId in itemIds) {
                route = candidate.Routes[routeIndex];
                var rewrite = RewriteRoute(
                    routeStartOnBoard,
                    route,
                    new HashSet<string>([itemId], StringComparer.Ordinal));
                if (!rewrite.Changed) continue;

                var routes = candidate.Routes.ToArray();
                routes[routeIndex] = rewrite.Route;
                var trial = new RoutePlan(
                    candidate.Status,
                    routes,
                    candidate.Objective,
                    candidate.Diagnostics,
                    candidate.InputFingerprint);
                var trialReplay = RouteReplay.ReplayPlan(request, trial);
                if (!trialReplay.Success || trialReplay.Plan is null)
                    continue;

                candidate = new RoutePlan(
                    trialReplay.Plan.Status,
                    trialReplay.Plan.Routes,
                    trialReplay.Plan.Objective,
                    plan.Diagnostics,
                    plan.InputFingerprint);
                acceptedChanges.AddRange(rewrite.Changes);
                changed = true;
            }
        }

        var replay = RouteReplay.ReplayPlan(request, candidate);
        if (!replay.Success || replay.Plan is null) {
            var failure = originalFailure ?? replay.Failure;
            return Failed(
                failure?.RouteNumber ?? 0,
                failure?.FailureStepKind ?? "replay",
                failure?.FailureStepIndex,
                failure?.RequestedItemId ?? "",
                failure?.FailureCode ?? "replay-failure",
                RouteReplay.FormatFailureDetail(failure),
                changed || attemptedChanges.Count > 0,
                changed ? acceptedChanges : attemptedChanges);
        }

        var normalizedPlan = new RoutePlan(
            replay.Plan.Status,
            replay.Plan.Routes,
            replay.Plan.Objective,
            plan.Diagnostics,
            plan.InputFingerprint);
        return new CargoNormalizationResult(
            true,
            normalizedPlan,
            changed,
            acceptedChanges,
            null);
    }

    private static HashSet<string> FindLaterRouteWarehouseHandoffItems(
        RoutePlan plan,
        int routeIndex) {
        var laterPickups = plan.Routes
            .Skip(routeIndex + 1)
            .SelectMany(route => route.Steps.OfType<WarehousePickupStep>())
            .SelectMany(pickup => pickup.Items.Select(
                item => (pickup.WarehouseId, item.ItemId)))
            .ToHashSet();

        return plan.Routes[routeIndex].Steps
            .OfType<WarehouseUnloadStep>()
            .SelectMany(unload => unload.Items
                .Where(item => laterPickups.Contains(
                    (unload.WarehouseId, item.ItemId)))
                .Select(item => item.ItemId))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Minimum quantity that must already be on board at the current point to
    /// survive every ordered consumption until another pickup of the same item
    /// can supply it. Barter production is credited only after its step.
    /// </summary>
    internal static int RequiredOnBoardUntilNextPickup(
        IReadOnlyList<RouteStep> steps,
        int startIndex,
        string itemId) {
        int balance = 0;
        int minimum = 0;
        for (int index = startIndex; index < steps.Count; index++) {
            switch (steps[index]) {
                case WarehousePickupStep pickup
                    when pickup.Items.Any(item => StringComparer.Ordinal.Equals(
                        item.ItemId, itemId)):
                    return -minimum;
                case BarterStep barter:
                    if (StringComparer.Ordinal.Equals(barter.Consumed.ItemId, itemId))
                        balance -= barter.Consumed.Quantity;
                    if (StringComparer.Ordinal.Equals(barter.Produced.ItemId, itemId))
                        balance += barter.Produced.Quantity;
                    minimum = Math.Min(minimum, balance);
                    break;
            }
        }
        return -minimum;
    }

    private static CargoNormalizationResult Failed(
        int routeNumber,
        string stepKind,
        int? stepIndex,
        string itemId,
        string code,
        string detail,
        bool changed,
        IReadOnlyList<CargoNormalizationChange> changes) =>
        new(false, null, changed, changes,
            new CargoNormalizationDiagnostic(
                routeNumber, stepKind, stepIndex, itemId, code, detail));

    private static void SetQuantity(
        Dictionary<string, int> quantities,
        string itemId,
        int quantity) {
        if (quantity == 0) quantities.Remove(itemId);
        else quantities[itemId] = quantity;
    }

    private static readonly IReadOnlyDictionary<string, int> EmptyOnBoard =
        new Dictionary<string, int>(StringComparer.Ordinal);
}

internal sealed record RouteCargoRewriteResult(
    PlannedRoute Route,
    bool Changed,
    IReadOnlyList<CargoNormalizationChange> Changes);
