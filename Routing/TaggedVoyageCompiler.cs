namespace iBarter.Routing;

internal readonly record struct TaggedVoyageJob(int TradeIndex, int Quantity);

/// <summary>Compiles a complete ordered voyage into legal, independently inventoried operations.</summary>
internal sealed class TaggedVoyageCompiler(TaggedTransportSimulator sim, Func<bool> stopped) {
    private TaggedTransportRequest R => sim.Request;
    private TaggedTransportState state = null!;
    internal string LastFailure = "";
    private readonly string[][] carrierOrders = [
        ["alt", "main", "alt-elephant", "main-elephant"],
        ["main", "alt", "main-elephant", "alt-elephant"],
        ["alt-elephant", "main-elephant", "alt", "main"]
    ];

    public TaggedTransportState? Compile(TaggedTransportState initial, IReadOnlyList<TaggedVoyageJob> jobs,
        string destinationWarehouseIsland, int packing = 0, bool prepareAtWarehouse = true, bool stopAfterFirstExchange = false,
        IReadOnlyList<TaggedVoyageJob>? preloadJobs = null) {
        if (jobs.Count == 0 || prepareAtWarehouse && sim.Port(initial.Location)?.WarehouseId.Length is not > 0) return null;
        state = initial;
        LastFailure = "preparing at " + state.Location;
        if (prepareAtWarehouse) {
            if (!KeepCargoHere && !UnloadAll()) return null;
            var departure = state;
            if (!Prepare(preloadJobs ?? jobs, packing)) {
                state = departure;
                if (!Prepare(preloadJobs ?? jobs, packing, prefill: false)) return null;
            }
        }
        for (int index = 0; index < jobs.Count; index++) {
            if (stopped()) return null;
            var job = jobs[index]; var trade = R.Trades[job.TradeIndex];
            LastFailure = "preparing exchange at " + trade.IslandId;
            var future = jobs.Skip(index).ToArray();
            var before = state;
            TaggedTransportState? selected = null;
            bool sellBeforeLaterPressure = CarriesTerminalLevelSeven(before)
                && FutureShipWillNeedRelief(before, future);
            // First try the direct leg. If cargo needs handling, enumerate only real ports.
            // Terminal LV7 goods are sold at the earliest useful handling port when
            // any later exchange in this voyage would otherwise make the ship tight.
            if (!sellBeforeLaterPressure && Move(trade.IslandId)
                && Ready(job, normalOutput: packing < 3 || !R.Settings.AllowOverloadedSailing)) selected = state;
            state = before;
            if (selected is null) {
                foreach (var port in R.Settings.Ports.Where(p => p.Enabled).OrderBy(p =>
                    sim.Distance(before.Location, p.IslandId) + sim.Distance(p.IslandId, trade.IslandId))) {
                    if (stopped()) return null;
                    double lower = before.Distance + sim.Distance(before.Location, port.IslandId) + sim.Distance(port.IslandId, trade.IslandId);
                    if (selected is not null && lower > selected.Distance + 0.000001) continue;
                    state = before;
                    if (!Move(port.IslandId) || !Rebalance(job, future) || !Move(trade.IslandId) || !Ready(job, normalOutput: false)) continue;
                    if (selected is null || (state.Distance, state.OverloadedDistance, state.Steps.Count)
                        .CompareTo((selected.Distance, selected.OverloadedDistance, selected.Steps.Count)) < 0) selected = state;
                }
            }
            // An overweight result is allowed only by the user's existing rules; normal
            // sailing candidates are attempted first, without weakening simulator checks.
            if (selected is null) {
                state = before;
                if (Move(trade.IslandId) && Ready(job, normalOutput: false)) selected = state;
            }
            if (selected is null) return null;
            state = selected;
            if (!Apply(new(TaggedActionKind.Barter, state.Location, Quantity: job.Quantity, TradeIndex: job.TradeIndex))) return null;
            if (stopAfterFirstExchange) return state;
            state = TaggedTerminalSales.SellAvailable(sim, state);
        }
        if (state.Remaining.All(n => n == 0) && R.Settings.HomeWarehouseId is { Length: > 0 } home)
            destinationWarehouseIsland = R.Settings.Ports.First(p => p.Enabled && p.WarehouseId == home).IslandId;
        LastFailure = "finishing at " + destinationWarehouseIsland;
        if (!Move(destinationWarehouseIsland)) return null;
        state = TaggedTerminalSales.SellAvailable(sim, state);
        if (KeepCargoHere) {
            // Retain goods for the home warehouse; use characters first to free the ship.
            var needed = R.Trades.Where((t, i) => state.Remaining[i] > 0).Select(t => t.InputId).ToHashSet();
            // Pack all eligible goods together. Packing one item at a time can
            // alternate TAG -> main -> TAG when the next item still fits TAG.
            while (state.Cargo["ship"].Any(i => i.Value > 0 && R.Items[i.Key].UnitWeight > 0 && !needed.Contains(i.Key))) {
                double before = sim.Weight(state, "ship");
                if (!Park("", 0, needed, double.MaxValue) || sim.Weight(state, "ship") >= before) break;
            }
        }
        else if (!UnloadAll()) return null;
        if (!Switch("main")) return null;
        return state;
    }

