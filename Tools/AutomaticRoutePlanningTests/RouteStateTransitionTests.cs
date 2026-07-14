using iBarter.Routing;
using iBarter.Navigation;
using Xunit;

namespace AutomaticRoutePlanningTests;

public sealed class RouteStateTransitionTests {
    [Fact]
    public void Intermediate_output_peak_is_rejected_even_when_initial_and_final_are_safe() {
        var request = RouteTestData.SingleTask(
            inputStock: 1, inputQuantity: 1, outputQuantity: 1,
            inputLevel: 1, outputLevel: 5, extraLT: 100, totalLT: 1_000);
        var initial = RouteSimulationState.CreateInitial(request);
        var pickup = RouteStateTransition.TryPickup(request, initial, "W", [new("IN", 1)]);
        Assert.True(pickup.Success);

        var before = pickup.State;
        var barter = RouteStateTransition.TryBarter(request, before, 0);

        Assert.False(barter.Success);
        Assert.Equal("overweight", barter.Diagnostic?.Code);
        Assert.Same(before, barter.State);
        Assert.Equal(1, before.OnBoard["IN"]);
        Assert.False(before.OnBoard.ContainsKey("OUT"));
        Assert.Equal(0UL, before.CompletedMask);
    }

    [Fact]
    public void Unload_moves_every_onboard_item_to_destination_warehouse() {
        var request = RouteTestData.SingleTask();
        var initial = RouteSimulationState.CreateInitial(request);
        var pickup = RouteStateTransition.TryPickup(request, initial, "W", [new("IN", 2)]);
        var barter = RouteStateTransition.TryBarter(request, pickup.State, 0);

        var unload = RouteStateTransition.TryUnload(request, barter.State, "W");

        Assert.True(unload.Success);
        Assert.Empty(unload.State.OnBoard);
        Assert.Equal(3, unload.State.WarehouseInventory["W"]["OUT"]);
        Assert.Equal(request.ExtraLT, unload.Step?.Load.TotalWithExtraLT);
        Assert.Single(unload.State.FinishedRoutes);
    }

    [Fact]
    public void Pickup_cannot_exceed_warehouse_stock_or_total_lt() {
        var insufficient = RouteTestData.SingleTask(inputStock: 1, inputQuantity: 2);
        var s1 = RouteSimulationState.CreateInitial(insufficient);
        var stockFailure = RouteStateTransition.TryPickup(insufficient, s1, "W", [new("IN", 2)]);
        Assert.False(stockFailure.Success);
        Assert.Equal("insufficient-stock", stockFailure.Diagnostic?.Code);
        Assert.Same(s1, stockFailure.State);

        var overweight = RouteTestData.SingleTask(
            inputStock: 2, inputQuantity: 2, inputLevel: 7, totalLT: 3_000);
        var s2 = RouteSimulationState.CreateInitial(overweight);
        var weightFailure = RouteStateTransition.TryPickup(overweight, s2, "W", [new("IN", 2)]);
        Assert.False(weightFailure.Success);
        Assert.Equal("overweight", weightFailure.Diagnostic?.Code);
        Assert.Same(s2, weightFailure.State);
    }

    [Fact]
    public void Barter_consumes_exact_input_and_adds_exact_output() {
        var request = RouteTestData.SingleTask(inputQuantity: 2, outputQuantity: 3);
        var initial = RouteSimulationState.CreateInitial(request);
        var pickup = RouteStateTransition.TryPickup(request, initial, "W", [new("IN", 2)]);
        Assert.True(pickup.Success);

        var barter = RouteStateTransition.TryBarter(request, pickup.State, taskIndex: 0);
        Assert.True(barter.Success);
        Assert.False(barter.State.OnBoard.ContainsKey("IN"));
        Assert.Equal(3, barter.State.OnBoard["OUT"]);
        Assert.Equal(1UL, barter.State.CompletedMask);
    }

    [Fact]
    public void Empty_route_cannot_be_unloaded_and_pickup_warehouse_cannot_repeat() {
        var request = RouteTestData.SingleTask();
        var initial = RouteSimulationState.CreateInitial(request);
        var empty = RouteStateTransition.TryUnload(request, initial, "W");
        Assert.False(empty.Success);
        Assert.Equal("empty-route", empty.Diagnostic?.Code);

        var pickup = RouteStateTransition.TryPickup(request, initial, "W", [new("IN", 1)]);
        var repeated = RouteStateTransition.TryPickup(request, pickup.State, "W", [new("IN", 1)]);
        Assert.False(repeated.Success);
        Assert.Equal("warehouse-revisited", repeated.Diagnostic?.Code);
        Assert.Same(pickup.State, repeated.State);
    }

