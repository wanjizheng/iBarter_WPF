using iBarter.Navigation;

namespace iBarter.Routing;

/// <summary>All search, execution and restore paths use these same sequential inventory rules.</summary>
public sealed class TaggedTransportSimulator(TaggedTransportRequest request) {
    private readonly Dictionary<(string, string), double> distances = new();
    public TaggedTransportRequest Request => request;
    public TaggedPort? Port(string location) => request.Settings.Ports.FirstOrDefault(x => x.Enabled && x.IslandId == location);
    public static string Warehouse(string id) => "warehouse:" + id;
    public static bool IsWarehouse(string id) => id.StartsWith("warehouse:", StringComparison.Ordinal);
    public static bool IsElephant(string id) => id.EndsWith("-elephant", StringComparison.Ordinal);
    public static string Owner(string id) => id.Split('-')[0];
    public bool Stackable(string item) => request.Items[item].Level < 5;
    public double Weight(TaggedTransportState s, string container) => s.Cargo[container].Sum(x => (double)request.Items[x.Key].UnitWeight * x.Value)
        + (container == "ship" ? request.ShipOccupiedLT : request.Settings.Carriers.FirstOrDefault(x => x.Id == container)?.OccupiedLT ?? 0);
    public int Count(TaggedTransportState s, string container, string item) => s.Cargo[container].GetValueOrDefault(item);
    public bool IsTerminalLevelSeven(TaggedTransportState s, string item) => request.Items.TryGetValue(item, out var metadata)
        && metadata.Level == 7 && !request.Trades.Where((t, i) => s.Remaining[i] > 0).Any(t => t.InputId == item);

    public string? Validate() {
        var c = request.Settings;
        if (c.SearchProfile is { } profile && (profile.TotalTarget <= TimeSpan.Zero || profile.MaxBeamParents <= 0)) return "Invalid search profile.";
        if (!double.IsFinite(request.ShipLimitLT) || request.ShipLimitLT <= 0
            || !double.IsFinite(request.ShipOccupiedLT) || request.ShipOccupiedLT < 0 || request.ShipOccupiedLT >= request.ShipLimitLT)
            return "Invalid ship capacity.";
        if (!request.Points.ContainsKey(c.StartIsland) || c.ActiveCharacter is not ("main" or "alt")) return "Invalid departure location or active character.";
        if (c.Carriers.Length != 4 || !c.Carriers.Select(x => x.Id).Order().SequenceEqual(new[] { "alt", "alt-elephant", "main", "main-elephant" })) return "Four distinct carriers are required.";
        if (c.Carriers.Any(x => !double.IsFinite(x.LimitLT) || x.LimitLT <= 0 || !double.IsFinite(x.OccupiedLT)
            || x.OccupiedLT < 0 || x.Slots <= 0 || x.Slots > 200)) return "Invalid character or elephant capacity.";
        if (new[] { c.NormalMetersPerSecond, c.OverloadedMetersPerSecond, c.DockSeconds, c.SwitchSeconds,
            c.TransferSeconds, c.BarterSeconds, c.ElephantSummonSeconds }.Any(x => !double.IsFinite(x) || x <= 0)
            || c.OverloadedMetersPerSecond > c.NormalMetersPerSecond || c.ShipSlots <= 0
            || !double.IsFinite(c.BarterOutputRatio) || c.BarterOutputRatio < 1 || c.BarterOutputRatio > 1.7
            || !double.IsFinite(c.CharacterReceiveRatio) || c.CharacterReceiveRatio < 1 || c.CharacterReceiveRatio > 1.7
            || c.SearchSeconds is < 1 or > 300 || c.MaxStates is < 1 or > 500000) return "Invalid time or loading assumptions.";
        if (request.Items.Any(x => x.Key != x.Value.ItemId || x.Value.UnitWeight < 0)) return "Invalid item catalog.";
        if (request.Trades.Length == 0 || request.Trades.Any(x => !request.Points.TryGetValue(x.IslandId, out var p) || !p.IsFinite
            || !request.Items.ContainsKey(x.InputId) || !request.Items.ContainsKey(x.OutputId)
            || x.InputPerExchange <= 0 || x.OutputPerExchange <= 0 || x.Exchanges is <= 0 or > 1000)) return "Invalid or empty exchange list.";
        if (request.Trades.Select(x => x.RowId).Distinct().Count() != request.Trades.Length) return "Duplicate exchange identity.";
        if (c.Ports.Where(x => x.Enabled).GroupBy(x => x.IslandId).Any(x => x.Count() > 1)) return "Duplicate port.";
        if (c.Ports.Where(x => x.Enabled).Any(x => !request.Points.TryGetValue(x.IslandId, out var p) || !p.IsFinite
            || (x.WarehouseId.Length > 0 && !request.Warehouses.ContainsKey(x.WarehouseId)))) return "An enabled port has no coordinates or warehouse.";
        if (c.Ports.Any(x => x.Enabled && x.WarehouseId == "Ancado" && x.IslandId != "Ancado")) return "Ancado storage cannot be linked to another island.";
        if (!c.Ports.Any(x => x.Enabled && x.WarehouseId.Length > 0)) return "At least one warehouse port is required.";
        if (c.HomeWarehouseId is { Length: > 0 } home && !c.Ports.Any(p => p.Enabled && p.WarehouseId == home)) return "The home warehouse must have an enabled port.";
        if (request.Warehouses.Values.Any(w => w.Any(x => x.Value < 0 || !request.Items.ContainsKey(x.Key)))) return "Invalid warehouse stock.";
        foreach (var entry in c.InitialCargo)
            if (entry.Container != "ship" && !c.Carriers.Any(x => x.Id == entry.Container)
                || !request.Items.ContainsKey(entry.ItemId) || entry.Quantity <= 0) return "Invalid initial cargo.";
        return null;
    }

