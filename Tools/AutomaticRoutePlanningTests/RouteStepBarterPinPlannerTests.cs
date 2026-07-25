using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public sealed class RouteStepBarterPinPlannerTests {
    [Fact]
    public void Plan_creates_one_group_pin_per_real_barter_step() {
        var labels = new[] {
            new RouteStepMapLabel(
                RouteNumber: 2,
                StepIndex: 3,
                StepKind: RouteStepKind.Barter,
                IslandId: "Haemo",
                BarterRowId: "row-1",
                DisplayText: "A × 5 → B × 5",
                IsWarehouseOperation: false,
                BarterGroup: 7),
            new RouteStepMapLabel(
                RouteNumber: 2,
                StepIndex: 4,
                StepKind: RouteStepKind.Barter,
                IslandId: "Haemo",
                BarterRowId: "row-2",
                DisplayText: "C × 1 → D × 1",
                IsWarehouseOperation: false,
                BarterGroup: 11),
        };

        var pins = RouteStepBarterPinPlanner.Plan(labels);

        Assert.Equal(2, pins.Count);
        Assert.Collection(
            pins,
            first => {
                Assert.Equal("r2s3", first.Identity);
                Assert.Equal("Haemo", first.IslandId);
                Assert.Equal(7, first.BarterGroup);
                Assert.Equal("A × 5 → B × 5", first.DisplayText);
            },
            second => {
                Assert.Equal("r2s4", second.Identity);
                Assert.Equal("Haemo", second.IslandId);
                Assert.Equal(11, second.BarterGroup);
            });
    }

    [Fact]
    public void Plan_does_not_create_pins_for_warehouse_operations() {
        var labels = new[] {
            new RouteStepMapLabel(
                RouteNumber: 1,
                StepIndex: 0,
                StepKind: RouteStepKind.Pickup,
                IslandId: "Velia",
                BarterRowId: null,
                DisplayText: "Velia · 装货",
                IsWarehouseOperation: true),
            new RouteStepMapLabel(
                RouteNumber: 1,
                StepIndex: 5,
                StepKind: RouteStepKind.Unload,
                IslandId: "Iliya",
                BarterRowId: null,
                DisplayText: "Iliya · 卸货",
                IsWarehouseOperation: true),
        };

        Assert.Empty(RouteStepBarterPinPlanner.Plan(labels));
    }

    [Fact]
    public void Plan_preserves_missing_group_as_null_for_existing_fallback_colour() {
        var label = new RouteStepMapLabel(
            RouteNumber: 4,
            StepIndex: 2,
            StepKind: RouteStepKind.Barter,
            IslandId: "Olvia",
            BarterRowId: "row-missing",
            DisplayText: "Input → Output",
            IsWarehouseOperation: false,
            BarterGroup: null);

        RouteStepBarterPin pin = Assert.Single(
            RouteStepBarterPinPlanner.Plan(new[] { label }));

        Assert.Null(pin.BarterGroup);
        Assert.Equal(label.Identity, pin.Identity);
    }
}
