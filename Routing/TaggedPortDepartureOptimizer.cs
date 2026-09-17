namespace iBarter.Routing;

/// <summary>Move legal arrival handling before an overloaded leg, without changing its island order or distance.</summary>
internal static class TaggedPortDepartureOptimizer {
    public static TaggedTransportState Improve(TaggedTransportSimulator sim, TaggedTransportState best, CancellationToken cancellation) {
        for (int index = 0; index < best.Steps.Count && !cancellation.IsCancellationRequested; index++) {
            var step = best.Steps[index];
            var sail = step.Action;
            if (sail.Kind != TaggedActionKind.Sail || step.ShipLT <= sim.Request.ShipLimitLT
                || sim.Port(sail.From) is null || sim.Port(sail.Location) is null) continue;
            int end = index + 1;
            while (end < best.Steps.Count && Portable(best.Steps[end].Action, sail.Location)) end++;
            var actions = best.Steps.Select(s => s.Action).ToArray();
            for (int limit = end; limit > index + 1 && !cancellation.IsCancellationRequested; limit--) {
                var candidate = sim.Initial();
                var reordered = actions.Take(index)
                    .Concat(actions.Skip(index + 1).Take(limit - index - 1).Select(a => a with { Location = sail.From }))
                    .Append(sail).Concat(actions.Skip(limit));
                bool valid = true;
                foreach (var action in reordered) {
                    if (cancellation.IsCancellationRequested || !sim.TryApply(candidate, action, out candidate, out _)) { valid = false; break; }
                }
                // Recheck every later exchange, slot, character and warehouse.
                // Failed alternatives never replace the already verified plan.
                if (!valid || !sim.Complete(candidate) || TaggedDistanceObjective.Compare(sim.Request, candidate, best) >= 0) continue;
                best = candidate;
                index = limit - 1; // the same sailing action now follows the moved handling
                break;
            }
        }
        return best;
    }

    private static bool Portable(TaggedAction action, string location) => action.Location == location
        && (action.Kind is TaggedActionKind.Switch or TaggedActionKind.SummonElephant or TaggedActionKind.Sell
            || action.Kind == TaggedActionKind.Transfer
                && !TaggedTransportSimulator.IsWarehouse(action.From) && !TaggedTransportSimulator.IsWarehouse(action.To));
}
