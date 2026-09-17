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
            if (action.Kind == TaggedActionKind.Sail && warehouses.Contains(location) && hasBartered) returned = true;
        }
        result.Add(new(result.Count + 1, start, plan.Steps.Length, startIsland));
        return result.ToArray();
    }
}
