using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public sealed class AutomaticRoutePreflightTests {
    [Fact]
    public void Rejects_more_than_64_tasks_and_duplicate_row_ids() {
        var seed = RouteTestData.SingleTask();
        var tooMany = Copy(seed, tasks: Enumerable.Range(0, 65)
            .Select(i => seed.Tasks[0] with { RowId = $"r{i}" }).ToArray());
        Assert.Contains(AutomaticRoutePreflight.Validate(tooMany).Diagnostics, x => x.Code == "too-many-tasks");

        var duplicates = Copy(seed, tasks: [seed.Tasks[0], seed.Tasks[0]]);
        Assert.Contains(AutomaticRoutePreflight.Validate(duplicates).Diagnostics, x => x.Code == "duplicate-row-id");
    }

    [Fact]
    public void Rejects_invalid_coordinates_items_inventory_and_capacity() {
        var seed = RouteTestData.SingleTask();
        var badTask = seed.Tasks[0] with { Point = new RoutePoint(double.NaN, 0), Item1Id = "MISSING" };
        var badWarehouse = new RouteWarehouse("W", "W_ISLAND", new RoutePoint(0, double.PositiveInfinity),
            new Dictionary<string, int> { ["IN"] = -1 });
        var request = new AutomaticRoutePlanningRequest(
            [badTask], seed.Items, [badWarehouse], 10, 0, seed.Limits, seed.ConfigurationVersion);

        var result = AutomaticRoutePreflight.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, x => x.Code == "non-finite-coordinate");
        Assert.Contains(result.Diagnostics, x => x.Code == "missing-item");
        Assert.Contains(result.Diagnostics, x => x.Code == "negative-stock");
        Assert.Contains(result.Diagnostics, x => x.Code == "invalid-capacity");
    }

    [Fact]
    public void Rejects_extra_lt_above_total_and_single_task_peak_overweight() {
        var invalidExtra = RouteTestData.SingleTask(extraLT: 1_001, totalLT: 1_000);
        Assert.Contains(AutomaticRoutePreflight.Validate(invalidExtra).Diagnostics,
            x => x.Code == "invalid-capacity");

        var overweight = RouteTestData.SingleTask(
            inputQuantity: 1, outputQuantity: 1, outputLevel: 7, totalLT: 1_500);
        Assert.Contains(AutomaticRoutePreflight.Validate(overweight).Diagnostics,
            x => x.Code == "task-overweight");
    }

    [Fact]
    public void Rejects_input_with_neither_stock_nor_reachable_producer() {
        var seed = RouteTestData.SingleTask(inputStock: 0);
        var result = AutomaticRoutePreflight.Validate(seed);
        Assert.Contains(result.Diagnostics, x => x.Code == "unreachable-input" && x.ItemId == "IN");
    }

    [Fact]
    public void Initial_stock_can_break_an_apparent_production_cycle() {
        var items = new Dictionary<string, RouteItem> {
            ["A"] = new("A", "A", 1, 100),
            ["B"] = new("B", "B", 1, 100),
        };
        var request = new AutomaticRoutePlanningRequest(
            [
                new("r1", "I1", new RoutePoint(1, 0), "A", 1, "B", 1),
                new("r2", "I2", new RoutePoint(2, 0), "B", 1, "A", 1),
            ],
            items,
            [new RouteWarehouse("W", "W", new RoutePoint(0, 0), new Dictionary<string, int> { ["A"] = 1 })],
            0, 1_000, new RouteSearchLimits(100, 10), "v1");

        var result = AutomaticRoutePreflight.Validate(request);

        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Diagnostics, x => x.Code == "unreachable-input");
        Assert.Equal(1UL << 1, result.ProducerMasks[0]);
        Assert.Equal(1UL << 0, result.ProducerMasks[1]);
    }

    [Fact]
    public void Weightless_misc_currency_is_a_valid_output() {
        var seed = RouteTestData.SingleTask();
        var items = seed.Items.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        items["OUT"] = new RouteItem("OUT", "Crow Coin", -1, 0);
        var request = new AutomaticRoutePlanningRequest(
            seed.Tasks, items, seed.Warehouses, seed.ExtraLT, seed.TotalLT,
            seed.Limits, seed.ConfigurationVersion);

        var result = AutomaticRoutePreflight.Validate(request);

        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Diagnostics, x => x.Code == "invalid-item");
    }

    private static AutomaticRoutePlanningRequest Copy(
        AutomaticRoutePlanningRequest source,
        IReadOnlyList<RouteBarterTask>? tasks = null) =>
        new(tasks ?? source.Tasks, source.Items, source.Warehouses, source.ExtraLT, source.TotalLT,
            source.Limits, source.ConfigurationVersion);
}
