using iBarter.Routing;
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
}