    private bool KeepCargoHere => R.Settings.HomeWarehouseId is { Length: > 0 } home && sim.Port(state.Location)?.WarehouseId != home;

    private bool CarriesTerminalLevelSeven(TaggedTransportState candidate) =>
        candidate.Cargo.Where(c => !TaggedTransportSimulator.IsWarehouse(c.Key))
            .Any(c => c.Value.Any(i => i.Value > 0 && sim.IsTerminalLevelSeven(candidate, i.Key)));

    private bool FutureShipWillNeedRelief(TaggedTransportState candidate, IReadOnlyList<TaggedVoyageJob> jobs) {
        var cargo = new Dictionary<string, int>(candidate.Cargo["ship"], StringComparer.Ordinal);
        double weight = sim.Weight(candidate, "ship");
        foreach (var job in jobs) {
            var trade = R.Trades[job.TradeIndex];
            int input = checked(trade.InputPerExchange * job.Quantity);
            int missing = Math.Max(0, input - cargo.GetValueOrDefault(trade.InputId));
            if (missing > 0) {
                cargo[trade.InputId] = cargo.GetValueOrDefault(trade.InputId) + missing;
                weight += missing * R.Items[trade.InputId].UnitWeight;
            }
            if (weight > R.ShipLimitLT + 0.001) return true;
            cargo[trade.InputId] -= input;
            if (cargo[trade.InputId] == 0) cargo.Remove(trade.InputId);
            int output = checked(trade.OutputPerExchange * job.Quantity);
            cargo[trade.OutputId] = cargo.GetValueOrDefault(trade.OutputId) + output;
            weight += output * R.Items[trade.OutputId].UnitWeight - input * R.Items[trade.InputId].UnitWeight;
            if (weight > R.ShipLimitLT + 0.001) return true;
        }
        return false;
    }

    internal TaggedTransportState? ReturnCargoToWarehouse(TaggedTransportState initial, string island) {
        state = initial;
        return Move(island) && UnloadAll() ? state : null;
    }

    internal TaggedTransportState? Recompile(TaggedTransportState initial, TaggedTransportStep[] steps) {
        var plan = new TaggedTransportPlan("", steps, 0, 0, 0, 0, "");
        var current = initial;
        var routes = TaggedTransportRoutes.Build(R, plan);
        foreach (var route in routes) {
            var routeSteps = steps.Skip(route.Start).Take(route.End - route.Start).ToArray();
            var jobs = routeSteps.Where(s => s.Action.Kind == TaggedActionKind.Barter)
                .Select(s => new TaggedVoyageJob(s.Action.TradeIndex, s.Action.Quantity)).ToArray();
            if (jobs.Length == 0) continue;
            string departure = routeSteps.TakeWhile(s => s.Action.Kind != TaggedActionKind.Barter)
                .FirstOrDefault(s => TaggedTransportSimulator.IsWarehouse(s.Action.From))?.Action.Location ?? route.StartIsland;
            if (current.Location != departure && !sim.TryApply(current,
                new(TaggedActionKind.Sail, departure, current.Location), out current, out _)) return null;
            var preload = jobs.ToList();
            foreach (var later in routes.Where(r => r.Start >= route.End)) {
                var laterSteps = steps.Skip(later.Start).Take(later.End - later.Start).ToArray();
                if (laterSteps.TakeWhile(s => s.Action.Kind != TaggedActionKind.Barter).Any(s => TaggedTransportSimulator.IsWarehouse(s.Action.From))) break;
                preload.AddRange(laterSteps.Where(s => s.Action.Kind == TaggedActionKind.Barter).Select(s => new TaggedVoyageJob(s.Action.TradeIndex, s.Action.Quantity)));
            }
            current = Compile(current, jobs, steps[route.End - 1].Action.Location,
                prepareAtWarehouse: sim.Port(current.Location)?.WarehouseId.Length > 0, preloadJobs: preload);
            if (current is null) return null;
        }
        return sim.Complete(current) ? current : null;
    }

