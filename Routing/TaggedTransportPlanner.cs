using System.Diagnostics;
using System.Text;

namespace iBarter.Routing;

/// <summary>Bounded search minimizing total sailing distance, then trips and handling steps.</summary>
public sealed class TaggedTransportPlanner {
    public TaggedTransportResult Plan(TaggedTransportRequest request, CancellationToken cancellation = default, RoutePlan? preferredShipPlan = null,
        Action<TaggedSearchProgress>? progress = null, TaggedSearchDiagnostics? diagnostics = null, TaggedTransportPlan? preferredTaggedPlan = null) {
        var simulator = new TaggedTransportSimulator(request);
        string? invalid = simulator.Validate();
        if (invalid is not null) return new(null, invalid);
        var start = simulator.Initial();
        if (!start.Cargo.Keys.All(x => simulator.FitsSlots(start, x))
            || simulator.Weight(start, "ship") > request.ShipLimitLT * request.Settings.BarterOutputRatio)
            return new(null, "Initial cargo exceeds available slots or ship allowance.");
        var watch = Stopwatch.StartNew();
        TaggedTransportState? best = TaggedShipRouteBaseline.Build(simulator, start, preferredShipPlan, cancellation, out bool preferredAccepted);
        best ??= ShipOnlyBaseline(simulator, start, cancellation);
        if (!request.Settings.StartFromSelectedLocation && request.Settings.InitialCargo.Length == 0 && best is not null) {
            var first = best.Steps.FirstOrDefault(s => s.Action.Kind != TaggedActionKind.Sail);
            if (first is not null && simulator.Port(first.Action.Location)?.WarehouseId.Length > 0 && first.Action.Location != start.Location) {
                var rebasedRequest = request with { Settings = request.Settings with { StartIsland = first.Action.Location } };
                var rebasedSimulator = new TaggedTransportSimulator(rebasedRequest);
                var rebased = rebasedSimulator.Initial(); bool valid = true;
                foreach (var step in best.Steps.SkipWhile(s => s.Action.Kind == TaggedActionKind.Sail)) {
                    if (!rebasedSimulator.TryApply(rebased, step.Action, out rebased, out _)) { valid = false; break; }
                }
                if (valid && rebasedSimulator.Complete(rebased)) {
                    request = rebasedRequest; simulator = rebasedSimulator; start = simulator.Initial(); best = rebased;
                }
            }
        }
        bool shipOnly = best is not null && best.Steps.All(s => s.ShipLT <= request.ShipLimitLT &&
            (s.Action.Kind is TaggedActionKind.Sail or TaggedActionKind.Barter || s.Action.Kind == TaggedActionKind.Transfer &&
                (s.Action.From == "ship" && TaggedTransportSimulator.IsWarehouse(s.Action.To) || s.Action.To == "ship" && TaggedTransportSimulator.IsWarehouse(s.Action.From))));
        double? baselineSeconds = shipOnly ? best!.Seconds : null;
        double? baselineDistance = shipOnly ? best!.Distance : null;
        int? baselineRoutes = !shipOnly ? null : TaggedTransportRoutes.Build(request,
            new("", best!.Steps.ToArray(), best.Seconds, best.SailingSeconds, best.Seconds - best.SailingSeconds, best.Distance, "")).Length;
        if (preferredTaggedPlan is not null) {
            var replay = start; bool valid = true, beforeHandling = true;
            foreach (var step in preferredTaggedPlan.Steps) {
                var action = step.Action;
                if (beforeHandling && action.Kind == TaggedActionKind.Sail && action.Location == replay.Location) continue;
                beforeHandling &= action.Kind == TaggedActionKind.Sail;
                if (action.Kind == TaggedActionKind.Sail) action = action with { From = replay.Location };
                if (!simulator.TryApply(replay, action, out replay, out _)) { valid = false; break; }
            }
            if (valid && !simulator.Complete(replay) && request.Settings.HomeWarehouseId is { Length: > 0 }) {
                var recompiled = new TaggedVoyageCompiler(simulator, () => cancellation.IsCancellationRequested).Recompile(start, preferredTaggedPlan.Steps);
                if (recompiled is not null) replay = recompiled;
            }
            if (valid && simulator.Complete(replay)) {
                if (best is null || TaggedDistanceObjective.Compare(request, replay, best) < 0) best = replay;
                // A saved TAG session can itself contain a wholly ordinary plan.
                // Recognize that verified reference instead of comparing against a
                // weaker fresh seed and overstating the benefit of assistance.
                if ((baselineDistance is null || replay.Distance < baselineDistance) && replay.Steps.All(s =>
                    s.ShipLT <= request.ShipLimitLT && (s.Action.Kind is TaggedActionKind.Sail or TaggedActionKind.Barter
                    || s.Action.Kind == TaggedActionKind.Transfer &&
                        (s.Action.From == "ship" && TaggedTransportSimulator.IsWarehouse(s.Action.To)
                        || s.Action.To == "ship" && TaggedTransportSimulator.IsWarehouse(s.Action.From))))) {
                    baselineDistance = replay.Distance; baselineSeconds = replay.Seconds;
                    baselineRoutes = TaggedTransportRoutes.Build(request, new("", replay.Steps.ToArray(), replay.Seconds,
                        replay.SailingSeconds, replay.Seconds - replay.SailingSeconds, replay.Distance, "")).Length;
                }
            }
        }
        if (best is not null) best = TaggedPortDepartureOptimizer.Improve(simulator,
            TaggedTerminalSales.Normalize(simulator, best, cancellation), cancellation);
        int expanded = 0;
        var profile = request.Settings.SearchProfile;
        double budgetSeconds = profile?.TotalTarget.TotalSeconds ?? request.Settings.SearchSeconds;
        bool sustained = profile?.UsesExtremeSearch == true;
        double lastImprovement = watch.Elapsed.TotalSeconds;
        bool converged = false;
        double lastProgress = -1;
        progress?.Invoke(new(watch.Elapsed.TotalSeconds, budgetSeconds, expanded, best?.Distance,
            best is null ? null : TaggedTransportRoutes.Build(request, new("", best.Steps.ToArray(), 0, 0, 0, best.Distance, "")).Length));
        // Only use operation-level search to establish feasibility when ordinary
        // shipping cannot seed a plan (for example, cargo already on a character).
        // Complete-route optimization below is independent of ordinary trip splits.
        for (int pass = 0; best is null && (sustained || pass < 2); pass++) {
            if (cancellation.IsCancellationRequested || watch.Elapsed.TotalSeconds >= budgetSeconds || converged) break;
            if (diagnostics is not null) diagnostics.Passes++;
            double progressWeight = new[] { 20000.0, 2000.0, 6000.0, 40000.0, 1000.0, 12000.0 }[pass % 6] * (1 + pass / 6 * 0.05);
            var queue = new PriorityQueue<TaggedTransportState, (double, long)>();
            var seen = new Dictionary<string, (double Distance, double OverloadedDistance, int Steps)>(StringComparer.Ordinal);
            long serial = 0;
            queue.Enqueue(start, (Score(start), serial++));
            seen[Key(start)] = (0, 0, 0);
            int passLimit = profile is not null ? (int)Math.Min(500000L, profile.MaxBeamParents + (sustained ? (long)pass * 5000 : 0)) : request.Settings.MaxStates / 2;
            // The desktop is x86. Keep live states and full inventory keys bounded;
            // a longer requested duration adds refinement passes, not unbounded memory.
            int frontierLimit = Environment.Is64BitProcess ? 12000 : 2500;
            int seenLimit = Environment.Is64BitProcess ? 100000 : 15000;
            int passExpanded = 0;
            // Restart from verified prefixes as well as departure, so long runs can improve
            // later trips without rediscovering the entire preceding cargo sequence.
            if (sustained && best is not null) {
                var prefix = start;
                for (int i = 0; i < best.Steps.Count; i++) {
                    if (!simulator.TryApply(prefix, best.Steps[i].Action, out prefix, out _)) break;
                    if (i % 8 != pass % 8 || simulator.Complete(prefix)) continue;
                    seen[Key(prefix)] = (prefix.Distance, prefix.OverloadedDistance, prefix.Steps.Count);
                    queue.Enqueue(prefix, (Score(prefix), serial++));
                }
            }
            while (queue.TryDequeue(out var state, out _)) {
                if (profile?.UsesExtremeConvergence == true && best is not null
                    && watch.Elapsed.TotalSeconds - lastImprovement >= profile.ExtremeNoImprovementTimeout.TotalSeconds) { converged = true; break; }
                if (cancellation.IsCancellationRequested || profile is null && expanded >= request.Settings.MaxStates
                    || watch.Elapsed.TotalSeconds >= budgetSeconds || passExpanded >= passLimit || seen.Count >= seenLimit) break;
                if (watch.Elapsed.TotalSeconds - lastProgress >= 1) {
                    progress?.Invoke(new(watch.Elapsed.TotalSeconds, budgetSeconds, expanded, best?.Distance));
                    lastProgress = watch.Elapsed.TotalSeconds;
                }
                string stateKey = Key(state);
                if (seen.TryGetValue(stateKey, out var known) && (state.Distance, state.OverloadedDistance, state.Steps.Count).CompareTo(known) > 0) continue;
                if (best is not null && state.Distance > best.Distance) continue;
                expanded++; passExpanded++;
                diagnostics?.Observe(stateKey, state);
                if (simulator.Complete(state)) {
                    if (diagnostics is not null) diagnostics.CompleteCandidates++;
                    if (best is null || TaggedDistanceObjective.Compare(request, state, best) < 0) {
                        best = state; lastImprovement = watch.Elapsed.TotalSeconds;
                        if (diagnostics is not null) diagnostics.BetterCandidates++;
                    }
                    break;
                }
                var actions = Actions(simulator, state);
                if (pass % 2 == 1) actions = actions.Reverse();
                foreach (var action in actions) {
                    if (!simulator.TryApply(state, action, out var next, out _)) continue;
                    if (best is not null && next.Distance > best.Distance) continue;
                    string key = Key(next);
                    var cost = (next.Distance, next.OverloadedDistance, next.Steps.Count);
                    if (seen.TryGetValue(key, out var old) && old.CompareTo(cost) <= 0) continue;
                    seen[key] = cost;
                    queue.Enqueue(next, (Score(next), serial++));
                }
                // Keep memory bounded independently of the number of expanded states.
                if (queue.Count > frontierLimit) {
                    var keep = queue.UnorderedItems.OrderBy(x => x.Priority).Take(frontierLimit / 2).ToArray();
                    if (diagnostics is not null) { diagnostics.FrontierTrims++; diagnostics.DiscardedQueuedStates += queue.Count - keep.Length; }
                    queue.Clear();
                    foreach (var x in keep) queue.Enqueue(x.Element, x.Priority);
                }
            }
            if (diagnostics is not null && seen.Count >= seenLimit) diagnostics.StateLimitRestarts++;
            double Score(TaggedTransportState s) {
                // Remaining exchange fractions prevent a high-multiplier row dominating all other work.
                double remaining = s.Remaining.Select((n, i) => (double)n / request.Trades[i].Exchanges).Sum();
                double carried = s.Cargo.Where(x => !TaggedTransportSimulator.IsWarehouse(x.Key)).Sum(x =>
                    x.Value.Count(i => request.Items[i.Key].UnitWeight > 0));
                return s.Distance + progressWeight * remaining + (remaining == 0 ? carried * 100 : 0);
            }
        }
        if (best is not null && !cancellation.IsCancellationRequested && watch.Elapsed.TotalSeconds < budgetSeconds)
            best = TaggedVoyageSearch.Improve(simulator, start, best, watch, budgetSeconds, cancellation, progress, diagnostics, ref expanded);
        if (best is not null) best = TaggedPortDepartureOptimizer.Improve(simulator,
            TaggedTerminalSales.Normalize(simulator, best, cancellation), cancellation);
        var reason = cancellation.IsCancellationRequested ? ExtremeSearchTerminationReason.UserCancelled
            : watch.Elapsed.TotalSeconds >= budgetSeconds ? ExtremeSearchTerminationReason.MaxDurationReached
            : profile?.UsesExtremeConvergence == true ? ExtremeSearchTerminationReason.NoImprovementConverged
            : ExtremeSearchTerminationReason.Completed;
        var report = new TaggedSearchReport(watch.Elapsed.TotalSeconds, expanded, reason, preferredShipPlan is null ? null : preferredAccepted);
        if (best is null) return new(null, cancellation.IsCancellationRequested ? "Cancelled; previous plan retained."
            : "No executable route found within the search budget. Check departure stock, port capabilities and carrier space, or increase search time.", Search: report);
        var plan = new TaggedTransportPlan(request.Fingerprint(), best.Steps.ToArray(), best.Seconds,
            best.SailingSeconds, best.Seconds - best.SailingSeconds, best.Distance, "BestKnownWithinLimit", baselineSeconds, baselineRoutes, baselineDistance);
        return simulator.Verify(plan, out _, out string error)
            ? new(plan, $"Shortest verified distance found; {expanded:N0} search candidates evaluated. Global optimality is not proven within a bounded search.", request, report)
            : new(null, "Replay failed: " + error, Search: report with { Termination = ExtremeSearchTerminationReason.Error });
    }

