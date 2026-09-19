namespace iBarter.Routing;

// Presentation partitions only: inventories and execution order remain one continuous session.
public sealed record TaggedTransportRoute(int Number, int Start, int End, string StartIsland);

public static class TaggedTransportRoutes {
    public static TaggedTransportRoute[] Build(TaggedTransportRequest request, TaggedTransportPlan plan) {
        if (plan.Steps.Length == 0) return [];
        var warehouses = request.Settings.Ports.Where(p => p.Enabled && !string.IsNullOrEmpty(p.WarehouseId))
            .Select(p => p.IslandId).ToHashSet(StringComparer.Ordinal);
        var result = new List<TaggedTransportRoute>();
        int start = 0;
        string startIsland = request.Settings.StartIsland, location = startIsland;
        bool returned = false, hasBartered = false;
        int lastSail = Array.FindLastIndex(plan.Steps, s => s.Action.Kind == TaggedActionKind.Sail);
        for (int i = 0; i < plan.Steps.Length; i++) {
            var action = plan.Steps[i].Action;
            bool loading = action.Kind == TaggedActionKind.StackAtWarehouse
                || action.Kind == TaggedActionKind.Transfer && TaggedTransportSimulator.IsWarehouse(action.From);
            if (returned && i <= lastSail && (loading || action.Kind == TaggedActionKind.Sail)) {
                result.Add(new(result.Count + 1, start, i, startIsland));
                start = i; startIsland = location; returned = false; hasBartered = false;
            }
            location = action.Location;
            if (action.Kind == TaggedActionKind.Barter) hasBartered = true;
            // A trade/sale at a foreign warehouse port is still part of the
            // current voyage. Split on actual storage handling, or arrival home.
            if (hasBartered && (action.Kind == TaggedActionKind.Transfer && TaggedTransportSimulator.IsWarehouse(action.To)
                || action.Kind == TaggedActionKind.Sail && warehouses.Contains(location)
                    && (string.IsNullOrEmpty(request.Settings.HomeWarehouseId)
                        || request.Settings.Ports.Any(p => p.IslandId == location && p.WarehouseId == request.Settings.HomeWarehouseId)))) returned = true;
        }
        result.Add(new(result.Count + 1, start, plan.Steps.Length, startIsland));
        bool numbered = plan.RouteNumbers is { } numbers && numbers.Length == result.Count
            && numbers.All(n => n > 0) && numbers.Distinct().Count() == numbers.Length;
        return result.Select((r, i) => numbered ? r with { Number = plan.RouteNumbers![i] } : r).ToArray();
    }

    public static TaggedTransportPlan RetainNumbers(TaggedTransportRequest oldRequest, TaggedTransportPlan oldPlan,
        TaggedTransportRequest request, TaggedTransportPlan plan, int completedStep = -1) {
        HashSet<string> Rows(TaggedTransportRequest r, TaggedTransportPlan p, TaggedTransportRoute route, int skip = -1) =>
            Enumerable.Range(route.Start, route.End - route.Start).Where(i => i != skip && p.Steps[i].Action.Kind == TaggedActionKind.Barter)
                .Select(i => r.Trades[p.Steps[i].Action.TradeIndex].RowId).ToHashSet(StringComparer.Ordinal);
        var old = Build(oldRequest, oldPlan).Select(r => (r.Number, Rows: Rows(oldRequest, oldPlan, r, completedStep))).ToArray();
        var used = new HashSet<int>();
        int next = old.Select(r => r.Number).DefaultIfEmpty(0).Max() + 1;
        var numbers = Build(request, plan with { RouteNumbers = null }).Select(r => {
            var rows = Rows(request, plan, r);
            var match = old.Where(o => !used.Contains(o.Number)).Select(o => (o.Number, Count: o.Rows.Count(rows.Contains)))
                .Where(o => o.Count > 0).OrderByDescending(o => o.Count).ThenBy(o => o.Number).FirstOrDefault();
            int number = match.Count > 0 ? match.Number : next++;
            used.Add(number); return number;
        }).ToArray();
        return plan with { RouteNumbers = numbers };
    }
}
