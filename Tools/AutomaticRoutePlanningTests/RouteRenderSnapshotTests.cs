using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public sealed class RouteRenderSnapshotTests {
    [Fact]
    public void Adapter_keeps_storage_separated_and_maps_ancado_to_sausan() {
        var rows = new[] {
            new PlannerRouteSnapshot("r1", false, 2, "A", "IN", "Input", 1, 3, "OUT", "Output", 2, 4),
            new PlannerRouteSnapshot("done", true, 9, "A", "IN", "Input", 1, 1, "OUT", "Output", 2, 1),
        };
        var storage = new[] { new StorageItemSnapshot("IN", 1, 10, 20, 30, 40) };
        var islands = new[] {
            new IslandRouteSnapshot("A", new RoutePoint(1, 1)),
            new IslandRouteSnapshot("Velia", new RoutePoint(2, 2)),
            new IslandRouteSnapshot("Iliya", new RoutePoint(3, 3)),
            new IslandRouteSnapshot("Epheria", new RoutePoint(4, 4)),
            new IslandRouteSnapshot("Sausan", new RoutePoint(5, 5)),
        };

        var request = AutomaticRoutePlanningAdapter.BuildRequest(
            rows, storage, islands, new CargoCapacitySnapshot(100, 10_000), new RouteSearchLimits(100, 10));

        Assert.Single(request.Tasks);
        Assert.Equal(6, request.Tasks[0].InputQuantity);
        Assert.Equal(8, request.Tasks[0].OutputQuantity);
        Assert.Equal(10, request.Warehouses.Single(x => x.WarehouseId == "Velia").Inventory["IN"]);
        Assert.Equal(20, request.Warehouses.Single(x => x.WarehouseId == "Iliya").Inventory["IN"]);
        Assert.Equal(30, request.Warehouses.Single(x => x.WarehouseId == "Epheria").Inventory["IN"]);
        var ancado = request.Warehouses.Single(x => x.WarehouseId == "Ancado");
        Assert.Equal("Sausan", ancado.IslandId);
        Assert.Equal(40, ancado.Inventory["IN"]);
        Assert.Equal(new RoutePoint(5, 5), ancado.Point);
    }

    [Fact]
    public void Automatic_snapshot_selects_one_or_all_routes_with_stable_colors() {
        var plan = BuildPlan();

        var one = RouteRenderSnapshotFactory.CreateAutomatic(plan, selectedRouteNumber: 2, showAll: false);
        var all = RouteRenderSnapshotFactory.CreateAutomatic(plan, selectedRouteNumber: 2, showAll: true);

        var selected = Assert.Single(one.Paths);
        Assert.Equal(2, selected.RouteNumber);
        Assert.Equal(["Iliya", "C", "Velia"], selected.IslandIds);
        Assert.Equal(["C"], one.BarterIslandIds);
        Assert.Equal(["Iliya", "Velia"], one.WarehouseIslandIds.OrderBy(x => x).ToArray());
        Assert.Equal(["C", "Iliya", "Velia"], one.HighlightedIslandIds.OrderBy(x => x).ToArray());
        Assert.Equal(["A", "C"], all.BarterIslandIds.OrderBy(x => x).ToArray());
        Assert.Equal(2, all.Paths.Count);
        Assert.NotEqual(all.Paths[0].ColorIndex, all.Paths[1].ColorIndex);
    }

    [Fact]
    public void Render_snapshot_collapses_only_adjacent_duplicate_islands() {
        var plan = BuildPlan(route1Islands: ["Velia", "Velia", "A", "Velia", "Velia"]);
        var snapshot = RouteRenderSnapshotFactory.CreateAutomatic(plan, 1, false);
        Assert.Equal(["Velia", "A", "Velia"], snapshot.Paths.Single().IslandIds);
    }

    [Fact]
    public void Warehouse_and_barter_order_keeps_non_adjacent_return_visits() {
        var load = new RouteLoadSnapshot(0, 0, 0);
        var route = new PlannedRoute(1, "Iliya", "Velia", [
            new WarehousePickupStep("Iliya", "Iliya", [], load),
            new BarterStep("rA", "A", new("X", 1), new("Y", 1), load),
            new WarehousePickupStep("Velia", "Velia", [], load),
            new BarterStep("rB", "B", new("X", 1), new("Y", 1), load),
            new WarehouseUnloadStep("Velia", "Velia", [], load),
        ], 1, 0, 0, 0);
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [route],
            new RoutePlanObjective(1, 1, 2, 0, ""), [], "f");

        var snapshot = RouteRenderSnapshotFactory.CreateAutomatic(plan, 1, false);

        Assert.Equal(["Iliya", "A", "Velia", "B", "Velia"], snapshot.Paths.Single().IslandIds);
    }

    [Fact]
    public void Manual_snapshot_highlights_every_selected_barter_island() {
        var snapshot = RouteRenderSnapshotFactory.CreateManual(["Velia", "A", "B"]);

        Assert.Equal(["A", "B", "Velia"], snapshot.HighlightedIslandIds.OrderBy(x => x).ToArray());
    }

    private static RoutePlan BuildPlan(IReadOnlyList<string>? route1Islands = null) {
        route1Islands ??= ["Velia", "A", "Velia"];
        var load = new RouteLoadSnapshot(0, 0, 0);
        var route1Steps = route1Islands.Select((island, index) => (RouteStep)(index == 0
            ? new WarehousePickupStep("Velia", island, [], load)
            : index == route1Islands.Count - 1
                ? new WarehouseUnloadStep("Velia", island, [], load)
                : new BarterStep($"r{index}", island, new("A", 1), new("B", 1), load))).ToArray();
        var route2Steps = new RouteStep[] {
            new WarehousePickupStep("Iliya", "Iliya", [], load),
            new BarterStep("rC", "C", new("A", 1), new("B", 1), load),
            new WarehouseUnloadStep("Velia", "Velia", [], load),
        };
        return new RoutePlan(RoutePlanStatus.Optimal,
            [
                new PlannedRoute(1, "Velia", "Velia", route1Steps, 1, 0, 0, 0),
                new PlannedRoute(2, "Iliya", "Velia", route2Steps, 1, 0, 0, 0),
            ], new RoutePlanObjective(2, 2, 2, 0, ""), [], "fingerprint");
    }
}
