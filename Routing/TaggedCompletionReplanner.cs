namespace iBarter.Routing;

/// <summary>Map completion removes one exact exchange segment, never a prefix.
/// Replanning uses recorded inputs; it does not invent stock or move any cargo.</summary>
public static class TaggedCompletionReplanner {
    public static TaggedTransportPlan? Seed(TaggedTransportSession session, int selectedStep,
        TaggedTransportRequest request, CancellationToken cancellation = default) {
        var sim = new TaggedTransportSimulator(request);
        var continued = ReplayContinuation(session, selectedStep, sim, cancellation);
        if (continued is not null) return ToPlan(continued);
        var compiler = new TaggedVoyageCompiler(sim, () => cancellation.IsCancellationRequested);
        var routes = TaggedTransportRoutes.Build(session.Request, session.Plan);
        var selectedRoute = routes.Single(r => r.Start <= selectedStep && selectedStep < r.End);
        var order = new[] { selectedRoute }.Concat(routes.Where(r => r != selectedRoute)).ToArray();
        TaggedTransportState? best = null;
        for (int variant = 0; variant < 3 && !cancellation.IsCancellationRequested; variant++) {
            var state = sim.Initial(); bool first = true, valid = true;
            foreach (var route in order) {
                var jobs = Enumerable.Range(route.Start, route.End - route.Start)
                    .Where(i => i >= session.CompletedSteps && i != selectedStep && session.Plan.Steps[i].Action.Kind == TaggedActionKind.Barter)
                    .Select(i => { var a = session.Plan.Steps[i].Action; var id = session.Request.Trades[a.TradeIndex].RowId;
                        return new TaggedVoyageJob(Array.FindIndex(request.Trades, t => t.RowId == id), a.Quantity); }).ToArray();
                if (jobs.Length == 0) continue;
                if (jobs.Any(j => j.TradeIndex < 0)) { valid = false; break; }
                if (first && variant == 1) jobs = jobs.Reverse().ToArray();
                if (first && variant == 2) jobs = jobs.OrderBy(j => sim.Distance(state.Location, request.Trades[j.TradeIndex].IslandId)).ToArray();
                if (!first && state.Location != route.StartIsland && !sim.TryApply(state,
                    new(TaggedActionKind.Sail, route.StartIsland, state.Location), out state, out _)) { valid = false; break; }
                var next = compiler.Compile(state, jobs, session.Plan.Steps[route.End - 1].Action.Location, prepareAtWarehouse: !first);
                if (next is null) { valid = false; break; }
                state = next; first = false;
            }
            if (valid && sim.Complete(state) && (best is null || TaggedDistanceObjective.Compare(request, state, best) < 0)) best = state;
        }
        return best is null ? null : ToPlan(best);

        TaggedTransportPlan ToPlan(TaggedTransportState state) => new(request.Fingerprint(), state.Steps.ToArray(), state.Seconds,
            state.SailingSeconds, state.Seconds - state.SailingSeconds, state.Distance, "BestKnownWithinLimit");
    }

