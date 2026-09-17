using iBarter.Routing;
using Xunit;
namespace AutomaticRoutePlanningTests;
public class TaggedOperationGroupTests {
    private static TaggedTransportPlan Plan(params TaggedAction[] actions) => new("", actions.Select(a => new TaggedTransportStep(a, 0, 0, 0, 0, 0)).ToArray(), 0, 0, 0, 0, "");
    [Fact] public void ElevenLoadsBecomeOneCardAndUnloadIsGroupedSeparately() {
        var actions = Enumerable.Range(0, 11).Select(i => new TaggedAction(TaggedActionKind.Transfer, "Velia", "warehouse:Velia", i % 2 == 0 ? "ship" : "main", "a", 1))
            .Concat(new TaggedAction[] { new(TaggedActionKind.Sail, "A"), new(TaggedActionKind.Barter, "A"), new(TaggedActionKind.Sail, "Velia"),
                new(TaggedActionKind.Transfer, "Velia", "ship", "warehouse:Velia", "b", 1), new(TaggedActionKind.Transfer, "Velia", "main", "warehouse:Velia", "b", 1) }).ToArray();
        var plan = Plan(actions); var request = new TaggedTransportRequest();
        var groups = TaggedOperationGroups.Build(request, plan);
        Assert.Equal(3, groups.Length);
        Assert.Equal(new TaggedOperationGroup(0, 11, "loading"), groups[0]);
        Assert.Equal("unloading", groups[2].Kind);
        Assert.Equal(11, TaggedOperationGroups.MoveCursor(request, plan, 0, true));
        Assert.Equal(11, TaggedOperationGroups.MoveCursor(request, plan, 5, true));
        Assert.Equal(0, TaggedOperationGroups.MoveCursor(request, plan, 5, false));
        Assert.Equal(0, TaggedOperationGroups.MoveCursor(request, plan, 11, false));
        Assert.Equal(16, TaggedOperationGroups.MoveCursor(request, plan, 13, true));
        Assert.Equal(13, TaggedOperationGroups.MoveCursor(request, plan, 16, false));
    }
    [Fact] public void MidRouteWharfTransfersAndSwitchesFormOneOrderedCard() {
        var plan = Plan(new(TaggedActionKind.Sail, "Kuit"), new(TaggedActionKind.Switch, "Kuit", "main", "alt"),
            new(TaggedActionKind.Transfer, "Kuit", "ship", "alt", "a", 1), new(TaggedActionKind.Transfer, "Kuit", "alt", "ship", "b", 1));
        var groups = TaggedOperationGroups.Build(new(), plan);
        Assert.Equal(new TaggedOperationGroup(1, 4, "transfer"), Assert.Single(groups));
        Assert.Equal(4, TaggedOperationGroups.MoveCursor(new(), plan, 0, true));
        Assert.Equal(0, TaggedOperationGroups.MoveCursor(new(), plan, 4, false));
    }
    [Theory] [InlineData("main")] [InlineData("alt")]
    public void WarehouseShuttleDisplaysItsFinalRecipientOnce(string owner) {
        var plan = Plan(new(TaggedActionKind.Transfer, "Iliya", "warehouse:Iliya", "ship", "a", 3),
            new(TaggedActionKind.Transfer, "Iliya", "ship", owner, "a", 3),
            new(TaggedActionKind.Transfer, "Iliya", "warehouse:Iliya", "ship", "a", 2));
        var lines = TaggedOperationGroups.Summarize(plan, new(0, 3, "loading"), 0);
        Assert.Equal(2, lines.Length);
        Assert.Equal(new TaggedAction(TaggedActionKind.Transfer, "Iliya", "warehouse:Iliya", owner, "a", 3), lines[0].Action);
        Assert.Equal(2, lines[1].Action.Quantity); Assert.Equal("ship", lines[1].Action.To);
        Assert.Equal("ship", plan.Steps[0].Action.To); Assert.Equal(3, plan.Steps.Length);
    }
    [Fact] public void IndividualReceivesCollapseBeforeSummarizingTheShuttle() {
        var plan = Plan(new(TaggedActionKind.Transfer, "Iliya", "warehouse:Iliya", "ship", "a", 3),
            new(TaggedActionKind.Transfer, "Iliya", "ship", "main", "a", 1),
            new(TaggedActionKind.Transfer, "Iliya", "ship", "main", "a", 1),
            new(TaggedActionKind.Transfer, "Iliya", "ship", "main", "a", 1));
        var line = Assert.Single(TaggedOperationGroups.Summarize(plan, new(0, 4, "loading"), 0));
        Assert.Equal(3, line.Action.Quantity); Assert.Equal("main", line.Action.To);
        Assert.Equal(2, TaggedOperationGroups.Summarize(plan, new(0, 4, "loading"), 1).Length);
    }
    [Fact] public void UnloadShuttleSummarizesButUnequalQuantitiesRemainExplicit() {
        var plan = Plan(new(TaggedActionKind.Transfer, "Iliya", "main", "ship", "a", 3),
            new(TaggedActionKind.Transfer, "Iliya", "ship", "warehouse:Iliya", "a", 3));
        var line = Assert.Single(TaggedOperationGroups.Summarize(plan, new(0, 2, "unloading"), 0));
        Assert.Equal("main", line.Action.From); Assert.Equal("warehouse:Iliya", line.Action.To);
        plan = Plan(plan.Steps[0].Action, plan.Steps[1].Action with { Quantity = 2 });
        Assert.Equal(2, TaggedOperationGroups.Summarize(plan, new(0, 2, "unloading"), 0).Length);
    }
}
