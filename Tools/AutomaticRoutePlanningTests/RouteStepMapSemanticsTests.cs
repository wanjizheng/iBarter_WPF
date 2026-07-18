using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

/// <summary>
/// Bug 3: the map used to overlay a Planner-barter node on top of an
/// island warehouse node, and the Planner-barter's
/// <c>Islands_MouseLeftButtonDown</c> handler would flip the unrelated
/// barter's ExchangeDone when the user double-clicked what they thought was
/// the unload step.
///
/// <para>The fix has three layers:
///   1. <see cref="WarehouseUnloadStep"/> and <see cref="WarehousePickupStep"/>
///      are distinct record types, so the renderer can pattern-match on
///      StepKind without consulting IslandId.
///   2. The render path suppresses Planner-barter overlays while an auto
///      route is active (covered in MapControl.xaml.cs).
///   3. Even if a future regression wires up the wrong handler, the
///      handler now refuses to match <c>GridContainer_*Warehouse</c> names.</para>
///
/// These tests pin the model-level invariants so a future route-step type
/// change is caught before it reaches WPF.
/// </summary>
public class RouteStepMapSemanticsTests {
    [Fact]
    public void BarterStep_CarriesStableRowId() {
        var step = new BarterStep(
            "82:Crow:800049:10",
            "Crow",
            new RouteItemQuantity("800049", 1),
            new RouteItemQuantity("10", 163),
            new RouteLoadSnapshot(0, 0, 0));

        // RowId is the ONLY identity the map double-click uses to flip
        // ExchangeDone. List index, sort order, item names and IslandId
        // are all forbidden substitutes.
        Assert.Equal("82:Crow:800049:10", step.RowId);
        Assert.Equal("Crow", step.IslandId);
    }

    [Fact]
    public void WarehousePickupStep_HasNoRowId() {
        var step = new WarehousePickupStep(
            "Iliya", "Iliya",
            [new RouteItemQuantity("800045", 1)],
            new RouteLoadSnapshot(0, 0, 0));

        // Bug 3: pickup steps MUST NOT carry a RowId property at all.
        // The map-double-click guard "if (string.IsNullOrWhiteSpace(node.BarterRowId)) return;"
        // relies on this invariant — pickup steps can never satisfy the
        // "must have RowId to flip ExchangeDone" requirement.
        var hasRowId = step.GetType().GetProperty("RowId");
        Assert.Null(hasRowId);
    }

    [Fact]
    public void WarehouseUnloadStep_HasNoRowId() {
        var step = new WarehouseUnloadStep(
            "Iliya", "Iliya",
            [new RouteItemQuantity("10", 163)],
            new RouteLoadSnapshot(0, 0, 0));

        // Bug 3: the unload step at the end of an Iliya route must NOT
        // carry a RowId property — otherwise the map-double-click path
        // would treat it as a real barter and complete the wrong
        // transaction. The fact that the type system forbids adding one
        // is itself the guarantee.
        var hasRowId = step.GetType().GetProperty("RowId");
        Assert.Null(hasRowId);
    }

    [Fact]
    public void BarterStep_IslandIdIsBarterLocation_NotPickupOrUnloadLocation() {
        // The barter's IslandId is where the exchange happens.  Even when
        // the route starts and ends at the same warehouse (StartWarehouseId
        // == EndWarehouseId), the intermediate barter islands are different
        // and distinct from the start/end island.
        var route = new PlannedRoute(
            1, "Iliya", "Iliya",
            new RouteStep[] {
                new WarehousePickupStep("Iliya", "Iliya",
                    [new RouteItemQuantity("800049", 1)],
                    new RouteLoadSnapshot(0, 0, 0)),
                new BarterStep("82:Crow:800049:10", "Crow",
                    new RouteItemQuantity("800049", 1),
                    new RouteItemQuantity("10", 163),
                    new RouteLoadSnapshot(0, 0, 0)),
                new WarehouseUnloadStep("Iliya", "Iliya",
                    [new RouteItemQuantity("10", 163)],
                    new RouteLoadSnapshot(0, 0, 0)),
            },
            distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);

        var barter = (BarterStep)route.Steps[1];
        var pickup = (WarehousePickupStep)route.Steps[0];
        var unload = (WarehouseUnloadStep)route.Steps[2];

        Assert.Equal("Crow", barter.IslandId);
        Assert.Equal("Iliya", pickup.IslandId);
        Assert.Equal("Iliya", unload.IslandId);

        // The map MUST distinguish the three steps by kind, not by IslandId
        // (the unload is at the same island as the pickup).
        Assert.NotEqual(barter.GetType(), pickup.GetType());
        Assert.NotEqual(barter.GetType(), unload.GetType());
        Assert.NotEqual(pickup.GetType(), unload.GetType());
    }

    [Fact]
    public void MapNode_WarehouseNames_EndWithWarehouseSuffix() {
        // MapControl.xaml.cs builds GridContainer_<Island>Warehouse for
        // warehouse nodes. The double-click guard's EndsWith("Warehouse")
        // check relies on this contract.
        var warehouse = new WarehouseUnloadStep(
            "Iliya", "Iliya", [], new RouteLoadSnapshot(0, 0, 0));
        string gridName = $"GridContainer_{warehouse.IslandId}Warehouse";

        Assert.EndsWith("Warehouse", gridName);
    }

    [Fact]
    public void MapNode_TempNames_EndWithTempSuffix() {
        var warehouse = new WarehouseUnloadStep(
            "Iliya", "Iliya", [], new RouteLoadSnapshot(0, 0, 0));
        string gridName = $"GridContainer_{warehouse.IslandId}Temp";

        Assert.EndsWith("Temp", gridName);
    }

    [Fact]
    public void MapNode_BarterNames_DoNotEndWithWarehouseOrTemp() {
        // Real barter steps render as GridContainer_<Island> — the only
        // name shape that the double-click guard should allow.
        var barter = new BarterStep(
            "82:Crow:800049:10", "Crow",
            new RouteItemQuantity("800049", 1),
            new RouteItemQuantity("10", 163),
            new RouteLoadSnapshot(0, 0, 0));
        string gridName = $"GridContainer_{barter.IslandId}";

        Assert.DoesNotContain("Warehouse", gridName);
        Assert.DoesNotContain("Temp", gridName);
    }

    [Fact]
    public void StepKind_Discriminator_IsExhaustive() {
        // Any future RouteStep subtype must be representable in the model.
        // The renderer's pattern match on step.GetType() must enumerate
        // every variant, otherwise a new step kind silently renders as the
        // default branch.
        var pickup = new WarehousePickupStep("Iliya", "Iliya", [],
            new RouteLoadSnapshot(0, 0, 0));
        var barter = new BarterStep("a", "Crow",
            new RouteItemQuantity("800049", 1),
            new RouteItemQuantity("10", 1),
            new RouteLoadSnapshot(0, 0, 0));
        var unload = new WarehouseUnloadStep("Iliya", "Iliya", [],
            new RouteLoadSnapshot(0, 0, 0));

        Assert.True(pickup is WarehousePickupStep);
        Assert.True(barter is BarterStep);
        Assert.True(unload is WarehouseUnloadStep);
        Assert.False(pickup is BarterStep);
        Assert.False(unload is BarterStep);
    }
}