    private Dictionary<string, int> Required(IReadOnlyList<TaggedVoyageJob> jobs) {
        var available = state.Cargo.Where(c => !TaggedTransportSimulator.IsWarehouse(c.Key))
            .SelectMany(c => c.Value).GroupBy(i => i.Key).ToDictionary(g => g.Key, g => g.Sum(i => i.Value));
        var need = new Dictionary<string, int>();
        foreach (var job in jobs) {
            var t = R.Trades[job.TradeIndex]; int consumed = checked(t.InputPerExchange * job.Quantity);
            int missing = Math.Max(0, consumed - available.GetValueOrDefault(t.InputId));
            if (missing > 0) need[t.InputId] = checked(need.GetValueOrDefault(t.InputId) + missing);
            available[t.InputId] = Math.Max(0, available.GetValueOrDefault(t.InputId) - consumed);
            available[t.OutputId] = checked(available.GetValueOrDefault(t.OutputId) + t.OutputPerExchange * job.Quantity);
        }
        return need;
    }

    private bool Prepare(IReadOnlyList<TaggedVoyageJob> jobs, int packing, bool prefill = true) {
        string wh = TaggedTransportSimulator.Warehouse(sim.Port(state.Location)!.WarehouseId);
        var requirements = Required(jobs);
        if (requirements.Any(i => sim.Count(state, wh, i.Key) < i.Value)) return false;
        var ship = new Dictionary<string, int>();
        var overflow = new Dictionary<string, int>();
        double free = R.ShipLimitLT - sim.Weight(state, "ship");
        int slots = R.Settings.ShipSlots - state.Cargo["ship"].Sum(i => R.Items[i.Key].UnitWeight == 0 ? 0 : sim.Stackable(i.Key) ? 1 : i.Value);
        foreach (var item in requirements) {
            double weight = R.Items[item.Key].UnitWeight;
            int count = weight == 0 ? item.Value : Math.Min(item.Value, Math.Max(0, (int)Math.Floor(free / weight)));
            if (weight > 0) count = Math.Min(count, sim.Stackable(item.Key) ? slots > 0 ? count : 0 : slots);
            if (count > 0) { ship[item.Key] = count; free -= (double)weight * count; if (weight > 0) slots -= sim.Stackable(item.Key) ? 1 : count; }
            if (count < item.Value) overflow[item.Key] = item.Value - count;
        }
        var allocation = R.Settings.Carriers.ToDictionary(c => c.Id, _ => new Dictionary<string, int>());
        foreach (var item in overflow.OrderByDescending(i => prefill ? R.Items[i.Key].UnitWeight * i.Value : 0).ToArray()) {
            int left = overflow[item.Key]; double weight = R.Items[item.Key].UnitWeight;
            if (left == 0) continue;
            foreach (string id in carrierOrders[packing % carrierOrders.Length]) {
                var c = R.Settings.Carriers.Single(c => c.Id == id); var cargo = allocation[id];
                double before = sim.Weight(state, id) + cargo.Sum(i => (double)R.Items[i.Key].UnitWeight * i.Value);
                int usedSlots = state.Cargo[id].Sum(i => R.Items[i.Key].UnitWeight == 0 ? 0 : sim.Stackable(i.Key) ? 1 : i.Value)
                    + cargo.Sum(i => sim.Stackable(i.Key) ? 1 : i.Value);
                if (before >= c.LimitLT * R.Settings.CharacterReceiveRatio || usedSlots >= c.Slots) continue;
                if (prefill && !TaggedTransportSimulator.IsElephant(id) && sim.Stackable(item.Key)
                    && before + left * weight >= c.LimitLT * R.Settings.CharacterReceiveRatio) {
                    foreach (var extra in overflow.Where(i => i.Key != item.Key && i.Value > 0)
                        .OrderByDescending(i => R.Items[i.Key].UnitWeight).ToArray()) {
                        double w = R.Items[extra.Key].UnitWeight;
                        if (w <= 0) continue;
                        int n = Math.Min(extra.Value, Math.Max(0, (int)Math.Floor((c.LimitLT * R.Settings.CharacterReceiveRatio - before - 0.01) / w)));
                        int availableSlots = c.Slots - usedSlots - 1;
                        n = sim.Stackable(extra.Key) ? availableSlots > 0 ? n : 0 : Math.Min(n, availableSlots);
                        if (n <= 0) continue;
                        cargo[extra.Key] = cargo.GetValueOrDefault(extra.Key) + n;
                        overflow[extra.Key] -= n; before += n * w;
                        usedSlots += sim.Stackable(extra.Key) ? 1 : n;
                    }
                }
                int count;
                if (TaggedTransportSimulator.IsElephant(id)) {
                    var owner = R.Settings.Carriers.Single(c => c.Id == TaggedTransportSimulator.Owner(id));
                    if (!sim.Stackable(item.Key) || owner.LimitLT - owner.OccupiedLT < weight) continue;
                    count = left;
                }
                else count = sim.Stackable(item.Key) ? left : Math.Min(left, Math.Min(c.Slots - usedSlots,
                    Math.Max(0, (int)Math.Floor((c.LimitLT * R.Settings.CharacterReceiveRatio - before) / weight))));
                if (count <= 0) continue;
                cargo[item.Key] = count; left -= count;
                if (left == 0) break;
            }
            overflow[item.Key] = left;
            if (left > 0) return false;
        }
        // Prepare mounts while their owners still have warehouse shuttle headroom.
        // A loaded elephant cannot be whistle-summoned at another island, so the
        // completed stack is transferred back to its owner before departure.
        foreach (var container in allocation.Where(c => TaggedTransportSimulator.IsElephant(c.Key)))
            foreach (var item in container.Value) {
                if (!Switch(TaggedTransportSimulator.Owner(container.Key)) || !Summon(container.Key)
                    || !Apply(new(TaggedActionKind.StackAtWarehouse, state.Location, wh, container.Key, item.Key, item.Value))) return false;
            }
        foreach (string id in new[] { "alt", "main" }) {
            if (allocation[id].Count == 0) continue;
            if (prefill) {
                // Keep a large stack together instead of leaving most of it on
                // the ship and using the character for only the overflow tail.
                var firstInput = R.Trades[jobs[0].TradeIndex].InputId;
                foreach (var stack in allocation[id].Where(i => sim.Stackable(i.Key) && i.Key != firstInput).ToArray()) {
                    int onShip = ship.GetValueOrDefault(stack.Key);
                    if (onShip > 0 && (stack.Value + onShip) * R.Items[stack.Key].UnitWeight <= R.ShipLimitLT - R.ShipOccupiedLT) {
                        allocation[id][stack.Key] += onShip;
                        ship.Remove(stack.Key);
                    }
                }
            }
            if (!Switch(id)) return false;
            // Fill the receiving headroom before the last stack takes the character
            // over the receive threshold. Keep the next exchange's input on board.
            var carrier = R.Settings.Carriers.Single(c => c.Id == id);
            double allocatedWeight = allocation[id].Sum(i => R.Items[i.Key].UnitWeight * i.Value);
            if (prefill && allocation[id].Count == 1 && allocation[id].Any(i => sim.Stackable(i.Key)) && sim.Weight(state, id) + allocatedWeight >= carrier.LimitLT * R.Settings.CharacterReceiveRatio) {
                var first = R.Trades[jobs[0].TradeIndex];
                foreach (var extra in ship.Where(i => !allocation[id].ContainsKey(i.Key) && R.Items[i.Key].UnitWeight > 0)
                    .OrderByDescending(i => R.Items[i.Key].UnitWeight).ToArray()) {
                    double weight = R.Items[extra.Key].UnitWeight;
                    int reserve = extra.Key == first.InputId ? first.InputPerExchange * jobs[0].Quantity : 0;
                    int count = Math.Min(extra.Value - reserve, (int)Math.Floor((carrier.LimitLT * R.Settings.CharacterReceiveRatio
                        - sim.Weight(state, id) - 0.01) / weight));
                    if (count <= 0) continue;
                    var before = state;
                    bool ok = Transfer(wh, "ship", extra.Key, count);
                    if (ok && sim.Stackable(extra.Key)) ok = Transfer("ship", id, extra.Key, count);
                    else if (ok) for (int n = 0; n < count && ok; n++) ok = Transfer("ship", id, extra.Key, 1);
                    var final = allocation[id].Single();
                    // Reserve the final stack's slot as well as its receive headroom.
                    if (!ok || !sim.FitsSlots(state, id) || state.Cargo[id].Sum(i => R.Items[i.Key].UnitWeight == 0 ? 0 : sim.Stackable(i.Key) ? 1 : i.Value)
                        + (state.Cargo[id].ContainsKey(final.Key) ? 0 : 1) > carrier.Slots) { state = before; continue; }
                    ship[extra.Key] -= count;
                    if (ship[extra.Key] == 0) ship.Remove(extra.Key);
                }
            }
            foreach (var item in allocation[id]) {
                // Warehouse withdrawal is subject to normal character LT. Use the empty
                // ship as a staging container when receiving one stack from a mount/ship.
                if (Transfer(wh, id, item.Key, item.Value)) continue;
                if (Transfer(wh, "ship", item.Key, item.Value)) {
                    if (sim.Stackable(item.Key)) { if (!Transfer("ship", id, item.Key, item.Value)) return false; }
                    else for (int n = 0; n < item.Value; n++) if (!Transfer("ship", id, item.Key, 1)) return false;
                    continue;
                }
                string elephant = id + "-elephant";
                if (state.Cargo[elephant].Count != 0 || !Summon(elephant)
                    || !Apply(new(TaggedActionKind.StackAtWarehouse, state.Location, wh, elephant, item.Key, item.Value))
                    || !Transfer(elephant, id, item.Key, item.Value)) return false;
            }
        }
        foreach (var container in allocation.Where(c => TaggedTransportSimulator.IsElephant(c.Key))) {
            string owner = TaggedTransportSimulator.Owner(container.Key);
            if (!Switch(owner)) return false;
            foreach (var item in container.Value)
                if (!Transfer(container.Key, owner, item.Key, item.Value)) return false;
        }
        if (!Switch("main")) return false;
        foreach (var item in ship) if (!Transfer(wh, "ship", item.Key, item.Value)) return false;
        return true;
    }

