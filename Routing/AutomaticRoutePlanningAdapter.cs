namespace iBarter.Routing;

public sealed record PlannerRouteSnapshot(
    string RowId, bool ExchangeDone, int ExchangeQuantity,
    string IslandId,
    string Item1Id, string Item1DisplayName, int Item1Level, int Item1Number,
    string Item2Id, string Item2DisplayName, int Item2Level, int Item2Number);

public sealed record StorageItemSnapshot(
    string ItemId, int Level,
    int Velia, int Iliya, int Epheria, int Ancado);

public sealed record IslandRouteSnapshot(string IslandId, RoutePoint Point);
public sealed record CargoCapacitySnapshot(int ExtraLT, int TotalLT);

public static class AutomaticRoutePlanningAdapter {
    private static readonly (string WarehouseId, string IslandId)[] WarehouseMap = [
        ("Velia", "Velia"),
        ("Iliya", "Iliya"),
        ("Epheria", "Epheria"),
        ("Ancado", "Sausan"),
    ];

    public static AutomaticRoutePlanningRequest BuildRequest(
        IReadOnlyList<PlannerRouteSnapshot> plannerRows,
        IReadOnlyList<StorageItemSnapshot> storageItems,
        IReadOnlyList<IslandRouteSnapshot> islands,
        CargoCapacitySnapshot cargo,
        RouteSearchLimits limits) {
        return BuildRequest(plannerRows, storageItems, islands, cargo, limits,
            RouteOptimizationProfile.For(RouteOptimizationMode.Balanced));
    }

    // Overload that carries the user-selected optimization profile through to the
    // planner. The profile's MaxLocalEvaluations is forwarded as the request's
    // MaxLocalMoves so the optimizers still see a per-call cap when a budget is
    // not provided.
    public static AutomaticRoutePlanningRequest BuildRequest(
        IReadOnlyList<PlannerRouteSnapshot> plannerRows,
        IReadOnlyList<StorageItemSnapshot> storageItems,
        IReadOnlyList<IslandRouteSnapshot> islands,
        CargoCapacitySnapshot cargo,
        RouteSearchLimits limits,
        RouteOptimizationProfile profile) {
        var points = islands
            .GroupBy(x => x.IslandId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First().Point, StringComparer.Ordinal);
        var activeRows = plannerRows.Where(x => x.ExchangeQuantity > 0 && !x.ExchangeDone).ToArray();

        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal);
        foreach (var storage in storageItems.Where(x => !string.IsNullOrWhiteSpace(x.ItemId)))
            items[storage.ItemId] = new RouteItem(
                storage.ItemId, storage.ItemId, storage.Level, CargoWeightTable.GetWeightForLevel(storage.Level));
        // Completed rows can leave their net positive output on board.  Keep
        // their item metadata too, even when storage has no record of it.
        foreach (var row in plannerRows) {
            items[row.Item1Id] = new RouteItem(
                row.Item1Id, row.Item1DisplayName, row.Item1Level,
                CargoWeightTable.GetWeightForLevel(row.Item1Level));
            items[row.Item2Id] = new RouteItem(
                row.Item2Id, row.Item2DisplayName, row.Item2Level,
                CargoWeightTable.GetWeightForLevel(row.Item2Level));
        }

