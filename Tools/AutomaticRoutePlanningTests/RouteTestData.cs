using iBarter.Routing;

namespace AutomaticRoutePlanningTests;

internal static class RouteTestData {
    public static AutomaticRoutePlanningRequest SingleTask(
        int inputStock = 10,
        int inputQuantity = 2,
        int outputQuantity = 3,
        int inputLevel = 1,
        int outputLevel = 2,
        int extraLT = 0,
        int totalLT = 10_000) {
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["IN"] = new("IN", "Input", inputLevel, CargoWeightTable.GetWeightForLevel(inputLevel)),
            ["OUT"] = new("OUT", "Output", outputLevel, CargoWeightTable.GetWeightForLevel(outputLevel)),
        };
        var warehouse = new RouteWarehouse(
            "W", "W_ISLAND", new RoutePoint(0, 0),
            new Dictionary<string, int>(StringComparer.Ordinal) { ["IN"] = inputStock });
        var task = new RouteBarterTask(
            "r1", "A", new RoutePoint(10, 0),
            "IN", inputQuantity, "OUT", outputQuantity);

        return new AutomaticRoutePlanningRequest(
            [task], items, [warehouse], extraLT, totalLT,
            new RouteSearchLimits(100_000, 1_000), "test-v1");
    }

    public static AutomaticRoutePlanningRequest TwoItemRequest(bool reverseDictionaryOrder) {
        var pairs = new[] {
            new KeyValuePair<string, RouteItem>("A", new RouteItem("A", "Item A", 1, 100)),
            new KeyValuePair<string, RouteItem>("B", new RouteItem("B", "Item B", 2, 400)),
            new KeyValuePair<string, RouteItem>("C", new RouteItem("C", "Item C", 3, 900)),
        };
        var orderedPairs = reverseDictionaryOrder ? pairs.Reverse() : pairs;
        var items = orderedPairs.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

        var inventoryPairs = new[] {
            new KeyValuePair<string, int>("A", 10),
            new KeyValuePair<string, int>("B", 10),
        };
        var orderedInventory = reverseDictionaryOrder ? inventoryPairs.Reverse() : inventoryPairs;
        var inventory = orderedInventory.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

        return new AutomaticRoutePlanningRequest(
            [
                new RouteBarterTask("r1", "A_ISLAND", new RoutePoint(10, 0), "A", 2, "C", 1),
                new RouteBarterTask("r2", "B_ISLAND", new RoutePoint(20, 0), "B", 1, "C", 1),
            ],
            items,
            [new RouteWarehouse("W", "W_ISLAND", new RoutePoint(0, 0), inventory)],
            100,
            10_000,
            new RouteSearchLimits(100_000, 1_000),
            "test-v1");
    }

    public static AutomaticRoutePlanningRequest Mutate(
        AutomaticRoutePlanningRequest source,
        string mutation) {
        var tasks = source.Tasks.ToList();
        var warehouses = source.Warehouses.ToList();
        int totalLT = source.TotalLT;

        switch (mutation) {
            case "quantity":
                tasks[0] = tasks[0] with { InputQuantity = tasks[0].InputQuantity + 1 };
                break;
            case "warehouse":
                var changedInventory = warehouses[0].Inventory.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
                changedInventory["A"]++;
                warehouses[0] = new RouteWarehouse(
                    warehouses[0].WarehouseId,
                    warehouses[0].IslandId,
                    warehouses[0].Point,
                    changedInventory);
                break;
            case "total-lt":
                totalLT++;
                break;
            case "coordinate":
                tasks[0] = tasks[0] with { Point = new RoutePoint(tasks[0].Point.X + 1, tasks[0].Point.Y) };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }

        return new AutomaticRoutePlanningRequest(
            tasks,
            source.Items,
            warehouses,
            source.ExtraLT,
            totalLT,
            source.Limits,
            source.ConfigurationVersion);
    }
}