    private bool Rebalance(TaggedVoyageJob job, IReadOnlyList<TaggedVoyageJob> future) {
        state = TaggedTerminalSales.SellAvailable(sim, state);
        var needed = future.Select(j => R.Trades[j.TradeIndex].InputId).ToHashSet(StringComparer.Ordinal);
        var port = sim.Port(state.Location)!;
        if (port.WarehouseId.Length > 0 && !KeepCargoHere && !UnloadTerminal(needed)) return false;
        var trade = R.Trades[job.TradeIndex]; int quantity = checked(trade.InputPerExchange * job.Quantity);
        for (int guard = 0; guard < 2000 && !stopped(); guard++) {
            if (Ready(job, normalOutput: true)) return Switch("main");
            int missing = quantity - sim.Count(state, "ship", trade.InputId);
            if (missing > 0 && Pull(trade.InputId, missing)) continue;
            double extra = Math.Max(0, missing) * (double)R.Items[trade.InputId].UnitWeight;
            double delta = (double)trade.OutputPerExchange * job.Quantity * R.Items[trade.OutputId].UnitWeight
                - (double)quantity * R.Items[trade.InputId].UnitWeight;
            double deficit = Math.Max(1, sim.Weight(state, "ship") + extra + Math.Max(0, delta) - R.ShipLimitLT);
            if (Park(trade.InputId, quantity, needed, deficit)) continue;
            return Ready(job, normalOutput: false) && Switch("main");
        }
        return false;
    }

