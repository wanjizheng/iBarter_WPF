namespace iBarter.Routing;

/// <summary>
/// Trims pickup/unload cargo so a route never carries items that no later
/// BarterStep actually consumes (and, symmetrically, never unloads items that
/// no upstream BarterStep actually produced).
///
/// <para>Bug 2 root cause: the demand bundle generator enumerates greedy
/// subsets of pending tasks; intermediate <c>required</c> dictionaries are
/// emitted as bundles even when only a subset of the supporting tasks end up
/// in the final route. The matching <c>WarehouseUnloadPlanner</c> then
/// unconditionally dumps every item still on board at end-of-route, so
/// unconsumed pickups are trucked out and dropped back at the source
/// warehouse — a zero-value round trip that still costs load and LT.</para>
///
/// <para>Audit round 2 (1): the v1 normalizer rewrote Items on pickup/unload
/// steps but kept the original <c>Load.CargoLT</c>/<c>TotalWithExtraLT</c>/
/// <c>PeakTotalLT</c> from the plan it was given. After pruning the
/// 五彩线团 round-trip the items disappeared from disk but every step's LT
/// stayed pinned to the old higher value, so PeakLT no longer reflected
/// reality. The v2 normalizer now requires the request and replays every
/// route through <see cref="RouteReplay"/> after items change, so each
/// <see cref="RouteStep.Load"/> matches the live on-board state and the
/// resulting <see cref="PlannedRoute.InitialLT"/>/
/// <see cref="PlannedRoute.CurrentLT"/>/<see cref="PlannedRoute.PeakLT"/>
/// are recomputed from scratch.</para>
///
/// <para>The fix is purely a post-pass over a finalized <see cref="RoutePlan"/>:
/// simulate on-board inventory forward through every step, drop any pickup
/// item that no remaining BarterStep consumes, drop any unload item that no
/// upstream BarterStep actually produced. Initial-on-board items and barter
/// outputs are never pruned.</para>
/// </summary>
public static class RouteCargoNormalizer {
    /// <summary>
    /// Returns a new <see cref="RoutePlan"/> with the same routes after
    /// pruning unused pickup/unload cargo and recomputing every step's LT
    /// snapshot via <see cref="RouteReplay"/>. Returns <paramref name="plan"/>
    /// unchanged if no route needed normalization AND every replay returned
    /// the original route (defense against silent state drift).
    /// </summary>
    public static RoutePlan Normalize(AutomaticRoutePlanningRequest request, RoutePlan plan) {
        if (plan is null) return plan!;
        if (plan.Routes.Count == 0) return plan;

        var newRoutes = new List<PlannedRoute>(plan.Routes.Count);
        bool anyChange = false;
        foreach (var route in plan.Routes) {
            var (normalized, changed) = NormalizeRoute(request, route);
            newRoutes.Add(normalized);
            anyChange |= changed;
        }
        if (!anyChange) return plan;
        return new RoutePlan(plan.Status, newRoutes, plan.Objective, plan.Diagnostics,
            plan.InputFingerprint);
    }

    /// <summary>
    /// Per-route normalization. Returns the route (possibly rebuilt) and a
    /// flag indicating whether any change was made.
    /// </summary>
    public static (PlannedRoute route, bool changed) NormalizeRoute(
        AutomaticRoutePlanningRequest request, PlannedRoute route) {
        // 1. Forward-simulate on-board inventory tracking which items were
        //    actually consumed/produced by BarterStep.
        var consumedTotals = new Dictionary<string, int>(StringComparer.Ordinal);
        var producedTotals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var step in route.Steps) {
            if (step is BarterStep barter) {
                consumedTotals[barter.Consumed.ItemId] =
                    consumedTotals.GetValueOrDefault(barter.Consumed.ItemId) + barter.Consumed.Quantity;
                producedTotals[barter.Produced.ItemId] =
                    producedTotals.GetValueOrDefault(barter.Produced.ItemId) + barter.Produced.Quantity;
            }
        }

        // 2. Track which pickup items survive (with their kept quantity)
        //    and which were dropped entirely, so we can prune the matching
        //    unload entries symmetrically.
        var keptPickupQty = new Dictionary<string, int>(StringComparer.Ordinal);
        var droppedPickupItems = new HashSet<string>(StringComparer.Ordinal);

