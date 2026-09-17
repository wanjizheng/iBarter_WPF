using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public sealed class ManualCargoProjectorTests {
    [Fact]
    public void Projects_minimum_initial_inventory_and_each_step_load() {
        var result = ManualCargoProjector.Project([
            new ManualCargoStepInput("a", "A", "A_ITEM", 2, 100, "B_ITEM", 1, 300),
            new ManualCargoStepInput("b", "B", "B_ITEM", 1, 300, "C_ITEM", 2, 200),
        ], extraLT: 50);

        Assert.Equal(250, result.InitialLT);
        Assert.Equal(350, result.Steps[0].Load.TotalWithExtraLT);
        Assert.Equal(450, result.Steps[1].Load.TotalWithExtraLT);
        Assert.Equal(450, result.PeakLT);
        Assert.Equal(450, result.CurrentLT);
    }

    [Fact]
    public void Consumer_before_producer_uses_initial_stock_for_the_shortage() {
        var result = ManualCargoProjector.Project([
            new ManualCargoStepInput("consumer", "C", "MID", 1, 400, "OUT", 1, 0),
            new ManualCargoStepInput("producer", "P", "BASE", 1, 100, "MID", 1, 400),
        ], extraLT: 0);

        Assert.Equal(500, result.InitialLT);
        Assert.Equal(100, result.Steps[0].Load.CargoLT);
        Assert.Equal(400, result.Steps[1].Load.CargoLT);
        Assert.Equal(500, result.PeakLT);
        Assert.Equal(400, result.CurrentLT);
    }

    [Fact]
    public void Extra_lt_participates_in_initial_current_and_peak_values() {
        var result = ManualCargoProjector.Project([
            new ManualCargoStepInput("a", "A", "IN", 1, 200, "OUT", 1, 500),
        ], extraLT: 2_000);

        Assert.Equal(2_200, result.InitialLT);
        Assert.Equal(2_500, result.CurrentLT);
        Assert.Equal(2_500, result.PeakLT);
        Assert.Equal(2_500, result.Steps[0].Load.PeakTotalLT);
    }
}
