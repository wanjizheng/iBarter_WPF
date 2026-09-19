using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;
public class TaggedTransportRouteTests {
    [Fact] public void ForeignPortBarterAndSalesDoNotSplitTheHomeboundVoyage() {
        var request = new TaggedTransportRequest { Settings = new() { HomeWarehouseId = "Iliya", StartIsland = "Midnight" } };
        var actions = new TaggedAction[] {
            new(TaggedActionKind.Sail, "Olvia", "Midnight"), new(TaggedActionKind.Barter, "Olvia"),
            new(TaggedActionKind.Sail, "Epheria", "Olvia"), new(TaggedActionKind.Sell, "Epheria", "ship", "shop", "a", 5),
            new(TaggedActionKind.Barter, "Epheria"), new(TaggedActionKind.Sail, "Iliya", "Epheria"),
            new(TaggedActionKind.Transfer, "Iliya", "ship", "warehouse:Iliya", "b", 3),
            new(TaggedActionKind.Transfer, "Iliya", "warehouse:Iliya", "ship", "c", 5),
            new(TaggedActionKind.Sail, "A", "Iliya"), new(TaggedActionKind.Barter, "A") };
        var plan = new TaggedTransportPlan("", actions.Select(a => new TaggedTransportStep(a, 0, 0, 0, 0, 0)).ToArray(), 0, 0, 0, 0, "");
        var routes = TaggedTransportRoutes.Build(request, plan);
        Assert.Equal(2, routes.Length); Assert.Equal(7, routes[0].End);
        Assert.Equal(2, plan.Steps[routes[0].Start..routes[0].End].Count(s => s.Action.Kind == TaggedActionKind.Barter));
    }
    [Fact] public void CompletingFirstTripRetainsLaterNumbersAcrossSavingAndFurtherCompletion() {
        var request = new TaggedTransportRequest { Trades = [new("a", "A", "x", 1, "y", 1, 1), new("b", "B", "x", 1, "y", 1, 1), new("c", "C", "x", 1, "y", 1, 1)] };
        TaggedTransportPlan Plan(params int[] indices) => new("", indices.SelectMany(i => new TaggedAction[] {
            new(TaggedActionKind.Sail, request.Trades[i].IslandId, "Velia"),
            new(TaggedActionKind.Barter, request.Trades[i].IslandId, Quantity: 1, TradeIndex: i),
            new(TaggedActionKind.Sail, "Velia", request.Trades[i].IslandId)
        }).Select(a => new TaggedTransportStep(a, 0, 0, 0, 0, 0)).ToArray(), 0, 0, 0, 0, "");
        var remaining = TaggedTransportRoutes.RetainNumbers(request, Plan(0, 1, 2), request, Plan(1, 2));
        Assert.Equal(new[] { 2, 3 }, TaggedTransportRoutes.Build(request, remaining).Select(r => r.Number));
        var restored = System.Text.Json.JsonSerializer.Deserialize<TaggedTransportPlan>(System.Text.Json.JsonSerializer.Serialize(remaining))!;
        var last = TaggedTransportRoutes.RetainNumbers(request, restored, request, Plan(2));
        Assert.Equal(3, Assert.Single(TaggedTransportRoutes.Build(request, last)).Number);
        // A capacity-split row can occur on multiple trips. The just-completed
        // occurrence must not claim the surviving occurrence's route number.
        var split = TaggedTransportRoutes.RetainNumbers(request, Plan(0, 0), request, Plan(0), completedStep: 1);
        Assert.Equal(2, Assert.Single(TaggedTransportRoutes.Build(request, split)).Number);
    }
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