        var onboard = new Dictionary<string, int>(StringComparer.Ordinal);
        var candidateSteps = new List<RouteStep>(route.Steps.Count);
        bool itemsChanged = false;
        bool pickupHasContent = false;
        bool unloadHasContent = false;
        foreach (var step in route.Steps) {
            switch (step) {
                case WarehousePickupStep pickup: {
                    var newItems = new List<RouteItemQuantity>(pickup.Items.Count);
                    foreach (var item in pickup.Items) {
                        int remainingNeeded = consumedTotals.GetValueOrDefault(item.ItemId)
                            - onboard.GetValueOrDefault(item.ItemId);
                        if (remainingNeeded <= 0) {
                            itemsChanged = true;
                            droppedPickupItems.Add(item.ItemId);
                            continue;
                        }
                        int newQty = Math.Min(item.Quantity, remainingNeeded);
                        if (newQty < item.Quantity) itemsChanged = true;
                        if (newQty > 0) {
                            newItems.Add(new RouteItemQuantity(item.ItemId, newQty));
                            onboard[item.ItemId] = onboard.GetValueOrDefault(item.ItemId) + newQty;
                            keptPickupQty[item.ItemId] = newQty;
                            pickupHasContent = true;
                        }
                        else {
                            itemsChanged = true;
                            droppedPickupItems.Add(item.ItemId);
                        }
                    }
                    if (pickupHasContent) {
                        // Drop pickups that became entirely empty: the
                        // simulator would reject an empty pickup with
                        // "insufficient-stock", and an empty pickup at
                        // an already-visited warehouse has no value.
                        candidateSteps.Add(new WarehousePickupStep(
                            pickup.WarehouseId, pickup.IslandId, newItems,
                            new RouteLoadSnapshot(0, 0, 0)));
                    }
                    else if (pickup.Items.Count > 0) {
                        itemsChanged = true;
                    }
                    break;
                }
                case BarterStep barter: {
                    int consumeAvailable = onboard.GetValueOrDefault(barter.Consumed.ItemId);
                    int consume = Math.Min(consumeAvailable, barter.Consumed.Quantity);
                    int consumeShortage = barter.Consumed.Quantity - consume;
                    if (consumeShortage > 0) {
                        onboard[barter.Consumed.ItemId] = consumeAvailable + consumeShortage;
                    }
                    else {
                        int after = consumeAvailable - consume;
                        if (after == 0) onboard.Remove(barter.Consumed.ItemId);
                        else onboard[barter.Consumed.ItemId] = after;
                    }
                    onboard[barter.Produced.ItemId] =
                        onboard.GetValueOrDefault(barter.Produced.ItemId) + barter.Produced.Quantity;
                    candidateSteps.Add(barter);
                    break;
                }
                case WarehouseUnloadStep unload: {
                    var newUnloadItems = new List<RouteItemQuantity>(unload.Items.Count);
                    foreach (var item in unload.Items) {
                        bool wasProduced = producedTotals.GetValueOrDefault(item.ItemId) > 0;
                        bool wasPickupKept = keptPickupQty.ContainsKey(item.ItemId);
                        bool wasPickupDropped = droppedPickupItems.Contains(item.ItemId);
                        if (wasPickupDropped && !wasProduced) {
                            itemsChanged = true;
                            continue;
                        }
                        if (!wasProduced && !wasPickupKept && !wasPickupDropped) {
                            newUnloadItems.Add(item);
                            unloadHasContent = true;
                            continue;
                        }
                        if (wasProduced) {
                            newUnloadItems.Add(item);
                            onboard[item.ItemId] = Math.Max(0,
                                onboard.GetValueOrDefault(item.ItemId) - item.Quantity);
                            unloadHasContent = true;
                            continue;
                        }
                        int held = onboard.GetValueOrDefault(item.ItemId);
                        if (held <= 0) {
                            itemsChanged = true;
                            continue;
                        }
                        newUnloadItems.Add(item);
                        unloadHasContent = true;
                        onboard[item.ItemId] = Math.Max(0, held - item.Quantity);
                    }
                    if (newUnloadItems.Count != unload.Items.Count) itemsChanged = true;
                    if (unloadHasContent || unload.Items.Count == 0) {
                        // Keep the unload only if it actually carries
                        // something we still hold. The verifier replays
                        // an unload-by-items list; an empty Items array
                        // matches "no remaining cargo to put back".
                        candidateSteps.Add(new WarehouseUnloadStep(
                            unload.WarehouseId, unload.IslandId, newUnloadItems,
                            new RouteLoadSnapshot(0, 0, 0)));
                    }
                    break;
                }
            }
        }
        if (!itemsChanged) return (route, false);

        // 3. Items changed — replay the candidate route through the
        //    simulator so every step's Load, InitialLT, CurrentLT and
        //    PeakLT are recomputed from the live on-board state.
        if (candidateSteps.Count == 0) return (route, false);
        var candidateRoute = new PlannedRoute(
            route.Number, route.StartWarehouseId, route.EndWarehouseId,
            candidateSteps, route.Distance,
            initialLT: 0, currentLT: 0, peakLT: 0);
        PlannedRoute? replayed = RouteReplay.ReplayRoute(request, candidateRoute);
        if (replayed is null) return (route, false);
        if (replayed.Steps.Count == 0) return (route, false);
        return (replayed, true);
    }
}