    private bool Pull(string item, int missing) {
        double weight = R.Items[item].UnitWeight;
        int room = weight == 0 ? missing : Math.Max(0, (int)Math.Floor((R.ShipLimitLT - sim.Weight(state, "ship")) / weight));
        if (room <= 0) return false;
        foreach (string id in new[] { "alt", "main", "alt-elephant", "main-elephant" }) {
            int n = Math.Min(missing, Math.Min(room, sim.Count(state, id, item)));
            if (n <= 0) continue;
            var before = state;
            string owner = TaggedTransportSimulator.Owner(id);
            if (!Switch(owner)) { state = before; continue; }
            if (TaggedTransportSimulator.IsElephant(id)) {
                if (!sim.Stackable(item)) n = 1;
                if (!Summon(id) || !Transfer(id, owner, item, n)) { state = before; continue; }
            }
            if (Transfer(owner, "ship", item, n)) return true;
            state = before;
        }
        string warehouse = sim.Port(state.Location)!.WarehouseId;
        if (warehouse.Length > 0) {
            string wh = TaggedTransportSimulator.Warehouse(warehouse);
            int n = Math.Min(missing, Math.Min(room, sim.Count(state, wh, item)));
            if (n > 0 && Transfer(wh, "ship", item, n)) return true;
        }
        return false;
    }

