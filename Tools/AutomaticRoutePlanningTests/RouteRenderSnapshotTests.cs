using iBarter.Routing;
using iBarter.View;
using iBarter.Navigation;
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
    public void Completed_exchange_output_is_carried_onto_the_next_automatic_route() {
        var rows = new[] {
            new PlannerRouteSnapshot("producer", true, 6, "DoneIsland", "SEED", "Seed", 4, 1, "CARRY", "Carry", 5, 1),
            new PlannerRouteSnapshot("consumer", false, 6, "NextIsland", "CARRY", "Carry", 5, 1, "OUT", "Output", 6, 1),
        };
        var storage = new[] {
            new StorageItemSnapshot("SEED", 4, 0, 0, 0, 0),
            new StorageItemSnapshot("CARRY", 5, 0, 0, 0, 0),
            new StorageItemSnapshot("OUT", 6, 0, 0, 0, 0),
        };
        var islands = new[] {
            new IslandRouteSnapshot("DoneIsland", new RoutePoint(1, 1)),
            new IslandRouteSnapshot("NextIsland", new RoutePoint(2, 1)),
            new IslandRouteSnapshot("Velia", new RoutePoint(0, 0)),
            new IslandRouteSnapshot("Iliya", new RoutePoint(0, 1)),
            new IslandRouteSnapshot("Epheria", new RoutePoint(0, 2)),
            new IslandRouteSnapshot("Sausan", new RoutePoint(0, 3)),
        };

        var request = AutomaticRoutePlanningAdapter.BuildRequest(
            rows, storage, islands, new CargoCapacitySnapshot(0, 30_000), new RouteSearchLimits(100, 10));

        Assert.Equal(6, request.InitialOnBoard["CARRY"]);
        var plan = new AutomaticRoutePlanner().Plan(request, TestContext.Current.CancellationToken);
        var route = Assert.Single(plan.Routes);
        Assert.Equal(RoutePlanStatus.Optimal, plan.Status);
        Assert.IsType<BarterStep>(route.Steps[0]);
        Assert.DoesNotContain(route.Steps, step => step is WarehousePickupStep);
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
    public void Completed_barter_is_hidden_from_route_path_and_highlight_set() {
        var plan = BuildPlan();

        var snapshot = RouteRenderSnapshotFactory.CreateAutomatic(
            plan, selectedRouteNumber: 2, showAll: false,
            completedBarterRowIds: new HashSet<string>(["rC"], StringComparer.Ordinal));

        Assert.Equal(["Velia"], snapshot.Paths.Single().IslandIds);
        Assert.Empty(snapshot.BarterIslandIds);
        Assert.DoesNotContain("C", snapshot.HighlightedIslandIds);
        Assert.DoesNotContain("Iliya", snapshot.HighlightedIslandIds);
    }

    [Fact]
    public void Remaining_map_path_starts_after_the_completed_route_prefix() {
        var load = new RouteLoadSnapshot(0, 0, 0);
        var route = new PlannedRoute(1, "Iliya", "Iliya", [
            new WarehousePickupStep("Iliya", "Iliya", [], load),
            new BarterStep("done", "A", new("X", 1), new("Y", 1), load),
            new BarterStep("next", "B", new("X", 1), new("Y", 1), load),
            new WarehouseUnloadStep("Iliya", "Iliya", [], load),
        ], 1, 0, 0, 0);
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [route],
            new RoutePlanObjective(1, 1, 1, 0, ""), [], "f");

        var snapshot = RouteRenderSnapshotFactory.CreateAutomatic(
            plan, 1, false, new HashSet<string>(["done"], StringComparer.Ordinal));

        Assert.Equal(["B", "Iliya"], snapshot.Paths.Single().IslandIds);
    }

    [Fact]
    public void New_first_visible_barter_focuses_its_outgoing_segment() {
        var load = new RouteLoadSnapshot(0, 0, 0);
        RouteStep[] steps = [
            new WarehousePickupStep("Iliya", "Iliya", [], load),
            new BarterStep("done", "A", new("X", 1), new("Y", 1), load),
            new BarterStep("next", "B", new("X", 1), new("Y", 1), load),
            new BarterStep("after", "C", new("X", 1), new("Y", 1), load),
            new WarehouseUnloadStep("Iliya", "Iliya", [], load),
        ];

        var segment = RouteProgressFilter.FindVisibleBarterSegment(
            steps,
            new HashSet<string>(["done"], StringComparer.Ordinal),
            "next");

        Assert.Equal(new RouteFocusSegment("B", "C"), segment);
    }

    [Fact]
    public void Visible_route_segment_resolves_back_to_its_barter_row() {
        var load = new RouteLoadSnapshot(0, 0, 0);
        RouteStep[] steps = [
            new WarehousePickupStep("Iliya", "Iliya", [], load),
            new BarterStep("row-a", "A", new("X", 1), new("Y", 1), load),
            new BarterStep("row-b", "B", new("X", 1), new("Y", 1), load),
            new WarehouseUnloadStep("Iliya", "Iliya", [], load),
        ];

        string? rowId = RouteProgressFilter.FindVisibleBarterRowForSegment(
            steps,
            new HashSet<string>(StringComparer.Ordinal),
            "A",
            "B");

        Assert.Equal("row-b", rowId);
    }

    [Fact]
    public void Planner_row_identity_matches_persisted_route_step_identity() {
        Assert.Equal("2:Iliya:800001:800002", RoutePlannerRowIdentity.Create(
            2, "Iliya", "800001", "800002"));
    }

    [Fact]
    public void Ship_cargo_wheel_uses_one_logical_item_per_notch() {
        Assert.Equal(1, ShipCargoScrollBehavior.GetLogicalItemDelta(-120));
        Assert.Equal(-1, ShipCargoScrollBehavior.GetLogicalItemDelta(120));
        Assert.Equal(0, ShipCargoScrollBehavior.GetLogicalItemDelta(0));
    }

    [Fact]
    public void Selected_route_uses_a_thin_base_line_and_a_separate_highlight_overlay() {
        var selected = RouteVisualStyle.For(routeSelected: true, focused: false, pulse: false);
        var unselected = RouteVisualStyle.For(routeSelected: false, focused: false, pulse: false);

        Assert.Equal(2.4, selected.StrokeThickness);
        Assert.True(selected.ShowSelectionOverlay);
        Assert.Equal(1.25, unselected.StrokeThickness);
        Assert.False(unselected.ShowSelectionOverlay);
    }

    [Fact]
    public void Repeated_island_markers_are_distributed_around_the_island_center() {
        var only = RouteStepMarkerLayout.OffsetFor(0, 1);
        var first = RouteStepMarkerLayout.OffsetFor(0, 2);
        var second = RouteStepMarkerLayout.OffsetFor(1, 2);

        Assert.Equal(new RouteMarkerOffset(0, 0), only);
        Assert.NotEqual(first, second);
        Assert.True(first.DistanceFromOrigin > 0);
        Assert.True(second.DistanceFromOrigin > 0);
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