    private static TaggedTransportState? ShipOnlyBaseline(TaggedTransportSimulator sim, TaggedTransportState start, CancellationToken token) {
        // A feasible ordinary-shipping incumbent ensures a large search can never replace
        // a known shorter run with a longer TAG run. This is a same-task fallback, not a
        // claim to reproduce the legacy distance optimizer's exact route.
        var r = sim.Request; var state = start;
        var warehouses = r.Settings.Ports.Where(x => x.Enabled && x.WarehouseId.Length > 0).ToArray();
        if (start.Cargo.Where(x => !TaggedTransportSimulator.IsWarehouse(x.Key)).Any(x => x.Value.Count > 0) || start.Active != "main") {
            // A stricter receive rule can invalidate the saved incumbent while
            // leaving real carried cargo. Establish a legal seed by returning
            // that cargo first; all sailing/unloading remains in the plan.
            var compiler = new TaggedVoyageCompiler(sim, () => token.IsCancellationRequested);
            var unloaded = warehouses.Where(p => r.Settings.HomeWarehouseId is not { Length: > 0 } home || p.WarehouseId == home)
                .OrderBy(p => sim.Distance(start.Location, p.IslandId))
                .Select(p => compiler.ReturnCargoToWarehouse(start, p.IslandId)).FirstOrDefault(s => s is not null);
            if (unloaded is null) return null;
            state = unloaded;
        }
        for (int iteration = 0; iteration < 2000 && !token.IsCancellationRequested; iteration++) {
            if (sim.Complete(state)) return state;
            TaggedTransportState? chosen = null;
            for (int index = 0; index < r.Trades.Length; index++) {
                if (state.Remaining[index] == 0) continue;
                var t = r.Trades[index];
                double inputLT = (double)r.Items[t.InputId].UnitWeight * t.InputPerExchange;
                double outputLT = (double)r.Items[t.OutputId].UnitWeight * t.OutputPerExchange;
                int capacity = Math.Max(inputLT, outputLT) == 0 ? state.Remaining[index]
                    : (int)Math.Floor((r.ShipLimitLT - r.ShipOccupiedLT) / Math.Max(inputLT, outputLT));
                if (!sim.Stackable(t.InputId)) capacity = Math.Min(capacity, r.Settings.ShipSlots / t.InputPerExchange);
                if (!sim.Stackable(t.OutputId)) capacity = Math.Min(capacity, r.Settings.ShipSlots / t.OutputPerExchange);
                if (capacity <= 0) continue;
                foreach (var w in warehouses) {
                    string wh = TaggedTransportSimulator.Warehouse(w.WarehouseId);
                    int n = Math.Min(capacity, Math.Min(state.Remaining[index], sim.Count(state, wh, t.InputId) / t.InputPerExchange));
                    if (n <= 0) continue;
                    var candidate = state;
                    if (!Move(w.IslandId) || !Apply(new(TaggedActionKind.Transfer, candidate.Location, wh, "ship", t.InputId, n * t.InputPerExchange))
                        || !Move(t.IslandId) || !Apply(new(TaggedActionKind.Barter, candidate.Location, Quantity: n, TradeIndex: index))) continue;
                    var destination = warehouses.Where(p => r.Settings.HomeWarehouseId is not { Length: > 0 } home || p.WarehouseId == home)
                        .OrderBy(p => sim.Distance(candidate.Location, p.IslandId)).First();
                    if (!Move(destination.IslandId)) continue;
                    bool good = true;
                    foreach (var item in candidate.Cargo["ship"].Where(x => r.Items[x.Key].UnitWeight > 0).ToArray())
                        if (!Apply(new(TaggedActionKind.Transfer, candidate.Location, "ship", TaggedTransportSimulator.Warehouse(destination.WarehouseId), item.Key, item.Value))) { good = false; break; }
                    if (good && (chosen is null || TaggedDistanceObjective.Compare(r, candidate, chosen) < 0)) chosen = candidate;
                    bool Move(string where) => candidate.Location == where || Apply(new(TaggedActionKind.Sail, where, candidate.Location, where));
                    bool Apply(TaggedAction action) {
                        if (!sim.TryApply(candidate, action, out var next, out _)) return false;
                        candidate = next; return true;
                    }
                }
            }
            if (chosen is null) return null;
            state = chosen;
        }
        return sim.Complete(state) ? state : null;
    }

