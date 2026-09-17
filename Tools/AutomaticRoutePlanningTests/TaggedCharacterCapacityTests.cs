using iBarter.Routing;
using Xunit;
namespace AutomaticRoutePlanningTests;

public class TaggedCharacterCapacityTests {
    private static TaggedTransportRequest Request(string owner, int level = 5) {
        var r = new TaggedTransportRequest {
            ShipLimitLT = 26410, ShipOccupiedLT = 2411,
            Points = new() { ["Lema"] = new(0, 0) },
            Items = new() { ["grass"] = new("grass", "102-year Golden Herb", level, 1000), ["quartz"] = new("quartz", "Blue Quartz", level, 1000) },
            Settings = new() { ActiveCharacter = owner, StartIsland = "Lema", Ports = [new() { IslandId = "Lema", Enabled = true }],
                InitialCargo = [new(owner, "grass", 5), new("ship", "quartz", 2)] }
        };
        var c = r.Settings.Carriers.Single(c => c.Id == owner); c.LimitLT = 3213; c.OccupiedLT = 120;
        return r;
    }
    [Theory] [InlineData("main")] [InlineData("alt")]
    public void FiveHerbsCannotReceiveTwoQuartzOrEvenOnePastTheConfiguredLimit(string owner) {
        var sim = new TaggedTransportSimulator(Request(owner)); var state = sim.Initial();
        Assert.Equal(5120, sim.Weight(state, owner));
        Assert.False(sim.TryApply(state, new(TaggedActionKind.Transfer, "Lema", "ship", owner, "quartz", 2), out _, out _));
        Assert.False(sim.TryApply(state, new(TaggedActionKind.Transfer, "Lema", "ship", owner, "quartz", 1), out _, out _));
        Assert.Equal(5, sim.Count(state, owner, "grass"));
    }
    [Theory] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void IndividualGoodsRequireIndividualSlotsAndCannotUseMountStacking(int level) {
        var r = Request("main", level); r.Settings.Carriers[0].Slots = 4;
        var sim = new TaggedTransportSimulator(r);
        Assert.False(sim.Stackable("grass")); Assert.False(sim.FitsSlots(sim.Initial(), "main"));
        Assert.False(sim.TryApply(sim.Initial(), new(TaggedActionKind.StackAtWarehouse, "Lema", "warehouse:Lema", "main-elephant", "grass", 5), out _, out _));
    }
    [Fact] public void TrueStacksRetainTheirSingleTransferException() {
        var sim = new TaggedTransportSimulator(Request("alt", 4)); var state = sim.Initial();
        Assert.True(sim.TryApply(state, new(TaggedActionKind.Transfer, "Lema", "ship", "alt", "quartz", 2), out state, out _));
        Assert.Equal(7120, sim.Weight(state, "alt"));
    }
    [Fact] public void ExactBoundaryAllowsOneItemButNotTheNextAndHonorsConfiguredRatio() {
        var r = Request("main"); r.Settings.InitialCargo = [new("ship", "grass", 3)];
        var c = r.Settings.Carriers[0]; c.LimitLT = 2000; c.OccupiedLT = 400;
        r.Settings.CharacterReceiveRatio = 1.2;
        var sim = new TaggedTransportSimulator(r); var state = sim.Initial();
        for (int i = 0; i < 2; i++) Assert.True(sim.TryApply(state, new(TaggedActionKind.Transfer, "Lema", "ship", "main", "grass", 1), out state, out _));
        Assert.Equal(2400, sim.Weight(state, "main"));
        Assert.False(sim.TryApply(state, new(TaggedActionKind.Transfer, "Lema", "ship", "main", "grass", 1), out _, out _));
    }
}
