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
        if (prepareAtWarehouse && ((!KeepCargoHere && !UnloadAll()) || !Prepare(preloadJobs ?? jobs, packing))) return null;
        for (int index = 0; index < jobs.Count; index++) {
            if (stopped()) return null;
            var job = jobs[index]; var trade = R.Trades[job.TradeIndex];
            LastFailure = "preparing exchange at " + trade.IslandId;
            var future = jobs.Skip(index).ToArray();
            var before = state;
            TaggedTransportState? selected = null;
            // First try the direct leg. If cargo needs handling, enumerate only real ports.
            if (Move(trade.IslandId) && Ready(job, normalOutput: packing < 3 || !R.Settings.AllowOverloadedSailing)) selected = state;
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
            foreach (var item in state.Cargo["ship"].Where(i => R.Items[i.Key].UnitWeight > 0 && !needed.Contains(i.Key)).ToArray())
                while (sim.Count(state, "ship", item.Key) > 0 && Park("", 0, needed, double.MaxValue, item.Key)) { }
        }
        else if (!UnloadAll()) return null;
        if (!Switch("main")) return null;
        return state;
    }

    private bool KeepCargoHere => R.Settings.HomeWarehouseId is { Length: > 0 } home && sim.Port(state.Location)?.WarehouseId != home;

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
            current = Compile(current, jobs, steps[route.End - 1].Action.Location, preloadJobs: preload);
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

    private bool Prepare(IReadOnlyList<TaggedVoyageJob> jobs, int packing) {
        string wh = TaggedTransportSimulator.Warehouse(sim.Port(state.Location)!.WarehouseId);
        var requirements = Required(jobs);
        if (requirements.Any(i => sim.Count(state, wh, i.Key) < i.Value)) return false;
        var ship = new Dictionary<string, int>();
        var overflow = new Dictionary<string, int>();
        double free = R.ShipLimitLT - sim.Weight(state, "ship");
        int slots = R.Settings.ShipSlots - state.Cargo["ship"].Sum(i => R.Items[i.Key].UnitWeight == 0 ? 0 : sim.Stackable(i.Key) ? 1 : i.Value);
        foreach (var item in requirements) {
            int weight = R.Items[item.Key].UnitWeight;
            int count = weight == 0 ? item.Value : Math.Min(item.Value, Math.Max(0, (int)Math.Floor(free / weight)));
            if (weight > 0) count = Math.Min(count, sim.Stackable(item.Key) ? slots > 0 ? count : 0 : slots);
            if (count > 0) { ship[item.Key] = count; free -= (double)weight * count; if (weight > 0) slots -= sim.Stackable(item.Key) ? 1 : count; }
            if (count < item.Value) overflow[item.Key] = item.Value - count;
        }
        var allocation = R.Settings.Carriers.ToDictionary(c => c.Id, _ => new Dictionary<string, int>());
        foreach (var item in overflow) {
            int left = item.Value; int weight = R.Items[item.Key].UnitWeight;
            foreach (string id in carrierOrders[packing % carrierOrders.Length]) {
                var c = R.Settings.Carriers.Single(c => c.Id == id); var cargo = allocation[id];
                double before = sim.Weight(state, id) + cargo.Sum(i => (double)R.Items[i.Key].UnitWeight * i.Value);
                int usedSlots = state.Cargo[id].Sum(i => R.Items[i.Key].UnitWeight == 0 ? 0 : sim.Stackable(i.Key) ? 1 : i.Value)
                    + cargo.Sum(i => sim.Stackable(i.Key) ? 1 : i.Value);
                if (before >= c.LimitLT * R.Settings.CharacterReceiveRatio || usedSlots >= c.Slots) continue;
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
            if (left > 0) return false;
        }
        // Prepare mounts while their owners still have warehouse shuttle headroom.
        foreach (var container in allocation.Where(c => TaggedTransportSimulator.IsElephant(c.Key)))
            foreach (var item in container.Value) {
                if (!Switch(TaggedTransportSimulator.Owner(container.Key)) || !Summon(container.Key)
                    || !Apply(new(TaggedActionKind.StackAtWarehouse, state.Location, wh, container.Key, item.Key, item.Value))) return false;
            }
        foreach (string id in new[] { "alt", "main" }) {
            if (allocation[id].Count == 0) continue;
            if (!Switch(id)) return false;
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
            if (!Switch("main")) return false;
            if (Ready(job, normalOutput: true)) return true;
            int missing = quantity - sim.Count(state, "ship", trade.InputId);
            if (missing > 0 && Pull(trade.InputId, missing)) continue;
            double extra = Math.Max(0, missing) * (double)R.Items[trade.InputId].UnitWeight;
            double delta = (double)trade.OutputPerExchange * job.Quantity * R.Items[trade.OutputId].UnitWeight
                - (double)quantity * R.Items[trade.InputId].UnitWeight;
            double deficit = Math.Max(1, sim.Weight(state, "ship") + extra + Math.Max(0, delta) - R.ShipLimitLT);
            if (Park(trade.InputId, quantity, needed, deficit)) continue;
            return Switch("main") && Ready(job, normalOutput: false);
        }
        return false;
    }

    private bool Pull(string item, int missing) {
        int weight = R.Items[item].UnitWeight;
        int room = weight == 0 ? missing : Math.Max(0, (int)Math.Floor((R.ShipLimitLT - sim.Weight(state, "ship")) / weight));
        if (room <= 0) return false;
        foreach (string id in new[] { "main", "alt", "main-elephant", "alt-elephant" }) {
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
        foreach (var item in state.Cargo["ship"].Where(i => R.Items[i.Key].UnitWeight > 0
            && !sim.IsTerminalLevelSeven(state, i.Key)
            && (onlyItem is null || i.Key == onlyItem) && i.Value > (i.Key == input ? quantity : 0)).OrderBy(i => needed.Contains(i.Key)).ThenByDescending(i => R.Items[i.Key].UnitWeight).ToArray()) {
            int available = item.Value - (item.Key == input ? quantity : 0);
            int n = sim.Stackable(item.Key) ? (int)Math.Min(available, Math.Max(1, Math.Ceiling(deficit / R.Items[item.Key].UnitWeight))) : 1;
            foreach (string owner in new[] { "main", "alt" }) {
                var before = state;
                if (Switch(owner) && Transfer("ship", owner, item.Key, n)) return true;
                state = before;
            }
            foreach (string owner in new[] { "main", "alt" }) {
                var before = state; string mount = owner + "-elephant";
                if (Switch(owner) && Summon(mount) && Transfer("ship", owner, item.Key, n) && Transfer(owner, mount, item.Key, n)) return true;
                state = before;
            }
        }
        return false;
    }

    private bool UnloadTerminal(HashSet<string> needed) {
        state = TaggedTerminalSales.SellAvailable(sim, state);
        string wh = TaggedTransportSimulator.Warehouse(sim.Port(state.Location)!.WarehouseId);
        foreach (string source in new[] { "ship", "main", "alt" }) {
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
        foreach (string owner in new[] { "main", "alt" }) {
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
        if (state.Active != "main") return false;
        var t = R.Trades[job.TradeIndex];
        if (normalOutput && sim.Weight(state, "ship") + ((double)t.OutputPerExchange * R.Items[t.OutputId].UnitWeight
            - (double)t.InputPerExchange * R.Items[t.InputId].UnitWeight) * job.Quantity > R.ShipLimitLT) return false;
        var arrival = state;
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
