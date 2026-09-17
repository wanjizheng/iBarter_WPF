namespace iBarter.Routing;

/// <summary>Read-only projection into the existing map/label pipeline; never used for cargo execution.</summary>
public static class TaggedRoutePresentation {
    public static RoutePlan Build(TaggedTransportSession session) {
        var request = session.Request;
        var routes = TaggedTransportRoutes.Build(request, session.Plan).Select(route => {
            var steps = Enumerable.Range(route.Start, route.End - route.Start).Select(i => {
                var step = session.Plan.Steps[i]; var a = step.Action;
                var load = new RouteLoadSnapshot((int)Math.Ceiling(step.ShipLT - request.ShipOccupiedLT), (int)Math.Ceiling(step.ShipLT), (int)Math.Ceiling(step.ShipLT));
                if (i < session.CompletedSteps) return (RouteStep)new HiddenStep(a.Location, load);
                if (a.Kind == TaggedActionKind.Barter) {
                    var t = request.Trades[a.TradeIndex];
                    return new BarterStep(RouteTaskIdentity.CreateSegmentId(t.RowId, i, session.Plan.Steps.Length), a.Location,
                        new(t.InputId, t.InputPerExchange * a.Quantity), new(t.OutputId, t.OutputPerExchange * a.Quantity), load);
                }
                if (TaggedTransportSimulator.IsWarehouse(a.From))
                    return new WarehousePickupStep(a.From[10..], a.Location, [new(a.ItemId, a.Quantity)], load);
                if (TaggedTransportSimulator.IsWarehouse(a.To))
                    return new WarehouseUnloadStep(a.To[10..], a.Location, [new(a.ItemId, a.Quantity)], load);
                return new HiddenStep(a.Location, load);
            }).ToArray();
            return new PlannedRoute(route.Number, "", "", steps, 0, 0, 0, steps.Select(s => s.Load.PeakTotalLT).DefaultIfEmpty().Max());
        }).ToArray();
        return new(RoutePlanStatus.BestKnownWithinLimit, routes, null, [], session.Plan.Fingerprint);
    }
    private sealed record HiddenStep(string Island, RouteLoadSnapshot Snapshot) : RouteStep(Island, Snapshot);
}
