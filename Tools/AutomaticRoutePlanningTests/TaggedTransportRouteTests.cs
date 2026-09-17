using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;
public class TaggedTransportRouteTests {
    [Fact] public void WarehouseTripsPartitionWithoutLosingOrReorderingTransfers() {
        var request = new TaggedTransportRequest();
        var actions = new TaggedAction[] {
            new(TaggedActionKind.Transfer, "Velia", "warehouse:Velia", "ship"),
            new(TaggedActionKind.Sail, "Kuit", "Velia"),
            new(TaggedActionKind.Switch, "Kuit", "main", "alt"),
            new(TaggedActionKind.Transfer, "Kuit", "alt", "ship"),
            new(TaggedActionKind.Sail, "Island", "Kuit"),
            new(TaggedActionKind.Barter, "Island"),
            new(TaggedActionKind.Sail, "Velia", "Island"),
            new(TaggedActionKind.Transfer, "Velia", "ship", "warehouse:Velia"),
            new(TaggedActionKind.StackAtWarehouse, "Velia", "warehouse:Velia", "main-elephant"),
            new(TaggedActionKind.Sail, "Island", "Velia"),
            new(TaggedActionKind.Barter, "Island"),
            new(TaggedActionKind.Sail, "Velia", "Island"),
            new(TaggedActionKind.Transfer, "Velia", "ship", "warehouse:Velia"),
        };
        var plan = new TaggedTransportPlan("", actions.Select(a => new TaggedTransportStep(a, 0, 0, 0, 0, 0)).ToArray(), 0, 0, 0, 0, "");
        var routes = TaggedTransportRoutes.Build(request, plan);
        Assert.Equal(2, routes.Length);
        Assert.Equal(new TaggedTransportRoute(1, 0, 8, "Velia"), routes[0]);
        Assert.Equal(new TaggedTransportRoute(2, 8, 13, "Velia"), routes[1]);
        Assert.Equal(Enumerable.Range(0, actions.Length), routes.SelectMany(r => Enumerable.Range(r.Start, r.End - r.Start)));
    }
    [Fact] public void EmptyPlanDoesNotInventARoute() {
        Assert.Empty(TaggedTransportRoutes.Build(new(), new("", [], 0, 0, 0, 0, "")));
    }
}
