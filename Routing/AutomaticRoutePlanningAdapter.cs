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
        var points = islands
            .GroupBy(x => x.IslandId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First().Point, StringComparer.Ordinal);
        var activeRows = plannerRows.Where(x => x.ExchangeQuantity > 0 && !x.ExchangeDone).ToArray();

        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal);
        foreach (var storage in storageItems.Where(x => !string.IsNullOrWhiteSpace(x.ItemId)))
            items[storage.ItemId] = new RouteItem(
                storage.ItemId, storage.ItemId, storage.Level, CargoWeightTable.GetWeightForLevel(storage.Level));
        foreach (var row in activeRows) {
            items[row.Item1Id] = new RouteItem(
                row.Item1Id, row.Item1DisplayName, row.Item1Level,
                CargoWeightTable.GetWeightForLevel(row.Item1Level));
            items[row.Item2Id] = new RouteItem(
                row.Item2Id, row.Item2DisplayName, row.Item2Level,
                CargoWeightTable.GetWeightForLevel(row.Item2Level));
        }

        var tasks = activeRows.Select(row => new RouteBarterTask(
            row.RowId,
            row.IslandId,
            points.GetValueOrDefault(row.IslandId, new RoutePoint(double.NaN, double.NaN)),
            row.Item1Id,
            checked(row.ExchangeQuantity * row.Item1Number),
            row.Item2Id,
            checked(row.ExchangeQuantity * row.Item2Number))).ToArray();

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
            tasks, items, warehouses, cargo.ExtraLT, cargo.TotalLT, limits, "automatic-route-v2-corridors");
    }
}
