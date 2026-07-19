using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

/// <summary>
/// Audit round 3 — cross-route cargo demand isolation.
///
/// <para>Spec violation: <see cref="RouteCargoNormalizer"/> previously
/// attached the consumption profile of every later route's BarterStep to
/// each route's pickup filter, so a route that incidentally picked up an
/// item it never consumed (because a later route did) kept the cargo and
/// was forced to unload it back at the source warehouse. The user observed
/// this in the field as a "零价值原仓往返" — 800061 picked up at Iliya
/// for Route 1, dropped back at Iliya at the end of the same route,
/// occupying 5000 LT it never used.</para>
///
/// <para>These tests pin the route-local cargo demand invariant: every
/// pickup/unload decision must reference ONLY the current route's BarterStep
/// set, and the per-prefix (order-sensitive) demand must rule out any
/// quantity the simulator would not actually need.</para>
/// </summary>
public class CrossRouteCargoLeakTests {
    private static RouteWarehouse Warehouse(string id, double x, double y,
        params (string Item, int Qty)[] stock) =>
        new(id, id, new RoutePoint(x, y),
            stock.ToDictionary(s => s.Item, s => s.Qty, StringComparer.Ordinal));

    // Test #1 — field bug fixture: real Route 1 + Route 3 shape.
    [Fact]
    public void Test1_CrossRouteLeak_Route1StripsPickedUpCargo_Route3KeepsIt() {
        // Items: X (the leak, never consumed in Route 1), CONS1..7 (consumed in R1).
        // Stock at Iliya: X=7.
        // Route 1: pickup X×5 + CONS1..3, barter through CONS1..3, end with
        //   barter outputs only on board, then unload back at Iliya a
        //   dump that legitimately contains X×5 (a phantom carry) plus the
        //   real output items.
        // Route 3: pickup X×5, consume X×5 → PROD, unload PROD at Velia.
        // After normalization:
        //   * Route 1 pickup must DROP X (no R1 barter consumes X).
        //   * Route 1 unload at Iliya must DROP X (R1 never picked it up).
        //   * Route 3 pickup and consume are untouched.
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["X"]     = new("X",     "Tear",     5, 1000),
            ["CONS1"] = new("CONS1", "C1",       1,  100),
            ["CONS2"] = new("CONS2", "C2",       1,  100),
            ["CONS3"] = new("CONS3", "C3",       1,  100),
            ["PROD1"] = new("PROD1", "P1",       1,    0),
            ["PROD2"] = new("PROD2", "P2",       1,    0),
            ["PROD3"] = new("PROD3", "P3",       1,    0),
        };
        var warehouses = new[] {
            Warehouse("Iliya", 0,    0, ("X", 7), ("CONS1", 5), ("CONS2", 5), ("CONS3", 5)),
            Warehouse("Velia", 1000, 0),
        };
        var request = new AutomaticRoutePlanningRequest(
            [
                // Route 3 task: consume X at Haemo, produce PROD4.
                new RouteBarterTask("consume-X", "Haemo", new RoutePoint(500, 0),
                    "X", 5, "PROD4", 5),
            ],
            items, warehouses, extraLT: 0, totalLT: 100_000,
            new RouteSearchLimits(100_000, 1_000), "test1");