    public TaggedTransportState Initial() {
        var state = new TaggedTransportState { Location = request.Settings.StartIsland, Active = request.Settings.ActiveCharacter,
            Remaining = request.Trades.Select(x => x.Exchanges).ToArray() };
        state.Cargo["ship"] = new();
        foreach (var c in request.Settings.Carriers) state.Cargo[c.Id] = new();
        foreach (var w in request.Warehouses) state.Cargo[Warehouse(w.Key)] = new(w.Value);
        foreach (var e in request.Settings.InitialCargo) Add(state.Cargo[e.Container], e.ItemId, e.Quantity);
        return state;
    }

    public double Distance(string from, string to) {
        if (from == to) return 0;
        if (distances.TryGetValue((from, to), out double d)) return d;
        var a = request.Points[from]; var b = request.Points[to];
        return distances[(from, to)] = ShippingCorridorGraph.Distance(from, new NavigationPoint(a.X, a.Y),
            to, new NavigationPoint(b.X, b.Y)) / 100.0;
    }

    public bool Complete(TaggedTransportState s) => s.Remaining.All(x => x == 0)
        && s.Cargo.Where(x => !IsWarehouse(x.Key)).All(x => x.Value.All(i => request.Items[i.Key].UnitWeight == 0))
        && (request.Settings.HomeWarehouseId is not { Length: > 0 } home ||
            s.Cargo.Where(c => IsWarehouse(c.Key) && c.Key != Warehouse(home)).All(c =>
                c.Value.All(i => request.Items[i.Key].UnitWeight == 0 || i.Value <= request.Warehouses[c.Key[10..]].GetValueOrDefault(i.Key))));

