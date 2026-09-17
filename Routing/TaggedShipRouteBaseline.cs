namespace iBarter.Routing;

/// <summary>Runs the normal multi-island planner, then replays its real operations under TAG rules.</summary>
internal static class TaggedShipRouteBaseline {
    public static TaggedTransportState? Build(TaggedTransportSimulator sim, TaggedTransportState start,
        RoutePlan? preferred, CancellationToken token, out bool preferredAccepted) {
        var best = preferred is null ? null : Replay(sim, start, preferred);
        preferredAccepted = best is not null;
        if (token.IsCancellationRequested) return best;
        var r = sim.Request;
        // The normal solver cannot model cargo on characters or elephants. A prior plan may
        // still be replayable, but do not invent stock by adding those inventories to a warehouse.
        if (start.Cargo.Any(c => c.Key != "ship" && !TaggedTransportSimulator.IsWarehouse(c.Key) && c.Value.Any(i => i.Value > 0))) return best;
        var warehouses = r.Settings.Ports.Where(p => p.Enabled && p.WarehouseId.Length > 0)
            .GroupBy(p => p.WarehouseId).Select(g => g.First())
            .Select(p => new RouteWarehouse(p.WarehouseId, p.IslandId, r.Points[p.IslandId], r.Warehouses[p.WarehouseId])).ToArray();
        if (warehouses.Length == 0) return best;
        var tasks = new List<RouteBarterTask>();
        foreach (var t in r.Trades) {
            double perExchange = Math.Max((double)r.Items[t.InputId].UnitWeight * t.InputPerExchange,
                (double)r.Items[t.OutputId].UnitWeight * t.OutputPerExchange);
            int capacity = perExchange == 0 ? t.Exchanges : (int)Math.Min(t.Exchanges,
                Math.Max(1, Math.Floor((r.ShipLimitLT - r.ShipOccupiedLT) / perExchange)));
            int count = (t.Exchanges - 1) / capacity + 1;
            for (int part = 0, left = t.Exchanges; left > 0; part++) {
                int n = Math.Min(left, capacity); left -= n;
                tasks.Add(new(RouteTaskIdentity.CreateSegmentId(t.RowId, part, count), t.IslandId, r.Points[t.IslandId],
                    t.InputId, checked(n * t.InputPerExchange), t.OutputId, checked(n * t.OutputPerExchange)));
            }
        }
        var request = new AutomaticRoutePlanningRequest(tasks, r.Items, warehouses,
            (int)Math.Ceiling(r.ShipOccupiedLT), (int)Math.Floor(r.ShipLimitLT), new(250000, 500),
            "tagged-ship-baseline-v1", start.Cargo["ship"]);
        double seconds = Math.Clamp((r.Settings.SearchProfile?.TotalTarget.TotalSeconds ?? r.Settings.SearchSeconds) * 0.25, 0.05, 5);
        var profile = RouteOptimizationProfile.For(RouteOptimizationMode.Balanced) with {
            TotalTarget = TimeSpan.FromSeconds(seconds), FinalizationReserve = TimeSpan.FromSeconds(Math.Min(0.1, seconds / 5))
        };
        var plan = new AutomaticRoutePlanner().Plan(request, profile, token);
        var candidate = Replay(sim, start, plan);
        return candidate is not null && (best is null || TaggedDistanceObjective.Compare(r, candidate, best) < 0) ? candidate : best;
    }

    internal static TaggedTransportState? Replay(TaggedTransportSimulator sim, TaggedTransportState start, RoutePlan plan) {
        if (plan.Routes.Count == 0) return null;
        var r = sim.Request; var state = start;
        if (state.Active != "main" && !Apply(new(TaggedActionKind.Switch, state.Location, state.Active, "main"))) return null;
        foreach (var route in plan.Routes) {
            foreach (var step in route.Steps) {
                if (!Move(step.IslandId)) return null;
                switch (step) {
                    case WarehousePickupStep pickup:
                        if (sim.Port(state.Location)?.WarehouseId != pickup.WarehouseId) return null;
                        foreach (var item in pickup.Items)
                            if (!Apply(new(TaggedActionKind.Transfer, state.Location, TaggedTransportSimulator.Warehouse(pickup.WarehouseId), "ship", item.ItemId, item.Quantity))) return null;
                        break;
                    case WarehouseUnloadStep unload:
                        if (sim.Port(state.Location)?.WarehouseId != unload.WarehouseId) return null;
                        foreach (var item in unload.Items)
                            if (!Apply(new(TaggedActionKind.Transfer, state.Location, "ship", TaggedTransportSimulator.Warehouse(unload.WarehouseId), item.ItemId, item.Quantity))) return null;
                        break;
                    case BarterStep barter:
                        int index = Array.FindIndex(r.Trades, t => t.RowId == RouteTaskIdentity.PlannerRowId(barter.RowId));
                        if (index < 0) return null;
                        var t = r.Trades[index];
                        if (barter.IslandId != t.IslandId || barter.Consumed.ItemId != t.InputId || barter.Produced.ItemId != t.OutputId
                            || barter.Consumed.Quantity % t.InputPerExchange != 0) return null;
                        int n = barter.Consumed.Quantity / t.InputPerExchange;
                        if ((long)t.OutputPerExchange * n != barter.Produced.Quantity
                            || !Apply(new(TaggedActionKind.Barter, state.Location, Quantity: n, TradeIndex: index))) return null;
                        break;
                    default: return null;
                }
            }
        }
        // Full replay checks real stock, departure travel, slots, port access and every task.
        // In particular a stale/incomplete ordinary plan cannot become the TAG incumbent.
        if (r.Settings.HomeWarehouseId is { Length: > 0 })
            return new TaggedVoyageCompiler(sim, () => false).Recompile(start, state.Steps.ToArray());
        return sim.Complete(state) ? state : null;
        bool Move(string island) => state.Location == island || Apply(new(TaggedActionKind.Sail, island, state.Location, island));
        bool Apply(TaggedAction action) {
            if (!sim.TryApply(state, action, out var next, out _)) return false;
            state = next; return true;
        }
    }
}
