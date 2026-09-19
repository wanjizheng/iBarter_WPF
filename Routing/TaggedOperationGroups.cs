namespace iBarter.Routing;

public sealed record TaggedOperationGroup(int Start, int End, string Kind);
// Display summaries only; these actions must never be executed or persisted as plan steps.
public sealed record TaggedDisplayOperation(TaggedAction Action, bool Done);

public static class TaggedOperationGroups {
    public static TaggedDisplayOperation[] Summarize(TaggedTransportPlan plan, TaggedOperationGroup group, int completedSteps) {
        var combined = new List<TaggedDisplayOperation>();
        for (int n = group.Start; n < group.End; n++) {
            var action = plan.Steps[n].Action; bool done = n < completedSteps;
            if (combined.Count > 0 && n > group.Start && action.Kind == TaggedActionKind.Transfer && combined[^1].Done == done
                && plan.Steps[n - 1].Action == action) {
                var last = combined[^1]; combined[^1] = last with { Action = last.Action with { Quantity = last.Action.Quantity + action.Quantity } };
            } else combined.Add(new(action, done));
        }
        var result = new List<TaggedDisplayOperation>();
        for (int i = 0; i < combined.Count; i++) {
            var entry = combined[i]; var a = entry.Action;
            if (i + 1 < combined.Count) {
                var next = combined[i + 1]; var b = next.Action;
                if (entry.Done == next.Done && a.Kind == TaggedActionKind.Transfer && b.Kind == TaggedActionKind.Transfer
                    && a.Location == b.Location && a.ItemId == b.ItemId && a.Quantity == b.Quantity && a.To == "ship" && b.From == "ship"
                    && (TaggedTransportSimulator.IsWarehouse(a.From) && b.To is "main" or "alt"
                        || a.From is "main" or "alt" && TaggedTransportSimulator.IsWarehouse(b.To))) {
                    entry = entry with { Action = a with { To = b.To } }; i++;
                }
            }
            result.Add(entry);
        }
        return result.ToArray();
    }
    public static TaggedOperationGroup[] Build(TaggedTransportRequest request, TaggedTransportPlan plan) {
        var result = new List<TaggedOperationGroup>();
        foreach (var route in TaggedTransportRoutes.Build(request, plan)) {
            for (int i = route.Start; i < route.End;) {
                var a = plan.Steps[i].Action;
                if (a.Kind == TaggedActionKind.Sail) { i++; continue; }
                if (a.Kind == TaggedActionKind.Barter) { result.Add(new(i, ++i, "operation")); continue; }
                if (a.Kind == TaggedActionKind.Sell) {
                    int saleEnd = i + 1;
                    while (saleEnd < route.End && plan.Steps[saleEnd].Action is { Kind: TaggedActionKind.Sell } sale && sale.Location == a.Location) saleEnd++;
                    result.Add(new(i, saleEnd, "selling")); i = saleEnd; continue;
                }
                int end = i + 1;
                while (end < route.End && plan.Steps[end].Action.Location == a.Location
                    && plan.Steps[end].Action.Kind is not (TaggedActionKind.Sail or TaggedActionKind.Barter or TaggedActionKind.Sell)) end++;
                var actions = plan.Steps.Skip(i).Take(end - i).Select(s => s.Action).ToArray();
                bool loading = actions.Any(x => TaggedTransportSimulator.IsWarehouse(x.From));
                bool unloading = actions.Any(x => TaggedTransportSimulator.IsWarehouse(x.To));
                if (loading || unloading) {
                    result.Add(new(i, end, loading && unloading ? "handling" : loading ? "loading" : "unloading"));
                    i = end;
                }
                else { result.Add(new(i, end, end > i + 1 ? "transfer" : "operation")); i = end; }
            }
        }
        return result.ToArray();
    }

    public static int MoveCursor(TaggedTransportRequest request, TaggedTransportPlan plan, int cursor, bool forward) {
        var groups = Build(request, plan);
        if (forward) {
            var next = groups.FirstOrDefault(g => g.End > cursor);
            if (next is null || next == groups.LastOrDefault()) return plan.Steps.Length;
            return next.End;
        }
        // Older saves may stop inside a newly merged loading card. Undo that partial card first.
        var partial = groups.FirstOrDefault(g => g.Start < cursor && cursor < g.End);
        if (partial is not null) return groups.LastOrDefault(g => g.End <= partial.Start)?.End ?? 0;
        var done = groups.Where(g => g.End <= cursor).ToArray();
        return done.Length < 2 ? 0 : done[^2].End;
    }
}