    private bool Park(string input, int quantity, HashSet<string> needed, double deficit, string? onlyItem = null) {
        var beforeSale = state;
        state = TaggedTerminalSales.SellAvailable(sim, state);
        if (sim.Weight(state, "ship") < sim.Weight(beforeSale, "ship")) return true;
        if (PackCharacter(input, quantity, onlyItem)) return true;
        foreach (var item in state.Cargo["ship"].Where(i => R.Items[i.Key].UnitWeight > 0
            && !sim.IsTerminalLevelSeven(state, i.Key)
            && (onlyItem is null || i.Key == onlyItem) && i.Value > (i.Key == input ? quantity : 0)).OrderBy(i => needed.Contains(i.Key)).ThenByDescending(i => R.Items[i.Key].UnitWeight).ToArray()) {
            int available = item.Value - (item.Key == input ? quantity : 0);
            int n = sim.Stackable(item.Key) ? (int)Math.Min(available, Math.Max(1, Math.Ceiling(deficit / R.Items[item.Key].UnitWeight))) : 1;
            foreach (string owner in new[] { "alt", "main" }) {
                var before = state;
                if (Switch(owner) && Transfer("ship", owner, item.Key, n)) return true;
                state = before;
            }
        }
        return false;
    }

    private bool PackCharacter(string input, int quantity, string? onlyItem) {
        var original = state;
        var available = state.Cargo["ship"].Where(i => R.Items[i.Key].UnitWeight > 0
            && !sim.IsTerminalLevelSeven(state, i.Key) && (onlyItem is null || i.Key == onlyItem))
            .Select(i => new KeyValuePair<string, int>(i.Key, i.Value - (i.Key == input ? quantity : 0)))
            .Where(i => i.Value > 0).ToArray();
        // Finish the TAG character's entire useful batch before considering the
        // main character. This minimizes character switches and matches the
        // user's preferred handling order.
        foreach (string owner in new[] { "alt", "main" }) {
            TaggedTransportState? best = null;
            double bestWeight = sim.Weight(original, "ship");
            var c = R.Settings.Carriers.Single(c => c.Id == owner);
            foreach (var final in available.Where(i => sim.Stackable(i.Key))) {
                state = original;
                if (!Switch(owner)) continue;
                // Reserve one slot for the final distinct stack, and verify every
                // receiving operation through the same simulator used for routes.
                foreach (var item in available.Where(i => i.Key != final.Key).OrderByDescending(i => R.Items[i.Key].UnitWeight)) {
                    double weight = R.Items[item.Key].UnitWeight;
                    int count = Math.Min(item.Value, Math.Max(0, (int)Math.Floor(
                        (c.LimitLT * R.Settings.CharacterReceiveRatio - sim.Weight(state, owner) - 0.01) / weight)));
                    if (count == 0) continue;
                    var before = state;
                    bool ok = true;
                    if (sim.Stackable(item.Key)) ok = Transfer("ship", owner, item.Key, count);
                    else for (int n = 0; n < count && ok; n++) ok = Transfer("ship", owner, item.Key, 1);
                    if (!ok || !sim.TryApply(state, new(TaggedActionKind.Transfer, state.Location, "ship", owner, final.Key, final.Value), out _, out _)) state = before;
                }
                if (Transfer("ship", owner, final.Key, final.Value) && sim.Weight(state, "ship") < bestWeight) {
                    best = state; bestWeight = sim.Weight(state, "ship");
                }
            }
            if (best is not null) { state = best; return true; }
        }
        state = original;
        return false;
    }