    public bool TryApply(TaggedTransportState s, TaggedAction action, out TaggedTransportState next, out string error) {
        next = s; error = "Illegal action or insufficient receiving space.";
        if (!request.Points.ContainsKey(action.Location)) return false;
        double duration = 0, distance = 0;
        if (action.Kind == TaggedActionKind.Sail) {
            if (action.Location == s.Location || s.Active != "main") return false;
            bool overweight = Weight(s, "ship") > request.ShipLimitLT;
            if (overweight && !request.Settings.AllowOverloadedSailing) return false;
            distance = Distance(s.Location, action.Location);
            duration = distance / (overweight ? request.Settings.OverloadedMetersPerSecond : request.Settings.NormalMetersPerSecond)
                + (Port(action.Location) is null ? 0 : request.Settings.DockSeconds);
            next = s.Copy(); next.Location = action.Location; next.Distance += distance; next.SailingSeconds += duration;
            if (overweight) next.OverloadedDistance += distance;
            next.SummonedElephants.Clear();
        }
        else {
            if (action.Location != s.Location) return false;
            var port = Port(s.Location);
            if (action.Kind == TaggedActionKind.Switch) {
                if (port is null || action.To is not ("main" or "alt") || action.To == s.Active || action.From != s.Active) return false;
                next = s.Copy(); next.Active = action.To; duration = request.Settings.SwitchSeconds;
            }
            else if (action.Kind == TaggedActionKind.SummonElephant) {
                if (port is null || action.To != s.Active + "-elephant" || s.SummonedElephants.Contains(action.To)) return false;
                next = s.Copy(); next.SummonedElephants.Add(action.To);
                duration = request.Settings.ElephantSummonSeconds;
            }
            else if (action.Kind == TaggedActionKind.Barter) {
                if (s.Active != "main" || action.TradeIndex < 0 || action.TradeIndex >= request.Trades.Length) return false;
                var trade = request.Trades[action.TradeIndex];
                int n = action.Quantity;
                if (trade.IslandId != s.Location || n <= 0 || n > s.Remaining[action.TradeIndex]) return false;
                long input = (long)n * trade.InputPerExchange, output = (long)n * trade.OutputPerExchange;
                double before = Weight(s, "ship");
                // The button's precondition is independent from the output-overweight allowance.
                if (before > request.ShipLimitLT || input > Count(s, "ship", trade.InputId) || output > int.MaxValue) return false;
                double after = before - input * request.Items[trade.InputId].UnitWeight + output * request.Items[trade.OutputId].UnitWeight;
                if (after > request.ShipLimitLT * request.Settings.BarterOutputRatio) return false;
                next = s.Copy("ship"); Add(next.Cargo["ship"], trade.InputId, -(int)input); Add(next.Cargo["ship"], trade.OutputId, (int)output);
                if (!FitsSlots(next, "ship")) { next = s; return false; }
                next.Remaining[action.TradeIndex] -= n; duration = request.Settings.BarterSeconds;
            }
            else if (action.Kind == TaggedActionKind.Sell) {
                if (port is null || action.To != "shop" || action.Quantity <= 0 || action.From != "ship" && action.From != s.Active
                    || !IsTerminalLevelSeven(s, action.ItemId) || Count(s, action.From, action.ItemId) < action.Quantity) return false;
                // One user-facing sale can include taking out and selling items
                // one at a time. The temporary receiving slot/weight must exist.
                if (action.From == "ship") {
                    if (!CanReceive(s, s.Active, action.ItemId, 1)) return false;
                    var receiving = s.Copy(s.Active); Add(receiving.Cargo[s.Active], action.ItemId, 1);
                    if (!FitsSlots(receiving, s.Active)) return false;
                }
                next = s.Copy(action.From); Add(next.Cargo[action.From], action.ItemId, -action.Quantity);
                next.SoldItems[action.ItemId] = checked(next.SoldItems.GetValueOrDefault(action.ItemId) + action.Quantity);
                duration = request.Settings.TransferSeconds * action.Quantity;
            }
            else if (action.Kind is TaggedActionKind.Transfer or TaggedActionKind.StackAtWarehouse) {
                if (port is null || action.From == action.To || action.Quantity <= 0
                    || !s.Cargo.ContainsKey(action.From) || !s.Cargo.ContainsKey(action.To)
                    || !request.Items.ContainsKey(action.ItemId) || Count(s, action.From, action.ItemId) < action.Quantity
                    || !Accessible(s, action.From, port) || !Accessible(s, action.To, port)) return false;
                bool stack = action.Kind == TaggedActionKind.StackAtWarehouse;
                if (stack) {
                    // A warehouse shuttle is a macro, restricted to the user's local elephant.
                    if (action.From != Warehouse(port.WarehouseId) || !IsElephant(action.To) || !Stackable(action.ItemId)) return false;
                    double carry = request.Settings.Carriers.First(x => x.Id == s.Active).LimitLT - Weight(s, s.Active);
                    int weight = request.Items[action.ItemId].UnitWeight;
                    var elephant = request.Settings.Carriers.First(x => x.Id == action.To);
                    double otherElephantWeight = Weight(s, action.To) - Count(s, action.To, action.ItemId) * (double)weight;
                    var transient = s.Copy(s.Active); Add(transient.Cargo[s.Active], action.ItemId, 1);
                    if (weight <= 0 || carry < weight || !FitsSlots(transient, s.Active)
                        || otherElephantWeight >= elephant.LimitLT * request.Settings.CharacterReceiveRatio) return false;
                    // Each batch can require withdrawal, retrieving the existing elephant
                    // stack, putting the combined stack back, and returning to storage.
                    duration = Math.Ceiling(action.Quantity / Math.Floor(carry / weight)) * request.Settings.TransferSeconds * 4;
                }
                else {
                    // No direct main/alt exchange, no remote mounts, no warehouse-to-warehouse moves.
                    if (action.From != s.Active && action.To != s.Active
                        && !((action.From == "ship" && IsWarehouse(action.To)) || (action.To == "ship" && IsWarehouse(action.From)))) return false;
                    if (!CanReceive(s, action.To, action.ItemId, action.Quantity)) return false;
                    if (IsWarehouse(action.From) && action.To == s.Active && Weight(s, action.To)
                        + (double)request.Items[action.ItemId].UnitWeight * action.Quantity
                        > request.Settings.Carriers.First(x => x.Id == s.Active).LimitLT) return false;
                    duration = request.Settings.TransferSeconds;
                }
                next = s.Copy(action.From, action.To);
                Add(next.Cargo[action.From], action.ItemId, -action.Quantity);
                Add(next.Cargo[action.To], action.ItemId, action.Quantity);
                if (!FitsSlots(next, action.To)) { next = s; return false; }
            }
            else return false;
        }
        next.Seconds += duration;
        next.Steps.Add(new(action, duration, next.Seconds, Weight(next, "ship"), Weight(next, "main"), Weight(next, "alt")));
        error = ""; return true;
    }