        var carriedBalance = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var row in plannerRows.Where(row => row.ExchangeDone && row.ExchangeQuantity > 0)) {
            AddBalance(carriedBalance, row.Item1Id, -(long)row.ExchangeQuantity * row.Item1Number);
            AddBalance(carriedBalance, row.Item2Id, (long)row.ExchangeQuantity * row.Item2Number);
        }
        var activeDemand = activeRows
            .GroupBy(row => row.Item1Id, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(row => (long)row.ExchangeQuantity * row.Item1Number),
                StringComparer.Ordinal);
        var storedTotals = storageItems
            .Where(item => !string.IsNullOrWhiteSpace(item.ItemId))
            .GroupBy(item => item.ItemId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(item => (long)item.Velia + item.Iliya + item.Epheria + item.Ancado),
                StringComparer.Ordinal);

        // A DONE row proves that its exchange happened, but it does not prove that
        // the output is still on the ship: older completed routes have normally
        // already unloaded. Only carry the part that an active downstream barter
        // still needs and cannot obtain from the recorded warehouse stock. This
        // preserves the direct completed-step -> next-step handoff while preventing
        // old outputs from bypassing a real warehouse pickup or appearing again at
        // the next unload stop.
        var initialOnBoard = carriedBalance
            .Select(pair => new {
                pair.Key,
                Quantity = Math.Min(
                    Math.Max(0L, pair.Value),
                    Math.Max(0L, activeDemand.GetValueOrDefault(pair.Key)
                        - storedTotals.GetValueOrDefault(pair.Key))),
            })
            .Where(pair => pair.Quantity > 0)
            .ToDictionary(
                pair => pair.Key,
                pair => checked((int)pair.Quantity),
                StringComparer.Ordinal);

        // Preserve every exchange selected by the automatic strategy. When all
        // repetitions of one Planner row cannot fit as one atomic cargo task,
        // split them into deterministic capacity-safe segments. The route
        // solver is then free to place those segments on different trips and
        // optimize their combined distance instead of silently dropping work.
        var tasks = activeRows
            .SelectMany(row => SplitByCargoCapacity(
                row,
                cargo,
                points.GetValueOrDefault(row.IslandId,
                    new RoutePoint(double.NaN, double.NaN))))
            .ToArray();

        var warehouses = WarehouseMap.Select((mapping, index) => {
            var inventory = storageItems.ToDictionary(
                x => x.ItemId,
                x => index switch {
                    0 => x.Velia,
                    1 => x.Iliya,
                    2 => x.Epheria,
                    _ => x.Ancado,
                },
                StringComparer.Ordinal);
            return new RouteWarehouse(
                mapping.WarehouseId,
                mapping.IslandId,
                points.GetValueOrDefault(mapping.IslandId, new RoutePoint(double.NaN, double.NaN)),
                inventory);
        }).ToArray();

        return new AutomaticRoutePlanningRequest(
            tasks, items, warehouses, cargo.ExtraLT, cargo.TotalLT, limits,
            "automatic-route-v4-capacity-split-distance-first", initialOnBoard);
    }

    // Convenience for callers that just need the profile's local-moves cap.
    public static RouteSearchLimits WithProfileLimits(
        RouteSearchLimits limits, RouteOptimizationProfile profile) =>
        new(limits.MaxExpandedStates, profile.MaxLocalEvaluations);


    private static void AddBalance(Dictionary<string, long> balance, string itemId, long delta) {
        if (string.IsNullOrWhiteSpace(itemId) || delta == 0) return;
        balance[itemId] = checked(balance.GetValueOrDefault(itemId) + delta);
    }

    private static IReadOnlyList<RouteBarterTask> SplitByCargoCapacity(
        PlannerRouteSnapshot row,
        CargoCapacitySnapshot cargo,
        RoutePoint point) {
        int maxPerSegment = MaxExchangesPerSegment(row, cargo);
        // A single exchange that exceeds capacity must still reach preflight,
        // where it produces the normal task-overweight diagnostic.
        if (maxPerSegment <= 0) maxPerSegment = 1;

        int segmentCount = checked((row.ExchangeQuantity - 1) / maxPerSegment + 1);
        var result = new List<RouteBarterTask>(segmentCount);
        int remaining = row.ExchangeQuantity;
        for (int index = 0; index < segmentCount; index++) {
            int exchanges = Math.Min(maxPerSegment, remaining);
            result.Add(new RouteBarterTask(
                RouteTaskIdentity.CreateSegmentId(row.RowId, index, segmentCount),
                row.IslandId,
                point,
                row.Item1Id,
                checked(exchanges * row.Item1Number),
                row.Item2Id,
                checked(exchanges * row.Item2Number)));
            remaining -= exchanges;
        }
        return result;
    }

    private static int MaxExchangesPerSegment(
        PlannerRouteSnapshot row,
        CargoCapacitySnapshot cargo) {
        long availableLT = (long)cargo.TotalLT - cargo.ExtraLT;
        if (availableLT < 0) return 0;
        int input = LimitForSide(
            availableLT,
            CargoWeightTable.GetWeightForLevel(row.Item1Level),
            row.Item1Number);
        int output = LimitForSide(
            availableLT,
            CargoWeightTable.GetWeightForLevel(row.Item2Level),
            row.Item2Number);
        return Math.Min(input, output);
    }

    private static int LimitForSide(long availableLT, int unitWeight, int quantity) {
        if (unitWeight <= 0 || quantity <= 0) return Int32.MaxValue;
        long perExchange = checked((long)unitWeight * quantity);
        long limit = availableLT / perExchange;
        return limit >= Int32.MaxValue ? Int32.MaxValue : (int)Math.Max(0, limit);
    }
}
