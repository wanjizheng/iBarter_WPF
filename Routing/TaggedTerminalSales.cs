namespace iBarter.Routing;

internal static class TaggedTerminalSales {
    public static TaggedTransportState SellAvailable(TaggedTransportSimulator sim, TaggedTransportState state) {
        if (sim.Port(state.Location) is null) return state;
        // Sell already-carried character goods first, freeing receiving space.
        foreach (string from in new[] { state.Active, state.Active == "main" ? "alt" : "main", "ship" })
            foreach (var item in state.Cargo[from].Where(i => i.Value > 0 && sim.IsTerminalLevelSeven(state, i.Key)).ToArray()) {
                foreach (string owner in from == "ship" ? new[] { state.Active, state.Active == "main" ? "alt" : "main" } : new[] { from }) {
                    var candidate = state;
                    if (owner != candidate.Active && !sim.TryApply(candidate,
                        new(TaggedActionKind.Switch, candidate.Location, candidate.Active, owner), out candidate, out _)) continue;
                    if (!sim.TryApply(candidate, new(TaggedActionKind.Sell, candidate.Location, from, "shop", item.Key, item.Value), out candidate, out _)) continue;
                    if (candidate.Active != state.Active && !sim.TryApply(candidate,
                        new(TaggedActionKind.Switch, candidate.Location, candidate.Active, state.Active), out candidate, out _)) continue;
                    state = candidate; break;
                }
            }
        return state;
    }

    public static TaggedTransportState Normalize(TaggedTransportSimulator sim, TaggedTransportState original, CancellationToken cancellation) {
        if (!sim.Request.Items.Values.Any(i => i.Level == 7)) return original;
        var state = SellAvailable(sim, sim.Initial());
        var moved = new HashSet<int>();
        for (int index = 0; index < original.Steps.Count; index++) {
            if (cancellation.IsCancellationRequested) return original;
            if (moved.Contains(index)) continue;
            var action = original.Steps[index].Action;
            if (action.Kind == TaggedActionKind.Transfer && sim.IsTerminalLevelSeven(state, action.ItemId)
                && sim.Count(state, action.From, action.ItemId) > 0) {
                // A legacy plan can unload LV7 before freeing either character.
                // Move only already-planned local character unloads forward; do
                // not invent warehouse deposits or assume remote stock access.
                for (int later = index + 1; later < original.Steps.Count; later++) {
                    var unload = original.Steps[later].Action;
                    if (unload.Location != state.Location || unload.Kind is TaggedActionKind.Sail or TaggedActionKind.Barter) break;
                    if (moved.Contains(later) || unload.Kind != TaggedActionKind.Transfer || unload.From is not ("main" or "alt")
                        || !TaggedTransportSimulator.IsWarehouse(unload.To) || sim.IsTerminalLevelSeven(state, unload.ItemId)) continue;
                    var candidate = state;
                    if (candidate.Active != unload.From && !sim.TryApply(candidate,
                        new(TaggedActionKind.Switch, candidate.Location, candidate.Active, unload.From), out candidate, out _)) continue;
                    if (!sim.TryApply(candidate, unload, out candidate, out _)) continue;
                    if (candidate.Active != state.Active && !sim.TryApply(candidate,
                        new(TaggedActionKind.Switch, candidate.Location, candidate.Active, state.Active), out candidate, out _)) continue;
                    state = SellAvailable(sim, candidate); moved.Add(later);
                    if (sim.Count(state, action.From, action.ItemId) == 0) break;
                }
            }
            if (action.Kind is TaggedActionKind.Transfer or TaggedActionKind.Sell && sim.IsTerminalLevelSeven(state, action.ItemId)) {
                // Previous carried goods may already have been sold at this or
                // an earlier port. Never synthesize a replacement withdrawal.
                int left = sim.Count(state, action.From, action.ItemId);
                if (left == 0) continue;
                action = action with { Quantity = Math.Min(action.Quantity, left) };
            }
            if (!sim.TryApply(state, action, out state, out _)) return original;
            state = SellAvailable(sim, state);
        }
        return sim.Complete(state) && state.Distance <= original.Distance ? state : original;
    }
}