    private static TaggedTransportState? ReplayContinuation(TaggedTransportSession session, int selectedStep,
        TaggedTransportSimulator sim, CancellationToken cancellation) {
        // Completing the next exchange already leaves an executable suffix in
        // the saved plan. Preserve it before attempting a new packing/order.
        // For an out-of-order click, Complete below rejects any missing earlier
        // exchange; the route compiler then builds a complete continuation.
        var state = sim.Initial();
        foreach (var step in session.Plan.Steps.Skip(selectedStep + 1)) {
            if (cancellation.IsCancellationRequested) return null;
            var action = step.Action;
            if (action.Kind == TaggedActionKind.Barter) {
                var rowId = session.Request.Trades[action.TradeIndex].RowId;
                int index = Array.FindIndex(sim.Request.Trades, t => t.RowId == rowId);
                if (index < 0) return null;
                action = action with { TradeIndex = index };
            }
            // Summoning is transient and is not persisted in InitialCargo.
            // Re-establish it legally if completion took place at this wharf.
            foreach (var mount in new[] { action.From, action.To }.Where(TaggedTransportSimulator.IsElephant).Distinct()) {
                if (action.Kind is not (TaggedActionKind.Transfer or TaggedActionKind.StackAtWarehouse)
                    || state.SummonedElephants.Contains(mount)) continue;
                if (!sim.TryApply(state, new(TaggedActionKind.SummonElephant, state.Location, state.Active, mount), out state, out _)) return null;
            }
            if (!sim.TryApply(state, action, out state, out _)) return null;
        }
        return sim.Complete(state) ? state : null;
    }
    public static TaggedTransportRequest AtCompletedExchange(TaggedTransportSession session, int stepIndex) {
        _ = Remaining(session, stepIndex); // validate target identity and quantity
        var sim = new TaggedTransportSimulator(session.Request);
        var state = session.Current();
        var original = state;
        var route = TaggedTransportRoutes.Build(session.Request, session.Plan).Single(r => r.Start <= stepIndex && stepIndex < r.End);
        // A click on a later trip does not confirm the earlier trips. Reconstruct
        // only the selected trip's required loading/handling, skipping other barters.
        bool replayed = true;
        for (int i = Math.Max(session.CompletedSteps, route.Start); i <= stepIndex; i++) {
            var action = session.Plan.Steps[i].Action;
            if (action.Kind == TaggedActionKind.Sail || action.Kind == TaggedActionKind.Barter && i != stepIndex) continue;
            if (state.Location != action.Location && !sim.TryApply(state,
                    new(TaggedActionKind.Sail, action.Location, state.Location), out state, out string sailError))
                { replayed = false; break; }
            if (!sim.TryApply(state, action, out state, out string error))
                { replayed = false; break; }
        }
        if (!replayed) state = ReorderSelectedExchange(sim, original, session, route, stepIndex)
            ?? throw new InvalidOperationException("The selected exchange's input is unavailable in recorded cargo and warehouse stock.");
        return session.Request with {
            Settings = session.Request.Settings with { StartIsland = state.Location, ActiveCharacter = state.Active,
                StartFromSelectedLocation = true, InitialCargo = state.Cargo.Where(c => !TaggedTransportSimulator.IsWarehouse(c.Key))
                    .SelectMany(c => c.Value.Where(i => i.Value > 0).Select(i => new TaggedCargoEntry(c.Key, i.Key, i.Value))).ToArray() },
            Warehouses = state.Cargo.Where(c => TaggedTransportSimulator.IsWarehouse(c.Key))
                .ToDictionary(c => c.Key["warehouse:".Length..], c => new Dictionary<string, int>(c.Value)),
            Trades = session.Request.Trades.Select((t, i) => t with { Exchanges = state.Remaining[i] }).Where(t => t.Exchanges > 0).ToArray()
        };
    }

    private static TaggedTransportState? ReorderSelectedExchange(TaggedTransportSimulator sim, TaggedTransportState initial,
        TaggedTransportSession session, TaggedTransportRoute route, int selectedStep) {
        var selected = session.Plan.Steps[selectedStep].Action;
        var first = new TaggedVoyageJob(selected.TradeIndex, selected.Quantity);
        var rest = Enumerable.Range(Math.Max(route.Start, session.CompletedSteps), route.End - Math.Max(route.Start, session.CompletedSteps))
            .Where(i => i != selectedStep && session.Plan.Steps[i].Action.Kind == TaggedActionKind.Barter)
            .Select(i => new TaggedVoyageJob(session.Plan.Steps[i].Action.TradeIndex, session.Plan.Steps[i].Action.Quantity)).ToArray();
        var compiler = new TaggedVoyageCompiler(sim, () => false);
        foreach (var jobs in new[] { new[] { first }.Concat(rest).ToArray(), new[] { first } }) {
            var carried = compiler.Compile(initial, jobs, selected.Location, prepareAtWarehouse: false, stopAfterFirstExchange: true);
            if (carried is not null) return carried;
            foreach (var port in sim.Request.Settings.Ports.Where(p => p.Enabled && p.WarehouseId.Length > 0)
                .OrderBy(p => p.IslandId == route.StartIsland ? 0 : 1).ThenBy(p => sim.Distance(initial.Location, p.IslandId))) {
                var departure = initial;
                if (departure.Location != port.IslandId && !sim.TryApply(departure,
                    new(TaggedActionKind.Sail, port.IslandId, departure.Location), out departure, out _)) continue;
                for (int packing = 0; packing < 3; packing++) {
                    var result = compiler.Compile(departure, jobs, selected.Location, packing, stopAfterFirstExchange: true);
                    if (result is not null) return result;
                }
            }
        }
        return null;
    }
    public static TaggedTransportRequest Remaining(TaggedTransportSession session, int stepIndex) {
        if (session.MapPlanCompleted || stepIndex < session.CompletedSteps || stepIndex >= session.Plan.Steps.Length)
            throw new InvalidOperationException("This exchange is no longer pending.");
        var action = session.Plan.Steps[stepIndex].Action;
        if (action.Kind != TaggedActionKind.Barter) throw new InvalidOperationException("Select an exchange.");
        var trade = session.Request.Trades[action.TradeIndex];
        var request = session.CompletedSteps > 0 ? session.RemainingRequest() : session.Request;
        var pending = request.Trades.Single(t => t.RowId == trade.RowId);
        if (pending.Exchanges < action.Quantity) throw new InvalidOperationException("Exchange quantity changed.");
        return request with { Trades = request.Trades.Select(t => t.RowId == trade.RowId
            ? t with { Exchanges = t.Exchanges - action.Quantity } : t).Where(t => t.Exchanges > 0).ToArray() };
    }
}
