using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;
public class TaggedDistanceObjectiveTests {
    [Fact] public void ShorterDistanceWinsEvenWithMuchLongerHandlingTime() {
        var shorter = new TaggedTransportState { Distance = 1000, OverloadedDistance = 1000, Seconds = 100000 };
        var faster = new TaggedTransportState { Distance = 2000, Seconds = 1 };
        Assert.True(TaggedDistanceObjective.Compare(new(), shorter, faster) < 0);
    }

    [Fact] public void EqualDistancePrefersLessOverloadedSailingBeforeHandlingCount() {
        var normal = new TaggedTransportState { Distance = 1000, OverloadedDistance = 0, Seconds = 100000 };
        normal.Steps.Add(new(new(TaggedActionKind.Switch, "A", "main", "alt"), 0, 0, 0, 0, 0));
        var overloaded = new TaggedTransportState { Distance = 1000, OverloadedDistance = 500, Seconds = 1 };
        Assert.True(TaggedDistanceObjective.Compare(new(), normal, overloaded) < 0);
    }

    [Fact] public void TaggedCargoChainsThreeIslandsWithoutAnotherWarehouseTrip() {
        var request = new TaggedTransportRequest {
            ShipLimitLT = 2000,
            Settings = new() { MaxStates = 30000, SearchSeconds = 10, Ports = [
                new() { IslandId = "Velia", WarehouseId = "Velia", Enabled = true },
                new() { IslandId = "A", Enabled = true }, new() { IslandId = "B", Enabled = true }, new() { IslandId = "C", Enabled = true }
            ] },
            Items = new() { ["in"] = new("in", "Input", 4, 1000), ["out"] = new("out", "Output", 5, 1000) },
            Points = new() { ["Velia"] = new(0, 0), ["A"] = new(1000000, 0), ["B"] = new(1000100, 0), ["C"] = new(1000200, 0) },
            Warehouses = new() { ["Velia"] = new() { ["in"] = 3 } },
            Trades = [new("a", "A", "in", 1, "out", 1, 1), new("b", "B", "in", 1, "out", 1, 1), new("c", "C", "in", 1, "out", 1, 1)]
        };
        var result = new TaggedTransportPlanner().Plan(request, TestContext.Current.CancellationToken);
        Assert.NotNull(result.Plan);
        Assert.True(result.Plan.Distance < result.Plan.ShipOnlyDistance);
        Assert.Single(TaggedTransportRoutes.Build(request, result.Plan));
        Assert.Equal(3, result.Plan.Steps.Count(s => s.Action.Kind == TaggedActionKind.Barter));
        Assert.Contains(result.Plan.Steps, s => s.Action.Kind == TaggedActionKind.Transfer && s.Action.From == "main");
        Assert.True(new TaggedTransportSimulator(request).Verify(result.Plan, out _, out _));
    }
}