        var route1 = new PlannedRoute(1, "Iliya", "Velia", new RouteStep[] {
            new WarehousePickupStep("Iliya", "Iliya",
                [
                    new RouteItemQuantity("X",     5),
                    new RouteItemQuantity("CONS1", 1),
                    new RouteItemQuantity("CONS2", 1),
                    new RouteItemQuantity("CONS3", 1),
                ],
                new RouteLoadSnapshot(5300, 5300, 5300)),
            new BarterStep("c1", "Haemo",
                new RouteItemQuantity("CONS1", 1), new RouteItemQuantity("PROD1", 1),
                new RouteLoadSnapshot(5000, 5000, 5300)),
            new BarterStep("c2", "Olvia",
                new RouteItemQuantity("CONS2", 1), new RouteItemQuantity("PROD2", 1),
                new RouteLoadSnapshot(5000, 5000, 5300)),
            new BarterStep("c3", "Epheria",
                new RouteItemQuantity("CONS3", 1), new RouteItemQuantity("PROD3", 1),
                new RouteLoadSnapshot(5000, 5000, 5300)),
            new WarehouseUnloadStep("Iliya", "Iliya",
                [
                    new RouteItemQuantity("X",     5),    // phantom: only valid if X stayed on board
                    new RouteItemQuantity("PROD1", 1),
                    new RouteItemQuantity("PROD2", 1),
                    new RouteItemQuantity("PROD3", 1),
                ],
                new RouteLoadSnapshot(0, 0, 0)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);

        // Route 3 uses the consume-X task.
        var route3 = new PlannedRoute(3, "Iliya", "Velia", new RouteStep[] {
            new WarehousePickupStep("Iliya", "Iliya",
                [new RouteItemQuantity("X", 5)],
                new RouteLoadSnapshot(5000, 5000, 5000)),
            new BarterStep("consume-X", "Haemo",
                new RouteItemQuantity("X", 5), new RouteItemQuantity("PROD4", 5),
                new RouteLoadSnapshot(0, 0, 0)),
            new WarehouseUnloadStep("Velia", "Velia",
                [new RouteItemQuantity("PROD4", 5)],
                new RouteLoadSnapshot(0, 0, 0)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);

        var plan = new RoutePlan(RoutePlanStatus.Optimal, [route1, route3], null, [], "test1");
        var normalized = RouteCargoNormalizer.Normalize(request, plan);

        var normR1 = normalized.Routes.Single(r => r.Number == 1);
        var normR3 = normalized.Routes.Single(r => r.Number == 3);

        var r1Pickup = (WarehousePickupStep)normR1.Steps[0];
        Assert.DoesNotContain(r1Pickup.Items, x => x.ItemId == "X");
        var r1Unload = (WarehouseUnloadStep)normR1.Steps.Last(s => s is WarehouseUnloadStep);
        Assert.DoesNotContain(r1Unload.Items, x => x.ItemId == "X");

        var r3Pickup = (WarehousePickupStep)normR3.Steps[0];
        Assert.Contains(r3Pickup.Items, x => x.ItemId == "X" && x.Quantity == 5);
        var r3Barter = (BarterStep)normR3.Steps.First(s => s is BarterStep);
        Assert.Equal("X", r3Barter.Consumed.ItemId);
        Assert.Equal(5, r3Barter.Consumed.Quantity);
    }

    // Test #2 — plan-global isolation: Route 1 must drop X even when Route 3
    // would consume it. We pass only Route 1 to NormalizeRoute; the other
    // route's demand must be invisible to the per-route filter.
    [Fact]
    public void Test2_PlanGlobalConsumption_DoesNotLeakIntoOtherRoute() {
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["X"]     = new("X",     "Tear", 5, 1000),
            ["NEED"]  = new("NEED",  "Need", 1,  100),
            ["PROD"]  = new("PROD",  "Out",  1,    0),
        };
        var warehouses = new[] {
            Warehouse("Iliya", 0, 0, ("X", 7), ("NEED", 5)),
            Warehouse("Velia", 1000, 0),
        };
        var request = new AutomaticRoutePlanningRequest(
            Array.Empty<RouteBarterTask>(),
            items, warehouses, extraLT: 0, totalLT: 100_000,
            new RouteSearchLimits(100_000, 1_000), "test2");

        // Route 1 only: pickup X×5, NEED×5 (consumed), unload PROD only.
        // X is NEVER consumed in this route; unload must NOT carry it.
        var route1 = new PlannedRoute(1, "Iliya", "Velia", new RouteStep[] {
            new WarehousePickupStep("Iliya", "Iliya",
                [
                    new RouteItemQuantity("X",    5),
                    new RouteItemQuantity("NEED", 5),
                ],
                new RouteLoadSnapshot(5500, 5500, 5500)),
            new BarterStep("use", "Haemo",
                new RouteItemQuantity("NEED", 5), new RouteItemQuantity("PROD", 5),
                new RouteLoadSnapshot(5000, 5000, 5500)),
            new WarehouseUnloadStep("Iliya", "Iliya",
                [
                    new RouteItemQuantity("X",    5),  // phantom, must be dropped
                    new RouteItemQuantity("PROD", 5),
                ],
                new RouteLoadSnapshot(0, 0, 0)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);

        var (normalized, changed) = RouteCargoNormalizer.NormalizeRoute(request, route1);
        Assert.True(changed, "Pickup containing unconsumed X must trigger a change");
        var pickup = (WarehousePickupStep)normalized.Steps[0];
        Assert.DoesNotContain(pickup.Items, x => x.ItemId == "X");
        var unload = (WarehouseUnloadStep)normalized.Steps.Last(s => s is WarehouseUnloadStep);
        Assert.DoesNotContain(unload.Items, x => x.ItemId == "X");
    }

    // Test #3 — fresh generation: real planner output normalizing should
    // strip any cross-route leak. We hand-build a plan that matches what the
    // real planner would emit (Route 1 with a phantom X pickup, Route 3 with
    // real X consumption), feed it through Normalize, and inspect the result.
    [Fact]
    public void Test3_FreshGeneration_NormalizerAtPlanLevelFixesLeak() {
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["X"]    = new("X",    "Tear", 5, 1000),
            ["R1A"]  = new("R1A",  "R1A",  1,  100),
            ["R1B"]  = new("R1B",  "R1B",  1,  100),
            ["R1O"]  = new("R1O",  "R1O",  1,    0),
        };
        var warehouses = new[] {
            Warehouse("Iliya", 0, 0, ("X", 7), ("R1A", 5), ("R1B", 5)),
            Warehouse("Velia", 1000, 0),
        };
        var request = new AutomaticRoutePlanningRequest(
            [
                new RouteBarterTask("consume-X", "Haemo", new RoutePoint(500, 0),
                    "X", 5, "XOUT", 5),
            ],
            items, warehouses, extraLT: 0, totalLT: 100_000,
            new RouteSearchLimits(100_000, 1_000), "test3");

        var route1 = new PlannedRoute(1, "Iliya", "Velia", new RouteStep[] {
            new WarehousePickupStep("Iliya", "Iliya",
                [
                    new RouteItemQuantity("X",   5),
                    new RouteItemQuantity("R1A", 1),
                    new RouteItemQuantity("R1B", 1),
                ],
                new RouteLoadSnapshot(5200, 5200, 5200)),
            new BarterStep("r1a", "Haemo",
                new RouteItemQuantity("R1A", 1), new RouteItemQuantity("R1O", 1),
                new RouteLoadSnapshot(5000, 5000, 5200)),
            new BarterStep("r1b", "Olvia",
                new RouteItemQuantity("R1B", 1), new RouteItemQuantity("R1O", 1),
                new RouteLoadSnapshot(5000, 5000, 5200)),
            new WarehouseUnloadStep("Iliya", "Iliya",
                [
                    new RouteItemQuantity("X",   5),  // phantom
                    new RouteItemQuantity("R1O", 2),
                ],
                new RouteLoadSnapshot(0, 0, 0)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);

        var route3 = new PlannedRoute(3, "Iliya", "Velia", new RouteStep[] {
            new WarehousePickupStep("Iliya", "Iliya",
                [new RouteItemQuantity("X", 5)],
                new RouteLoadSnapshot(5000, 5000, 5000)),
            new BarterStep("consume-X", "Haemo",
                new RouteItemQuantity("X", 5), new RouteItemQuantity("XOUT", 5),
                new RouteLoadSnapshot(0, 0, 0)),
            new WarehouseUnloadStep("Velia", "Velia",
                [new RouteItemQuantity("XOUT", 5)],
                new RouteLoadSnapshot(0, 0, 0)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);

        var plan = new RoutePlan(RoutePlanStatus.Optimal, [route1, route3], null, [], "test3");
        var normalized = RouteCargoNormalizer.Normalize(request, plan);

        var normR1 = normalized.Routes.Single(r => r.Number == 1);
        var r1Pickup = (WarehousePickupStep)normR1.Steps[0];
        Assert.DoesNotContain(r1Pickup.Items, x => x.ItemId == "X");
    }

    // Test #4 — partial redundant (pickup 5, consume 3, unload 2).
    [Fact]
    public void Test4_PartialRedundant_CapsToConsumedQuantity() {
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["X"]   = new("X",   "Tear", 5, 1000),
            ["OUT"] = new("OUT", "Out",  1,    0),
        };
        var warehouses = new[] {
            Warehouse("Iliya", 0,    0, ("X", 10)),
            Warehouse("Mid",  500,    0),
        };
        var request = new AutomaticRoutePlanningRequest(
            Array.Empty<RouteBarterTask>(),
            items, warehouses, extraLT: 0, totalLT: 100_000,
            new RouteSearchLimits(100_000, 1_000), "test4");

        var route = new PlannedRoute(1, "Iliya", "Iliya", new RouteStep[] {
            new WarehousePickupStep("Iliya", "Iliya",
                [new RouteItemQuantity("X", 5)],
                new RouteLoadSnapshot(5000, 5000, 5000)),
            new BarterStep("use3", "Mid",
                new RouteItemQuantity("X", 3), new RouteItemQuantity("OUT", 3),
                new RouteLoadSnapshot(2000, 2000, 5000)),
            new WarehouseUnloadStep("Iliya", "Iliya",
                [new RouteItemQuantity("X", 2), new RouteItemQuantity("OUT", 3)],
                new RouteLoadSnapshot(0, 0, 0)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);

        var (normalized, changed) = RouteCargoNormalizer.NormalizeRoute(request, route);
        Assert.True(changed);
        var pickup = (WarehousePickupStep)normalized.Steps[0];
        Assert.Single(pickup.Items);
        Assert.Equal("X", pickup.Items[0].ItemId);
        Assert.Equal(3, pickup.Items[0].Quantity);
        var unload = (WarehouseUnloadStep)normalized.Steps.Last(s => s is WarehouseUnloadStep);
        Assert.DoesNotContain(unload.Items, x => x.ItemId == "X");
        Assert.Contains(unload.Items, x => x.ItemId == "OUT");
    }

    // Test #5 — route-local barter output is preserved.
    [Fact]
    public void Test5_BarterOutputIsPreservedInUnload() {
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["RAW"] = new("RAW", "Raw", 1, 100),
            ["OUT"] = new("OUT", "Out", 1,   0),
        };
        var warehouses = new[] {
            Warehouse("Iliya", 0,    0, ("RAW", 5)),
            Warehouse("Velia", 1000, 0),
        };
        var request = new AutomaticRoutePlanningRequest(
            Array.Empty<RouteBarterTask>(),
            items, warehouses, extraLT: 0, totalLT: 100_000,
            new RouteSearchLimits(100_000, 1_000), "test5");

        var route = new PlannedRoute(1, "Iliya", "Velia", new RouteStep[] {
            new WarehousePickupStep("Iliya", "Iliya",
                [new RouteItemQuantity("RAW", 3)],
                new RouteLoadSnapshot(300, 300, 300)),
            new BarterStep("make", "Mid",
                new RouteItemQuantity("RAW", 3), new RouteItemQuantity("OUT", 3),
                new RouteLoadSnapshot(0, 0, 0)),
            new WarehouseUnloadStep("Velia", "Velia",
                [new RouteItemQuantity("OUT", 3)],
                new RouteLoadSnapshot(0, 0, 0)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);

        var (normalized, _) = RouteCargoNormalizer.NormalizeRoute(request, route);
        var unload = (WarehouseUnloadStep)normalized.Steps.Last(s => s is WarehouseUnloadStep);
        Assert.Contains(unload.Items, x => x.ItemId == "OUT" && x.Quantity == 3);
    }

    // Test #7 — another route's demand must not resurrect items.
    [Fact]
    public void Test7_ConsumedElsewhere_StillDroppedFromCurrentRoute() {
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["X"]    = new("X",    "Tear", 5, 1000),
            ["NEED"] = new("NEED", "Need", 1,  100),
            ["OUT"]  = new("OUT",  "Out",  1,    0),
        };
        var warehouses = new[] {
            Warehouse("Iliya", 0, 0, ("X", 7), ("NEED", 5)),
            Warehouse("Velia", 1000, 0),
        };
        var request = new AutomaticRoutePlanningRequest(
            Array.Empty<RouteBarterTask>(),
            items, warehouses, extraLT: 0, totalLT: 100_000,
            new RouteSearchLimits(100_000, 1_000), "test7");

        // Plan with two routes. Route 1 never consumes X. Route 2 does.
        var route1 = new PlannedRoute(1, "Iliya", "Iliya", new RouteStep[] {
            new WarehousePickupStep("Iliya", "Iliya",
                [new RouteItemQuantity("X", 5), new RouteItemQuantity("NEED", 5)],
                new RouteLoadSnapshot(5500, 5500, 5500)),
            new BarterStep("use", "Haemo",
                new RouteItemQuantity("NEED", 5), new RouteItemQuantity("OUT", 5),
                new RouteLoadSnapshot(5000, 5000, 5500)),
            new WarehouseUnloadStep("Iliya", "Iliya",
                [new RouteItemQuantity("X", 5), new RouteItemQuantity("OUT", 5)],
                new RouteLoadSnapshot(0, 0, 0)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);
        var route2 = new PlannedRoute(2, "Iliya", "Velia", new RouteStep[] {
            new WarehousePickupStep("Iliya", "Iliya",
                [new RouteItemQuantity("X", 2)],
                new RouteLoadSnapshot(2000, 2000, 2000)),
            new BarterStep("consume-X", "Haemo",
                new RouteItemQuantity("X", 2), new RouteItemQuantity("OUT", 2),
                new RouteLoadSnapshot(0, 0, 0)),
            new WarehouseUnloadStep("Velia", "Velia",
                [new RouteItemQuantity("OUT", 2)],
                new RouteLoadSnapshot(0, 0, 0)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);

        var plan = new RoutePlan(RoutePlanStatus.Optimal, [route1, route2], null, [], "test7");
        var normalized = RouteCargoNormalizer.Normalize(request, plan);

        var normR1 = normalized.Routes.Single(r => r.Number == 1);
        var r1Pickup = (WarehousePickupStep)normR1.Steps[0];
        Assert.DoesNotContain(r1Pickup.Items, x => x.ItemId == "X");
        var r1Unload = (WarehouseUnloadStep)normR1.Steps.Last(s => s is WarehouseUnloadStep);
        Assert.DoesNotContain(r1Unload.Items, x => x.ItemId == "X");

        var normR2 = normalized.Routes.Single(r => r.Number == 2);
        var r2Pickup = (WarehousePickupStep)normR2.Steps[0];
        Assert.Contains(r2Pickup.Items, x => x.ItemId == "X" && x.Quantity == 2);
    }

    // Test #8 — multi warehouse: pickup X at A, transfer to B via route.
    // For now we assert no false-positive drop on the multi-warehouse case.
    [Fact]
    public void Test8_MultiWarehouse_NoFalsePositiveDrop() {
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["X"]   = new("X",   "Tear", 5, 1000),
            ["OUT"] = new("OUT", "Out",  1,    0),
        };
        var warehouses = new[] {
            Warehouse("A",    0,    0, ("X", 10)),
            Warehouse("B",  500,    0),
            Warehouse("C", 1000,    0),
        };
        var request = new AutomaticRoutePlanningRequest(
            Array.Empty<RouteBarterTask>(),
            items, warehouses, extraLT: 0, totalLT: 100_000,
            new RouteSearchLimits(100_000, 1_000), "test8");

        // Single route: pickup A X×5, barter at B consumes X×5, output OUT.
        var route = new PlannedRoute(1, "A", "C", new RouteStep[] {
            new WarehousePickupStep("A", "A",
                [new RouteItemQuantity("X", 5)],
                new RouteLoadSnapshot(5000, 5000, 5000)),
            new BarterStep("use", "B",
                new RouteItemQuantity("X", 5), new RouteItemQuantity("OUT", 5),
                new RouteLoadSnapshot(0, 0, 0)),
            new WarehouseUnloadStep("C", "C",
                [new RouteItemQuantity("OUT", 5)],
                new RouteLoadSnapshot(0, 0, 0)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);

        var (normalized, _) = RouteCargoNormalizer.NormalizeRoute(request, route);
        var pickup = (WarehousePickupStep)normalized.Steps[0];
        Assert.Contains(pickup.Items, x => x.ItemId == "X" && x.Quantity == 5);
    }

    // Test #9 — LT recompute: removing the leak must reduce CargoLT by
    // exactly the weight of the dropped item × quantity.
    [Fact]
    public void Test9_LTRecompute_DropsExactlyByWeightOfDroppedItems() {
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["X"]     = new("X",     "Tear", 5, 1000),
            ["CONS1"] = new("CONS1", "C1",   1,  100),
            ["CONS2"] = new("CONS2", "C2",   1,  100),
            ["PROD1"] = new("PROD1", "P1",   1,    0),
            ["PROD2"] = new("PROD2", "P2",   1,    0),
        };
        var warehouses = new[] {
            Warehouse("Iliya", 0,    0, ("X", 7), ("CONS1", 5), ("CONS2", 5)),
            Warehouse("Velia", 1000, 0),
        };
        var request = new AutomaticRoutePlanningRequest(
            Array.Empty<RouteBarterTask>(),
            items, warehouses, extraLT: 0, totalLT: 100_000,
            new RouteSearchLimits(100_000, 1_000), "test9");

        // 800061×5 weight = 5000; CONS1 + CONS2 = 100 + 100 = 200 total.
        // Before: CargoLT = 5200. After dropping X: CargoLT = 200.
        var route = new PlannedRoute(1, "Iliya", "Velia", new RouteStep[] {
            new WarehousePickupStep("Iliya", "Iliya",
                [
                    new RouteItemQuantity("X",     5),
                    new RouteItemQuantity("CONS1", 1),
                    new RouteItemQuantity("CONS2", 1),
                ],
                new RouteLoadSnapshot(5200, 5200, 5200)),
            new BarterStep("c1", "Haemo",
                new RouteItemQuantity("CONS1", 1), new RouteItemQuantity("PROD1", 1),
                new RouteLoadSnapshot(5000, 5000, 5200)),
            new BarterStep("c2", "Olvia",
                new RouteItemQuantity("CONS2", 1), new RouteItemQuantity("PROD2", 1),
                new RouteLoadSnapshot(5000, 5000, 5200)),
            new WarehouseUnloadStep("Iliya", "Iliya",
                [new RouteItemQuantity("X", 5), new RouteItemQuantity("PROD1", 1), new RouteItemQuantity("PROD2", 1)],
                new RouteLoadSnapshot(0, 0, 0)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);

        var (normalized, changed) = RouteCargoNormalizer.NormalizeRoute(request, route);
        Assert.True(changed);
        var pickup = (WarehousePickupStep)normalized.Steps[0];
        Assert.DoesNotContain(pickup.Items, x => x.ItemId == "X");
        // 5200 - 5000 = 200
        Assert.Equal(200, pickup.Load.CargoLT);
    }

    // Test #10 — persistence round-trip.
    [Fact]
    public void Test10_PersistenceRoundTrip_DoesNotResurrectPrunedItems() {
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["X"]   = new("X",   "Tear", 5, 1000),
            ["RAW"] = new("RAW", "Raw",  1,  100),
            ["OUT"] = new("OUT", "Out",  1,    0),
        };
        var warehouses = new[] {
            Warehouse("Iliya", 0, 0, ("X", 7), ("RAW", 5)),
            Warehouse("Velia", 1000, 0),
        };
        var request = new AutomaticRoutePlanningRequest(
            Array.Empty<RouteBarterTask>(),
            items, warehouses, extraLT: 0, totalLT: 100_000,
            new RouteSearchLimits(100_000, 1_000), "test10");

        var route = new PlannedRoute(1, "Iliya", "Velia", new RouteStep[] {
            new WarehousePickupStep("Iliya", "Iliya",
                [new RouteItemQuantity("X", 5), new RouteItemQuantity("RAW", 5)],
                new RouteLoadSnapshot(5500, 5500, 5500)),
            new BarterStep("use", "Haemo",
                new RouteItemQuantity("RAW", 5), new RouteItemQuantity("OUT", 5),
                new RouteLoadSnapshot(5000, 5000, 5500)),
            new WarehouseUnloadStep("Iliya", "Iliya",
                [new RouteItemQuantity("X", 5), new RouteItemQuantity("OUT", 5)],
                new RouteLoadSnapshot(0, 0, 0)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [route], null, [], "test10");

        var normalized = RouteCargoNormalizer.Normalize(request, plan);
        string tempPath = Path.Combine(Path.GetTempPath(),
            $"autoroute-crosroute-{Guid.NewGuid():N}.json");
        try {
            RoutePlanPersistence.Save(tempPath, normalized, 1, false);
            var loaded = RoutePlanPersistence.TryLoad(tempPath, "test10");
            Assert.Equal(RoutePlanLoadStatus.Loaded, loaded.Status);
            var reloaded = loaded.Snapshot!.Plan;
            var pickup = (WarehousePickupStep)reloaded.Routes[0].Steps[0];
            Assert.DoesNotContain(pickup.Items, x => x.ItemId == "X");
        }
        finally {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    // Test #11 — determinism: two consecutive normalizations must converge
    // (the second is a no-op because the first already dropped X).
    [Fact]
    public void Test11_Determinism_SecondPassIsNoOp() {
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["X"]   = new("X",   "Tear", 5, 1000),
            ["RAW"] = new("RAW", "Raw",  1,  100),
            ["OUT"] = new("OUT", "Out",  1,    0),
        };
        var warehouses = new[] {
            Warehouse("Iliya", 0, 0, ("X", 7), ("RAW", 5)),
            Warehouse("Velia", 1000, 0),
        };
        var request = new AutomaticRoutePlanningRequest(
            Array.Empty<RouteBarterTask>(),
            items, warehouses, extraLT: 0, totalLT: 100_000,
            new RouteSearchLimits(100_000, 1_000), "test11");
        var route = new PlannedRoute(1, "Iliya", "Velia", new RouteStep[] {
            new WarehousePickupStep("Iliya", "Iliya",
                [new RouteItemQuantity("X", 5), new RouteItemQuantity("RAW", 5)],
                new RouteLoadSnapshot(5500, 5500, 5500)),
            new BarterStep("use", "Haemo",
                new RouteItemQuantity("RAW", 5), new RouteItemQuantity("OUT", 5),
                new RouteLoadSnapshot(5000, 5000, 5500)),
            new WarehouseUnloadStep("Iliya", "Iliya",
                [new RouteItemQuantity("X", 5), new RouteItemQuantity("OUT", 5)],
                new RouteLoadSnapshot(0, 0, 0)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [route], null, [], "test11");

        var first = RouteCargoNormalizer.Normalize(request, plan);
        var firstPickup = (WarehousePickupStep)first.Routes[0].Steps[0];
        Assert.DoesNotContain(firstPickup.Items, x => x.ItemId == "X");

        var second = RouteCargoNormalizer.Normalize(request, first);
        // Second pass must be a no-op — same instance.
        Assert.Same(first, second);
    }

    // Test #12 — verifier rejection for a deliberately bad plan.
    [Fact]
    public void Test12_VerifierRejects_RoundtripPickupWithoutConsumption() {
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["X"]   = new("X",   "Tear", 5, 1000),
            ["RAW"] = new("RAW", "Raw",  1,  100),
            ["OUT"] = new("OUT", "Out",  1,    0),
        };
        var warehouses = new[] {
            Warehouse("Iliya", 0,    0, ("X", 7), ("RAW", 5)),
            Warehouse("Velia", 1000, 0),
        };
        var request = new AutomaticRoutePlanningRequest(
            [
                new RouteBarterTask("make", "Haemo", new RoutePoint(500, 0),
                    "RAW", 5, "OUT", 5),
            ],
            items, warehouses, extraLT: 0, totalLT: 100_000,
            new RouteSearchLimits(100_000, 1_000), "test12");

        // Test the route-redundant-cargo-roundtrip detector directly.
        // The plan: pickup Iliya X×5 + RAW×5, barter consumes RAW (not X),
        // unload Iliya X×5 + OUT×5. Round-trip persists for X because no
        // barter in the route touches X.
        var route = new PlannedRoute(1, "Iliya", "Iliya", new RouteStep[] {
            new WarehousePickupStep("Iliya", "Iliya",
                [new RouteItemQuantity("RAW", 5), new RouteItemQuantity("X", 5)],
                new RouteLoadSnapshot(5500, 5500, 5500)),
            new BarterStep("make", "Haemo",
                new RouteItemQuantity("RAW", 5), new RouteItemQuantity("OUT", 5),
                new RouteLoadSnapshot(5000, 5000, 5500)),
            new WarehouseUnloadStep("Iliya", "Iliya",
                [new RouteItemQuantity("OUT", 5), new RouteItemQuantity("X", 5)],
                new RouteLoadSnapshot(0, 0, 5500)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);
        var plan = new RoutePlan(RoutePlanStatus.Optimal, [route], null, [],
            RoutePlanFingerprint.Compute(request));

        // Direct: the new helper must flag X.
        Assert.True(RoutePlanVerifier.TryDetectRouteRedundantCargoRoundTrip(plan, out var detail));
        Assert.Contains("route=1", detail);
        Assert.Contains("warehouse=Iliya", detail);
        Assert.Contains("item=X", detail);
        Assert.Contains("picked=5", detail);
        Assert.Contains("sameWarehouseReturned=5", detail);

        // A clean plan (no round-trip) must NOT trip the detector.
        var cleanRoute = new PlannedRoute(1, "Iliya", "Iliya", new RouteStep[] {
            new WarehousePickupStep("Iliya", "Iliya",
                [new RouteItemQuantity("RAW", 5)],
                new RouteLoadSnapshot(500, 500, 500)),
            new BarterStep("make", "Haemo",
                new RouteItemQuantity("RAW", 5), new RouteItemQuantity("OUT", 5),
                new RouteLoadSnapshot(0, 0, 500)),
            new WarehouseUnloadStep("Iliya", "Iliya",
                [new RouteItemQuantity("OUT", 5)],
                new RouteLoadSnapshot(0, 0, 0)),
        }, distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);
        var cleanPlan = new RoutePlan(RoutePlanStatus.Optimal, [cleanRoute], null, [],
            RoutePlanFingerprint.Compute(request));
        Assert.False(RoutePlanVerifier.TryDetectRouteRedundantCargoRoundTrip(cleanPlan, out var cleanDetail));
        Assert.Equal("", cleanDetail);
    }
}
