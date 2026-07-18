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
/// <para>The fix is purely a post-pass over a finalized <see cref="RoutePlan"/>:
/// simulate on-board inventory forward through every step, drop any pickup
/// item that no remaining BarterStep consumes, drop any unload item that no
/// upstream BarterStep actually produced. Initial-on-board items and barter
/// outputs are never pruned.</para>
/// </summary>
public static class RouteCargoNormalizer {
    /// <summary>
    /// Returns a new <see cref="RoutePlan"/> with the same routes after
    /// pruning unused pickup/unload cargo. Returns <paramref name="plan"/>
    /// unchanged if no route needed normalization.
    /// </summary>
    public static RoutePlan Normalize(RoutePlan plan) {
        if (plan is null) return plan!;
        if (plan.Routes.Count == 0) return plan;

        var newRoutes = new List<PlannedRoute>(plan.Routes.Count);
        bool anyChange = false;
        foreach (var route in plan.Routes) {
            var (normalized, changed) = NormalizeRoute(route);
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
    public static (PlannedRoute route, bool changed) NormalizeRoute(PlannedRoute route) {
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
        //    keptPickupQty[item] = quantity we still hold on board by end-of-route
        //    droppedPickupItems = items whose pickup was pruned for zero consumption
        var keptPickupQty = new Dictionary<string, int>(StringComparer.Ordinal);
        var droppedPickupItems = new HashSet<string>(StringComparer.Ordinal);

        var onboard = new Dictionary<string, int>(StringComparer.Ordinal);
        var newSteps = new List<RouteStep>(route.Steps.Count);
        bool anyChanged = false;
        foreach (var step in route.Steps) {
            switch (step) {
                case WarehousePickupStep pickup: {
                    var newItems = new List<RouteItemQuantity>(pickup.Items.Count);
                    foreach (var item in pickup.Items) {
                        int remainingNeeded = consumedTotals.GetValueOrDefault(item.ItemId)
                            - onboard.GetValueOrDefault(item.ItemId);
                        if (remainingNeeded <= 0) {
                            anyChanged = true;
                            droppedPickupItems.Add(item.ItemId);
                            continue;
                        }
                        int newQty = Math.Min(item.Quantity, remainingNeeded);
                        if (newQty < item.Quantity) anyChanged = true;
                        if (newQty > 0) {
                            newItems.Add(new RouteItemQuantity(item.ItemId, newQty));
                            onboard[item.ItemId] = onboard.GetValueOrDefault(item.ItemId) + newQty;
                            keptPickupQty[item.ItemId] = newQty;
                        }
                        else {
                            anyChanged = true;
                            droppedPickupItems.Add(item.ItemId);
                        }
                    }
                    newSteps.Add(new WarehousePickupStep(
                        pickup.WarehouseId, pickup.IslandId, newItems, pickup.Load));
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
                    newSteps.Add(barter);
                    break;
                }
                case WarehouseUnloadStep unload: {
                    var newUnloadItems = new List<RouteItemQuantity>(unload.Items.Count);
                    foreach (var item in unload.Items) {
                        bool wasProduced = producedTotals.GetValueOrDefault(item.ItemId) > 0;
                        bool wasPickupKept = keptPickupQty.ContainsKey(item.ItemId);
                        bool wasPickupDropped = droppedPickupItems.Contains(item.ItemId);
                        // Was this unload item's matching pickup entirely
                        // dropped (no consumption)? The pickup → unload
                        // round trip is what we are eliminating.
                        if (wasPickupDropped && !wasProduced) {
                            anyChanged = true;
                            continue;
                        }
                        // Pure cosmetic unload (no pickup, no production)
                        // could be InitialOnBoard cargo — keep it.
                        if (!wasProduced && !wasPickupKept && !wasPickupDropped) {
                            newUnloadItems.Add(item);
                            continue;
                        }
                        // Barter output (with or without pickup) — keep.
                        if (wasProduced) {
                            newUnloadItems.Add(item);
                            onboard[item.ItemId] = Math.Max(0,
                                onboard.GetValueOrDefault(item.ItemId) - item.Quantity);
                            continue;
                        }
                        // Kept pickup item: how much do we still hold at
                        // end-of-route? If we still hold exactly the
                        // unloaded quantity, keep; if all of it was
                        // consumed by upstream barters, drop the unload
                        // entry.
                        int held = onboard.GetValueOrDefault(item.ItemId);
                        if (held <= 0) {
                            // Nothing left on board by end-of-route. The
                            // unload entry was a phantom; drop it.
                            anyChanged = true;
                            continue;
                        }
                        newUnloadItems.Add(item);
                        onboard[item.ItemId] = Math.Max(0, held - item.Quantity);
                    }
                    if (newUnloadItems.Count != unload.Items.Count) anyChanged = true;
                    newSteps.Add(new WarehouseUnloadStep(
                        unload.WarehouseId, unload.IslandId, newUnloadItems, unload.Load));
                    break;
                }
            }
        }
        if (!anyChanged) return (route, false);
        var rebuilt = new PlannedRoute(
            route.Number, route.StartWarehouseId, route.EndWarehouseId,
            newSteps, route.Distance, route.InitialLT, route.CurrentLT, route.PeakLT);
        return (rebuilt, true);
    }
}