    [Fact]
    public void Route_distance_uses_the_same_right_region_corridor_in_both_directions() {
        var items = new Dictionary<string, RouteItem> {
            ["IN"] = new("IN", "Input", 1, 100),
            ["OUT"] = new("OUT", "Output", 1, 100),
        };
        var halmad = new RoutePoint(558_999, 333_684);
        var hakoven = new RoutePoint(1_252_450, 547_567);
        var request = new AutomaticRoutePlanningRequest(
            [new RouteBarterTask("r", "Hakoven", hakoven, "IN", 1, "OUT", 1)],
            items,
            [new RouteWarehouse("W", "Halmad", halmad, new Dictionary<string, int> { ["IN"] = 1 })],
            0, 10_000, new RouteSearchLimits(100, 10), "corridor-test");
        var pickup = RouteStateTransition.TryPickup(
            request, RouteSimulationState.CreateInitial(request), "W", [new("IN", 1)]);
        var barter = RouteStateTransition.TryBarter(request, pickup.State, 0);
        var unload = RouteStateTransition.TryUnload(request, barter.State, "W");

        double straightRoundTrip = 2 * IslandNavigationGeometry.Distance(
            new NavigationPoint(halmad.X, halmad.Y), new NavigationPoint(hakoven.X, hakoven.Y));

        Assert.True(unload.Success);
        Assert.True(unload.State.TotalDistance > straightRoundTrip);
    }

    [Fact]
    public void Partial_unload_keeps_other_items_onboard_until_the_final_warehouse() {
        var items = new Dictionary<string, RouteItem> {
            ["A"] = new("A", "A", 1, 100),
            ["B"] = new("B", "B", 1, 100),
            ["C"] = new("C", "C", 1, 100),
        };
        var request = new AutomaticRoutePlanningRequest(
            [new RouteBarterTask("r", "Trade", new RoutePoint(10, 0), "A", 1, "C", 1)],
            items,
            [
                new RouteWarehouse("W1", "W1", new RoutePoint(0, 0),
                    new Dictionary<string, int> { ["A"] = 1 }),
                new RouteWarehouse("W2", "W2", new RoutePoint(20, 0),
                    new Dictionary<string, int>()),
            ],
            0, 10_000, new RouteSearchLimits(100, 10), "partial-unload");
        var pickup = RouteStateTransition.TryPickup(
            request, RouteSimulationState.CreateInitial(request), "W1", [new("A", 1)]);
        var barter = RouteStateTransition.TryBarter(request, pickup.State, 0);

        var partial = RouteStateTransition.TryUnload(
            request, barter.State, "W1", [new RouteItemQuantity("C", 1)], finishRoute: false);

        Assert.True(partial.Success);
        Assert.Empty(partial.State.FinishedRoutes);
        Assert.False(partial.State.OnBoard.ContainsKey("C"));
        Assert.Equal(1, partial.State.WarehouseInventory["W1"]["C"]);

        var invalidFinal = RouteStateTransition.TryUnload(
            request, barter.State, "W1", [], finishRoute: true);
        Assert.False(invalidFinal.Success);
        Assert.Equal("cargo-remains", invalidFinal.Diagnostic?.Code);
    }

    [Fact]
    public void Final_unload_discards_zero_weight_reward_without_storing_or_displaying_it() {
        var items = new Dictionary<string, RouteItem> {
            ["IN"] = new("IN", "Input", 5, 1_000),
            ["10"] = new("10", "Crow Coin", -1, 0),
        };
        var request = new AutomaticRoutePlanningRequest(
            [new RouteBarterTask("crow", "Trade", new RoutePoint(10, 0), "IN", 1, "10", 7)],
            items,
            [new RouteWarehouse("W", "W", new RoutePoint(0, 0),
                new Dictionary<string, int> { ["IN"] = 1 })],
            0, 10_000, new RouteSearchLimits(100, 10), "crow-unload");
        var pickup = RouteStateTransition.TryPickup(
            request, RouteSimulationState.CreateInitial(request), "W", [new("IN", 1)]);
        var barter = RouteStateTransition.TryBarter(request, pickup.State, 0);

        var finish = RouteStateTransition.TryUnload(request, barter.State, "W", [], finishRoute: true);

        Assert.True(finish.Success);
        Assert.Empty(finish.State.OnBoard);
        Assert.False(finish.State.WarehouseInventory["W"].ContainsKey("10"));
        Assert.Empty(Assert.IsType<WarehouseUnloadStep>(finish.Step).Items);
    }
}