    internal static IEnumerable<TaggedAction> Actions(TaggedTransportSimulator sim, TaggedTransportState state) {
        var r = sim.Request;
        string here = state.Location;
        var port = sim.Port(here);
        var demand = new Dictionary<string, int>();
        for (int i = 0; i < r.Trades.Length; i++) {
            var t = r.Trades[i];
            if (state.Remaining[i] == 0) continue;
            demand[t.InputId] = checked(demand.GetValueOrDefault(t.InputId) + state.Remaining[i] * t.InputPerExchange);
            if (t.IslandId != here) continue;
            int available = Math.Min(state.Remaining[i], sim.Count(state, "ship", t.InputId) / t.InputPerExchange);
            // Keep single exchanges available near the ship's overweight boundary.
            foreach (int n in new[] { available, 1 }.Where(n => n > 0 && n <= available).Distinct())
                yield return new(TaggedActionKind.Barter, here, Quantity: n, TradeIndex: i);
        }
        if (port is not null) {
            foreach (string source in new[] { "ship", state.Active })
                foreach (var item in state.Cargo[source].Where(i => i.Value > 0 && sim.IsTerminalLevelSeven(state, i.Key)))
                    yield return new(TaggedActionKind.Sell, here, source, "shop", item.Key, item.Value);
            yield return new(TaggedActionKind.Switch, here, state.Active, state.Active == "main" ? "alt" : "main");
            string wh = TaggedTransportSimulator.Warehouse(port.WarehouseId);
            var local = new List<string> { "ship", state.Active };
            string elephant = state.Active + "-elephant";
            if (state.SummonedElephants.Contains(elephant)) local.Add(elephant);
            else yield return new(TaggedActionKind.SummonElephant, here, state.Active, elephant);
            if (port.WarehouseId.Length > 0) local.Add(wh);
            foreach (string from in local)
                foreach (var item in state.Cargo[from].Where(x => x.Value > 0 && r.Items[x.Key].UnitWeight > 0
                             || x.Value > 0 && demand.ContainsKey(x.Key)).ToArray()) {
                    bool sourceWarehouse = TaggedTransportSimulator.IsWarehouse(from);
                    if (sourceWarehouse && !demand.ContainsKey(item.Key)) continue;
                    foreach (string to in local.Where(x => x != from)) {
                        if (sim.IsTerminalLevelSeven(state, item.Key) && !(from == elephant && to == state.Active)) continue;
                        bool targetWarehouse = TaggedTransportSimulator.IsWarehouse(to);
                        if (!targetWarehouse && to != state.Active && from != state.Active
                            && !(from == wh && to == "ship")) continue;
                        if (to == "ship" && !demand.ContainsKey(item.Key)) continue;
                        if (sourceWarehouse && to == state.Active && sim.Weight(state, state.Active) >=
                            r.Settings.Carriers.First(x => x.Id == state.Active).LimitLT * r.Settings.CharacterReceiveRatio) continue;
                        int max = item.Value;
                        if (sourceWarehouse) {
                            int carried = state.Cargo.Where(x => !TaggedTransportSimulator.IsWarehouse(x.Key)).Sum(x => x.Value.GetValueOrDefault(item.Key));
                            max = Math.Min(max, Math.Max(0, demand.GetValueOrDefault(item.Key) - carried));
                        }
                        if (to == "ship" && r.Items[item.Key].UnitWeight > 0)
                            max = Math.Min(max, Math.Max(0, (int)Math.Floor((r.ShipLimitLT - sim.Weight(state, "ship")) / r.Items[item.Key].UnitWeight)));
                        if (sourceWarehouse && to == state.Active && r.Items[item.Key].UnitWeight > 0)
                            max = Math.Min(max, Math.Max(0, (int)Math.Floor((r.Settings.Carriers.First(x => x.Id == state.Active).LimitLT
                                - sim.Weight(state, state.Active)) / r.Items[item.Key].UnitWeight)));
                        if (!targetWarehouse && to != "ship" && !sim.Stackable(item.Key)) max = Math.Min(max, 1);
                        var quantities = new HashSet<int> { max };
                        foreach (var trade in r.Trades.Where(t => t.InputId == item.Key)) {
                            quantities.Add(Math.Min(max, trade.InputPerExchange));
                            quantities.Add(Math.Min(max, checked(trade.InputPerExchange * trade.Exchanges)));
                        }
                        foreach (int n in quantities.Where(x => x > 0).OrderDescending())
                            yield return new(TaggedActionKind.Transfer, here, from, to, item.Key, n);
                    }
                }
            if (port.WarehouseId.Length > 0 && local.Contains(elephant))
                foreach (var item in state.Cargo[wh].Where(x => demand.ContainsKey(x.Key) && x.Value > 0 && sim.Stackable(x.Key))) {
                    int carried = state.Cargo.Where(x => !TaggedTransportSimulator.IsWarehouse(x.Key)).Sum(x => x.Value.GetValueOrDefault(item.Key));
                    int count = Math.Min(item.Value, Math.Max(0, demand[item.Key] - carried));
                    if (count > 0) yield return new(TaggedActionKind.StackAtWarehouse, here, wh, elephant, item.Key, count);
                }
        }
        // Ordinary barter islands are reachable, but never gain transfer capabilities.
        var destinations = r.Trades.Where((t, i) => state.Remaining[i] > 0
            && sim.Count(state, "ship", t.InputId) >= t.InputPerExchange).Select(t => t.IslandId)
            .Concat(r.Settings.Ports.Where(p => p.Enabled).Select(p => p.IslandId)).Distinct();
        foreach (string destination in destinations.Where(x => x != here).OrderBy(x => sim.Distance(here, x)))
            yield return new(TaggedActionKind.Sail, destination, here, destination);
    }

    private static string Key(TaggedTransportState s) {
        var b = new StringBuilder().Append(s.Location).Append('|').Append(s.Active).Append('|').AppendJoin(',', s.Remaining);
        b.Append('|').AppendJoin(',', s.SummonedElephants.Order(StringComparer.Ordinal));
        foreach (var c in s.Cargo.OrderBy(x => x.Key, StringComparer.Ordinal)) {
            b.Append('|').Append(c.Key);
            foreach (var i in c.Value.Where(x => x.Value > 0).OrderBy(x => x.Key, StringComparer.Ordinal))
                b.Append(';').Append(i.Key.Length).Append(':').Append(i.Key).Append('=').Append(i.Value);
        }
        return b.ToString();
    }
}