    private bool Accessible(TaggedTransportState s, string id, TaggedPort port) => id == "ship" || id == s.Active
        || (IsWarehouse(id) && port.WarehouseId.Length > 0 && id == Warehouse(port.WarehouseId))
        || (IsElephant(id) && Owner(id) == s.Active && s.SummonedElephants.Contains(id));
    private bool CanReceive(TaggedTransportState s, string target, string item, int quantity) {
        if (IsWarehouse(target)) return true;
        double before = Weight(s, target), added = (double)request.Items[item].UnitWeight * quantity;
        if (target == "ship") return before + added <= request.ShipLimitLT;
        var carrier = request.Settings.Carriers.First(x => x.Id == target);
        double limit = carrier.LimitLT * request.Settings.CharacterReceiveRatio;
        // Only a genuine stack can cross the receive threshold in one operation.
        // Individual goods need both a slot and enough remaining weight headroom.
        return before < limit && (Stackable(item) || quantity == 1 && before + added <= limit);
    }
    public bool FitsSlots(TaggedTransportState s, string id) => IsWarehouse(id) || s.Cargo[id].Sum(x =>
        request.Items[x.Key].UnitWeight == 0 ? 0 : Stackable(x.Key) ? 1 : x.Value)
        <= (id == "ship" ? request.Settings.ShipSlots : request.Settings.Carriers.First(x => x.Id == id).Slots);
    private static void Add(Dictionary<string, int> cargo, string id, int delta) {
        int value = checked(cargo.GetValueOrDefault(id) + delta);
        if (value == 0) cargo.Remove(id); else cargo[id] = value;
    }

    public bool Verify(TaggedTransportPlan plan, out TaggedTransportState state, out string error, int? prefix = null) {
        error = Validate() ?? ""; state = new();
        if (error.Length > 0 || plan.Fingerprint != request.Fingerprint()) { error = "Settings or inventory changed."; return false; }
        state = Initial();
        var initial = state;
        if (!initial.Cargo.Keys.All(x => FitsSlots(initial, x)) || Weight(initial, "ship") > request.ShipLimitLT * request.Settings.BarterOutputRatio) {
            error = "Initial cargo exceeds available slots or ship allowance."; return false;
        }
        int count = prefix ?? plan.Steps.Length;
        if (count < 0 || count > plan.Steps.Length) { error = "Invalid progress."; return false; }
        foreach (var step in plan.Steps.Take(count)) {
            if (!TryApply(state, step.Action, out var next, out error)) return false;
            if (next.Steps[^1] != step) { error = "Cargo or time snapshot mismatch."; return false; }
            state = next;
        }
        if (prefix is null && (!Complete(state) || Math.Abs(state.Seconds - plan.TotalSeconds) > 0.001
            || Math.Abs(state.SailingSeconds - plan.SailingSeconds) > 0.001 || Math.Abs(state.Distance - plan.Distance) > 0.001
            || Math.Abs(state.Seconds - state.SailingSeconds - plan.HandlingSeconds) > 0.001)) { error = "Incomplete route or time mismatch."; return false; }
        return true;
    }
}
