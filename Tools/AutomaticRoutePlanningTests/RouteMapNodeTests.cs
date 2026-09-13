using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

/// <summary>
/// Bug 3 / Audit 3 (round 2): the map's double-click authorization MUST
/// be derived from <see cref="RouteMapNode"/> semantics, NOT from the
/// grid's <c>Name</c> suffix.  These tests pin the model contract so any
/// future regression that re-introduces an IslandId-based or
/// suffix-based authorization condition is caught.
/// </summary>
public class RouteMapNodeTests {
    [Fact]
    public void BarterNode_AuthorizesCompletion() {
        var node = new RouteMapNode(
            RouteNumber: 1, StepIndex: 2,
            StepKind: RouteStepKind.Barter,
            IslandId: "Iliya",
            BarterRowId: "Iliya:800208:800241",
            CanMarkDone: true,
            DisplayTitle: "金色仙人掌花束 → 匠人的贝壳项链",
            DisplayDetail: "5 → 5");
        Assert.True(node.AuthorizesCompletion);
    }

    [Fact]
    public void PickupNode_RefusesCompletion() {
        var node = new RouteMapNode(
            RouteNumber: 1, StepIndex: 0,
            StepKind: RouteStepKind.Pickup,
            IslandId: "Iliya",
            BarterRowId: null,
            CanMarkDone: false,
            DisplayTitle: "装货", DisplayDetail: "");
        Assert.False(node.AuthorizesCompletion);
    }

    [Fact]
    public void UnloadNode_RefusesCompletion() {
        var node = new RouteMapNode(
            RouteNumber: 1, StepIndex: 5,
            StepKind: RouteStepKind.Unload,
            IslandId: "Iliya",
            BarterRowId: null,
            CanMarkDone: false,
            DisplayTitle: "卸货", DisplayDetail: "");
        Assert.False(node.AuthorizesCompletion);
    }

    [Fact]
    public void BarterNode_WithEmptyRowId_RefusesCompletion() {
        // Audit round 2 test: a BarterStep-shaped node that lost its
        // RowId (e.g. serialization dropped it) MUST refuse to
        // complete. The map cannot fall back to IslandId lookup.
        var node = new RouteMapNode(
            RouteNumber: 1, StepIndex: 0,
            StepKind: RouteStepKind.Barter,
            IslandId: "Iliya",
            BarterRowId: "",
            CanMarkDone: true,
            DisplayTitle: "?", DisplayDetail: "");
        Assert.False(node.AuthorizesCompletion);
    }

    [Fact]
    public void NonBarterNode_CarryingRowId_StillRefusesCompletion() {
        // Audit round 2 test: even if a future bug causes a Pickup
        // node to carry a BarterRowId, AuthorizesCompletion MUST
        // refuse. The StepKind gate is the only authority.
        var node = new RouteMapNode(
            RouteNumber: 1, StepIndex: 0,
            StepKind: RouteStepKind.Pickup,
            IslandId: "Iliya",
            BarterRowId: "Iliya:800208:800241",
            CanMarkDone: true,
            DisplayTitle: "装货", DisplayDetail: "");
        Assert.False(node.AuthorizesCompletion);
    }

    [Fact]
    public void BarterNode_WithCanMarkDoneFalse_RefusesCompletion() {
        // A row that the planner has already marked done must not be
        // toggleable by the map (a second double-click should be a
        // no-op, not a toggle). CanMarkDone=false encodes that.
        var node = new RouteMapNode(
            RouteNumber: 1, StepIndex: 2,
            StepKind: RouteStepKind.Barter,
            IslandId: "Iliya",
            BarterRowId: "Iliya:800208:800241",
            CanMarkDone: false,
            DisplayTitle: "金色仙人掌花束 → 匠人的贝壳项链",
            DisplayDetail: "5 → 5 (done)");
        Assert.False(node.AuthorizesCompletion);
    }

    [Fact]
    public void UnloadNode_CarriesNoRowId_AndNoCanMarkDone() {
        // Both invariants are required by the map's tag contract.
        var node = new RouteMapNode(
            RouteNumber: 1, StepIndex: 5,
            StepKind: RouteStepKind.Unload,
            IslandId: "Iliya",
            BarterRowId: null,
            CanMarkDone: false,
            DisplayTitle: "伊利亚岛\n卸货", DisplayDetail: "");
        Assert.Null(node.BarterRowId);
        Assert.False(node.CanMarkDone);
    }

    [Fact]
    public void SameIsland_MultipleBarters_HaveDistinctRowIds() {
        // Audit round 2 test: the audit requires that even when two
        // barters live on the same island, only the one whose RowId
        // matches can be completed. The RowId must be unique enough
        // to disambiguate.
        var a = new RouteMapNode(1, 0, RouteStepKind.Barter, "Iliya",
            "Iliya:800208:800241", true, "a", "");
        var b = new RouteMapNode(1, 1, RouteStepKind.Barter, "Iliya",
            "Iliya:800241:800229", true, "b", "");
        Assert.Equal(a.IslandId, b.IslandId);
        Assert.NotEqual(a.BarterRowId, b.BarterRowId);
        Assert.NotEqual(a.StepIndex, b.StepIndex);
    }
}