    private bool UnloadTerminal(HashSet<string> needed) {
        state = TaggedTerminalSales.SellAvailable(sim, state);
        string wh = TaggedTransportSimulator.Warehouse(sim.Port(state.Location)!.WarehouseId);
        foreach (string source in new[] { "ship", "alt", "main" }) {
            var goods = state.Cargo[source].Where(i => R.Items[i.Key].UnitWeight > 0 && !needed.Contains(i.Key)
                && !sim.IsTerminalLevelSeven(state, i.Key)).ToArray();
            if (goods.Length == 0) continue;
            if (source != "ship" && !Switch(source)) return false;
            foreach (var item in goods) if (!Transfer(source, wh, item.Key, item.Value)) return false;
        }
        state = TaggedTerminalSales.SellAvailable(sim, state);
        return Switch("main");
    }

    private bool UnloadAll() {
        if (sim.Port(state.Location)?.WarehouseId.Length is not > 0) return false;
        state = TaggedTerminalSales.SellAvailable(sim, state);
        string wh = TaggedTransportSimulator.Warehouse(sim.Port(state.Location)!.WarehouseId);
        foreach (var item in state.Cargo["ship"].Where(i => R.Items[i.Key].UnitWeight > 0 && !sim.IsTerminalLevelSeven(state, i.Key)).ToArray())
            if (!Transfer("ship", wh, item.Key, item.Value)) return false;
        foreach (string owner in new[] { "alt", "main" }) {
            string mount = owner + "-elephant";
            if (state.Cargo[owner].Count == 0 && state.Cargo[mount].Count == 0) continue;
            if (!Switch(owner)) return false;
            foreach (var item in state.Cargo[owner].Where(i => R.Items[i.Key].UnitWeight > 0 && !sim.IsTerminalLevelSeven(state, i.Key)).ToArray())
                if (!Transfer(owner, wh, item.Key, item.Value)) return false;
            foreach (var item in state.Cargo[mount].Where(i => R.Items[i.Key].UnitWeight > 0).ToArray()) {
                if (!Summon(mount)) return false;
                int batch = sim.Stackable(item.Key) ? item.Value : 1;
                for (int left = item.Value; left > 0; left -= batch) {
                    if (!Transfer(mount, owner, item.Key, batch)) return false;
                    if (sim.IsTerminalLevelSeven(state, item.Key)) state = TaggedTerminalSales.SellAvailable(sim, state);
                    else if (!Transfer(owner, wh, item.Key, batch)) return false;
                }
            }
        }
        state = TaggedTerminalSales.SellAvailable(sim, state);
        if (state.Cargo.Where(c => !TaggedTransportSimulator.IsWarehouse(c.Key))
            .Any(c => c.Value.Any(i => i.Value > 0 && sim.IsTerminalLevelSeven(state, i.Key)))) return false;
        return Switch("main");
    }
    private bool Ready(TaggedVoyageJob job, bool normalOutput) {
        var t = R.Trades[job.TradeIndex];
        if (normalOutput && sim.Weight(state, "ship") + ((double)t.OutputPerExchange * R.Items[t.OutputId].UnitWeight
            - (double)t.InputPerExchange * R.Items[t.InputId].UnitWeight) * job.Quantity > R.ShipLimitLT) return false;
        // Feasibility checks must not emit a character switch. Rebalance emits
        // the single required switch after the whole wharf-handling batch.
        var arrival = state;
        if (arrival.Active != "main") { arrival = arrival.Copy(); arrival.Active = "main"; }
        if (arrival.Location != t.IslandId && !sim.TryApply(arrival,
            new(TaggedActionKind.Sail, t.IslandId, arrival.Location, t.IslandId), out arrival, out _)) return false;
        return sim.TryApply(arrival, new(TaggedActionKind.Barter, t.IslandId, Quantity: job.Quantity, TradeIndex: job.TradeIndex), out _, out _);
    }
    private bool Switch(string owner) => state.Active == owner || Apply(new(TaggedActionKind.Switch, state.Location, state.Active, owner));
    private bool Summon(string mount) => state.SummonedElephants.Contains(mount) || Apply(new(TaggedActionKind.SummonElephant, state.Location, state.Active, mount));
    private bool Move(string island) => state.Location == island || Switch("main") && Apply(new(TaggedActionKind.Sail, island, state.Location, island));
    private bool Transfer(string from, string to, string item, int n) => Apply(new(TaggedActionKind.Transfer, state.Location, from, to, item, n));
    private bool Apply(TaggedAction action) {
        if (stopped() || !sim.TryApply(state, action, out var next, out _)) return false;
        state = next; return true;
    }
}
