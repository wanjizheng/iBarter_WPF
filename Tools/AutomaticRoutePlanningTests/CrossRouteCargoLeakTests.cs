using System.Text.Json;
using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public sealed class CrossRouteCargoLeakTests {
    [Fact]
    public void Fixture_Route1NormalizationSucceedsAndRemovesOnlyRedundantItem() {
        var (request, badPlan) = LoadFixture();

        var result = RouteCargoNormalizer.Normalize(request, badPlan);

        Assert.True(result.Success, result.Failure?.Detail);
        Assert.True(result.Changed);
        var route1 = result.Plan!.Routes.Single(route => route.Number == 1);
        Assert.DoesNotContain(route1.Steps.OfType<WarehousePickupStep>()
            .SelectMany(step => step.Items), item => item.ItemId == "I_A");
        Assert.DoesNotContain(route1.Steps.OfType<WarehouseUnloadStep>()
            .SelectMany(step => step.Items), item => item.ItemId == "I_A");
        Assert.Equal(7, route1.Steps.OfType<BarterStep>().Count());
    }

    [Fact]
    public void Fixture_Route1LoadDropsFrom13000To8000And15411To10411() {
        var (request, badPlan) = LoadFixture();
        var originalPickup = Assert.IsType<WarehousePickupStep>(badPlan.Routes[0].Steps[0]);
        Assert.Equal(13_000, originalPickup.Load.CargoLT);
        Assert.Equal(15_411, originalPickup.Load.TotalWithExtraLT);

        var result = RouteCargoNormalizer.Normalize(request, badPlan);
        var fixedPickup = Assert.IsType<WarehousePickupStep>(result.Plan!.Routes[0].Steps[0]);

        Assert.Equal(8_000, fixedPickup.Load.CargoLT);
        Assert.Equal(10_411, fixedPickup.Load.TotalWithExtraLT);
    }

    [Fact]
    public void Fixture_Route3KeepsLegitimateIAChain() {
        var (request, badPlan) = LoadFixture();

        var result = RouteCargoNormalizer.Normalize(request, badPlan);

        var route3 = result.Plan!.Routes.Single(route => route.Number == 3);
        var pickup = route3.Steps.OfType<WarehousePickupStep>().Single();
        Assert.Contains(pickup.Items, item => item.ItemId == "I_A" && item.Quantity == 5);
        var barter = route3.Steps.OfType<BarterStep>().First();
        Assert.Equal(new RouteItemQuantity("I_A", 5), barter.Consumed);
    }

    [Fact]
    public void Fixture_NormalizedPlanPassesFullVerifier() {
        var (request, badPlan) = LoadFixture();
        var normalized = RouteCargoNormalizer.Normalize(request, badPlan);

        var verification = RoutePlanVerifier.Verify(request, normalized.Plan!);

        Assert.True(verification.Success, verification.Diagnostic?.Detail);
    }

    [Fact]
    public void Fixture_EndToEndPublicationReturnsCorrectedCandidate() {
        var (request, badPlan) = LoadFixture();

        var prepared = RoutePlanPublication.PreparePlanForPublication(
            request, badPlan, RoutePlanPublicationSource.FreshGeneration);

        Assert.True(prepared.Success, prepared.Failure?.Detail);
        Assert.NotNull(prepared.Plan);
        Assert.False(RoutePlanVerifier.TryDetectRouteRedundantCargoRoundTrip(
            request, prepared.Plan!, out _));
    }

    [Fact]
    public void CompletionProgressUsesSamePreparationContractAndFullVerifier() {
        var (originalRequest, badPlan) = LoadFixture();
        var remainingRequest = new AutomaticRoutePlanningRequest(
            originalRequest.Tasks.Where(task => task.RowId != "task-1").ToArray(),
            originalRequest.Items,
            originalRequest.Warehouses,
            originalRequest.ExtraLT,
            originalRequest.TotalLT,
            originalRequest.Limits,
            originalRequest.ConfigurationVersion,
            new Dictionary<string, int>(StringComparer.Ordinal) { ["I_E"] = 5 });

        var prepared = RoutePlanPublication.PreparePlanForPublication(
            remainingRequest,
            badPlan,
            RoutePlanPublicationSource.CompletionProgress,
            new HashSet<string>(StringComparer.Ordinal) { "task-1" });

        Assert.True(prepared.Success, prepared.Failure?.Detail);
        Assert.DoesNotContain(prepared.Plan!.Routes.SelectMany(route => route.Steps)
            .OfType<BarterStep>(), step => step.RowId == "task-1");
        Assert.True(RoutePlanVerifier.Verify(remainingRequest, prepared.Plan).Success);
    }

    [Fact]
    public void CompletionProgressCarriesCompletedPrefixOutputIntoNewFirstBarter() {
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["800046"] = new("800046", "input", 1, 1_000),
            ["800064"] = new("800064", "handoff", 2, 1_000),
            ["800224"] = new("800224", "output", 3, 1_000),
        };
        var request = new AutomaticRoutePlanningRequest(
            [new RouteBarterTask(
                "next", "Dallae", new RoutePoint(2, 0),
                "800064", 5, "800224", 5)],
            items,
            [new RouteWarehouse(
                "Iliya", "Iliya", new RoutePoint(0, 0),
                new Dictionary<string, int>(StringComparer.Ordinal) {
                    ["800046"] = 5,
                })],
            2_411, 24_110, new RouteSearchLimits(100, 10), "progress-handoff");
        var candidate = new RoutePlan(
            RoutePlanStatus.Optimal,
            [new PlannedRoute(1, "Iliya", "Iliya", [
                new WarehousePickupStep("Iliya", "Iliya",
                    [new RouteItemQuantity("800046", 5)], new(0, 0, 0)),
                new BarterStep("done", "Pilava",
                    new("800046", 5), new("800064", 5), new(0, 0, 0)),
                new BarterStep("next", "Dallae",
                    new("800064", 5), new("800224", 5), new(0, 0, 0)),
                new WarehouseUnloadStep("Iliya", "Iliya",
                    [new RouteItemQuantity("800224", 5)], new(0, 0, 0)),
            ], 0, 0, 0, 0)],
            null, [], "old-plan-fingerprint");

        var prepared = RoutePlanPublication.PreparePlanForPublication(
            request,
            candidate,
            RoutePlanPublicationSource.CompletionProgress,
            new HashSet<string>(StringComparer.Ordinal) { "done" });

        Assert.True(prepared.Success, prepared.Failure?.Detail);
        var route = Assert.Single(prepared.Plan!.Routes);
        Assert.Collection(route.Steps,
            step => Assert.Equal("next", Assert.IsType<BarterStep>(step).RowId),
            step => Assert.IsType<WarehouseUnloadStep>(step));
        Assert.Equal(7_411, route.Steps[0].Load.TotalWithExtraLT);
    }

    [Fact]
    public void ProgressProjectionRetainsFullBaselineSoUncheckedTaskCanReturn() {
        var fullRequest = RouteTestData.TwoItemRequest(reverseDictionaryOrder: false);
        var fullPlan = new AutomaticRoutePlanner().Plan(fullRequest, CancellationToken.None);
        Assert.Equal(2, fullPlan.Routes.SelectMany(route => route.Steps)
            .OfType<BarterStep>().Count());

        var remainingRequest = new AutomaticRoutePlanningRequest(
            fullRequest.Tasks.Where(task => task.RowId != "r1").ToArray(),
            fullRequest.Items,
            fullRequest.Warehouses,
            fullRequest.ExtraLT,
            fullRequest.TotalLT,
            fullRequest.Limits,
            fullRequest.ConfigurationVersion);
        var progressed = RoutePlanPublication.PreparePlanForPublication(
            remainingRequest,
            fullPlan,
            RoutePlanPublicationSource.ProgressRestore,
            new HashSet<string>(["r1"], StringComparer.Ordinal));

        Assert.True(progressed.Success, progressed.Failure?.Detail);
        Assert.Single(progressed.Plan!.Routes.SelectMany(route => route.Steps)
            .OfType<BarterStep>());
        Assert.Same(fullPlan, progressed.RetainedPlan);

        // Cancelling CK rebuilds the full request. It succeeds only when the
        // retained baseline still contains r1; using progressed.Plan here
        // reproduces verification-mismatch/incomplete.
        var restored = RoutePlanPublication.PreparePlanForPublication(
            fullRequest,
            progressed.RetainedPlan!,
            RoutePlanPublicationSource.CompletionProgress,
            new HashSet<string>(StringComparer.Ordinal));
        Assert.True(restored.Success, restored.Failure?.Detail);
        Assert.Equal(2, restored.Plan!.Routes.SelectMany(route => route.Steps)
            .OfType<BarterStep>().Count());

        var truncated = RoutePlanPublication.PreparePlanForPublication(
            fullRequest,
            progressed.Plan,
            RoutePlanPublicationSource.CompletionProgress,
            new HashSet<string>(StringComparer.Ordinal));
        Assert.False(truncated.Success);
        Assert.Equal("verification-mismatch", truncated.Failure?.Code);
        Assert.Equal("incomplete", truncated.Failure?.Detail);
    }

    [Fact]
    public void ReplayFailureReportsExactTransitionContextInsteadOfNull() {
        var (request, badPlan) = LoadFixture();
        var route = badPlan.Routes[0];
        var pickup = (WarehousePickupStep)route.Steps[0];
        var impossible = new WarehousePickupStep(
            pickup.WarehouseId,
            pickup.IslandId,
            [new RouteItemQuantity("I_A", 99)],
            pickup.Load);
        var tampered = new PlannedRoute(
            route.Number, route.StartWarehouseId, route.EndWarehouseId,
            [impossible, .. route.Steps.Skip(1)], route.Distance,
            route.InitialLT, route.CurrentLT, route.PeakLT);

        var replay = RouteReplay.ReplayRoute(request, tampered);

        Assert.False(replay.Success);
        Assert.Null(replay.Route);
        Assert.Equal("insufficient-stock", replay.FailureCode);
        Assert.Equal(0, replay.FailureStepIndex);
        Assert.Equal(nameof(WarehousePickupStep), replay.FailureStepKind);
        Assert.Equal("W_A", replay.FailedWarehouse);
        Assert.Equal("I_A", replay.RequestedItemId);
        Assert.Equal(7, replay.WarehouseStockQty);
        Assert.Empty(replay.OnBoardAtFailure!);
        Assert.Equal(100_000, replay.TotalLT);
        Assert.Equal(2_411, replay.ExtraLT);
    }

    [Fact]
    public void NormalizationFailureReturnsNoFallbackPlan() {
        var (request, badPlan) = LoadFixture();
        var route = badPlan.Routes[0];
        var pickup = (WarehousePickupStep)route.Steps[0];
        var impossible = new WarehousePickupStep(
            pickup.WarehouseId,
            pickup.IslandId,
            [new RouteItemQuantity("I_A", 99), new RouteItemQuantity("I_B", 1)],
            pickup.Load);
        var tamperedRoute = new PlannedRoute(
            route.Number, route.StartWarehouseId, route.EndWarehouseId,
            [impossible, .. route.Steps.Skip(1)], route.Distance,
            route.InitialLT, route.CurrentLT, route.PeakLT);
        var tamperedPlan = new RoutePlan(
            badPlan.Status, [tamperedRoute, .. badPlan.Routes.Skip(1)],
            badPlan.Objective, badPlan.Diagnostics, badPlan.InputFingerprint);

        var result = RouteCargoNormalizer.Normalize(request, tamperedPlan);

        Assert.False(result.Success);
        Assert.Null(result.Plan);
        Assert.True(result.Changed);
        Assert.Equal("missing-input", result.Failure!.Code);
        Assert.Equal(1, result.Failure.RouteNumber);
        Assert.Equal(1, result.Failure.StepIndex);
    }

    [Fact]
    public void PartialPickupFiveConsumeThreeReturnTwoIsDetectedAndRepaired() {
        var (request, _) = BuildPartialFixture(produceSameItem: false);
        var badPlan = BuildPartialPlan(request, produceSameItem: false);

        Assert.True(RoutePlanVerifier.TryDetectRouteRedundantCargoRoundTrip(
            request, badPlan, out string detail));
        Assert.Contains("picked=5", detail);
        Assert.Contains("required=3", detail);
        Assert.Contains("redundant=2", detail);
        Assert.Contains("returned=2", detail);

        var normalized = RouteCargoNormalizer.Normalize(request, badPlan);
        Assert.True(normalized.Success, normalized.Failure?.Detail);
        var pickup = normalized.Plan!.Routes[0].Steps.OfType<WarehousePickupStep>().Single();
        Assert.Equal(3, pickup.Items.Single(item => item.ItemId == "X").Quantity);
        Assert.DoesNotContain(normalized.Plan.Routes[0].Steps.OfType<WarehouseUnloadStep>()
            .SelectMany(step => step.Items), item => item.ItemId == "X");
    }

    [Fact]
    public void ProducedItemDoesNotHideRedundantPickupProvenance() {
        var (request, _) = BuildPartialFixture(produceSameItem: true);
        var badPlan = BuildPartialPlan(request, produceSameItem: true);

        Assert.True(RoutePlanVerifier.TryDetectRouteRedundantCargoRoundTrip(
            request, badPlan, out string detail));
        Assert.Contains("redundant=5", detail);

        var normalized = RouteCargoNormalizer.Normalize(request, badPlan);
        Assert.True(normalized.Success, normalized.Failure?.Detail);
        var route = normalized.Plan!.Routes[0];
        Assert.DoesNotContain(route.Steps.OfType<WarehousePickupStep>()
            .SelectMany(step => step.Items), item => item.ItemId == "X");
        Assert.Contains(route.Steps.OfType<WarehouseUnloadStep>()
            .SelectMany(step => step.Items), item => item.ItemId == "X" && item.Quantity == 5);
    }

    [Fact]
    public void PersistedBadSchemaV2PlanIsPreparedAndSavedCorrected() {
        var (request, badPlan) = LoadFixture();
        string directory = Path.Combine(Path.GetTempPath(), $"ibarter-restore-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "automatic-route-plan.json");
        try {
            RoutePlanPersistence.Save(path, badPlan, 1, false);
            var loaded = RoutePlanPersistence.TryLoad(path, badPlan.InputFingerprint);
            Assert.Equal(RoutePlanLoadStatus.Loaded, loaded.Status);

            var prepared = RoutePlanPublication.PreparePlanForPublication(
                request, loaded.Snapshot!.Plan, RoutePlanPublicationSource.PersistedRestore);
            Assert.True(prepared.Success, prepared.Failure?.Detail);
            RoutePlanPersistence.Save(path, prepared.Plan!, 1, false);

            var final = RoutePlanPersistence.TryLoad(path, badPlan.InputFingerprint);
            var route1 = final.Snapshot!.Plan.Routes.Single(route => route.Number == 1);
            Assert.DoesNotContain(route1.Steps.OfType<WarehousePickupStep>()
                .SelectMany(step => step.Items), item => item.ItemId == "I_A");
            var route3 = final.Snapshot.Plan.Routes.Single(route => route.Number == 3);
            Assert.Contains(route3.Steps.OfType<WarehousePickupStep>()
                .SelectMany(step => step.Items),
                item => item.ItemId == "I_A" && item.Quantity == 5);
            Assert.True(File.Exists(path + ".bak"));
            // A later UI snapshot save must atomically rotate an existing
            // backup too, rather than failing because .bak already exists.
            RoutePlanPersistence.Save(path, final.Snapshot.Plan, 1, false);
            Assert.Equal(RoutePlanLoadStatus.Loaded,
                RoutePlanPersistence.TryLoad(path, badPlan.InputFingerprint).Status);
        }
        finally {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PersistedNormalizationFailureIsRejectedWithoutOverwritingFile() {
        var (request, badPlan) = LoadFixture();
        var route = badPlan.Routes[0];
        var pickup = (WarehousePickupStep)route.Steps[0];
        var impossible = new WarehousePickupStep(
            pickup.WarehouseId, pickup.IslandId,
            [new RouteItemQuantity("I_A", 99), new RouteItemQuantity("I_B", 1)],
            pickup.Load);
        var impossiblePlan = new RoutePlan(
            badPlan.Status,
            [new PlannedRoute(1, route.StartWarehouseId, route.EndWarehouseId,
                [impossible, .. route.Steps.Skip(1)], route.Distance,
                route.InitialLT, route.CurrentLT, route.PeakLT), .. badPlan.Routes.Skip(1)],
            badPlan.Objective, badPlan.Diagnostics, badPlan.InputFingerprint);
        string directory = Path.Combine(Path.GetTempPath(), $"ibarter-refuse-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "automatic-route-plan.json");
        try {
            RoutePlanPersistence.Save(path, impossiblePlan, 1, false);
            string before = File.ReadAllText(path);

            var prepared = RoutePlanPublication.PreparePlanForPublication(
                request, impossiblePlan, RoutePlanPublicationSource.PersistedRestore);

            Assert.False(prepared.Success);
            Assert.Null(prepared.Plan);
            Assert.Equal(before, File.ReadAllText(path));
        }
        finally {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    internal static (AutomaticRoutePlanningRequest Request, RoutePlan Plan) LoadFixture() {
        string path = Path.Combine(
            AppContext.BaseDirectory, "TestData", "cross-route-cargo-leak-plan.json");
        var fixture = JsonSerializer.Deserialize<FixtureRoot>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var items = fixture.Items.ToDictionary(
            pair => pair.Key,
            pair => new RouteItem(pair.Key, pair.Key, pair.Value.Level, pair.Value.UnitWeight),
            StringComparer.Ordinal);
        var warehouses = fixture.Warehouses.Select(warehouse => new RouteWarehouse(
            warehouse.Id, warehouse.Id, new RoutePoint(warehouse.X, warehouse.Y),
            warehouse.Stock)).ToArray();
        var tasks = fixture.Tasks.Select(task => new RouteBarterTask(
            task.RowId, task.IslandId, new RoutePoint(task.X, task.Y),
            task.Item1Id, task.InputQuantity, task.Item2Id, task.OutputQuantity)).ToArray();
        var request = new AutomaticRoutePlanningRequest(
            tasks, items, warehouses, fixture.ExtraLT, fixture.TotalLT,
            new RouteSearchLimits(100_000, 1_000), "fixture-v1");
        string fingerprint = RoutePlanFingerprint.Compute(request);
        var rawPlan = new RoutePlan(
            RoutePlanStatus.Optimal,
            fixture.Plan.Routes.Select(ToRoute).ToArray(),
            null, [], fingerprint);
        var replay = RouteReplay.ReplayPlan(request, rawPlan);
        Assert.True(replay.Success, RouteReplay.FormatFailureDetail(replay.Failure));
        return (request, replay.Plan!);
    }

    private static PlannedRoute ToRoute(FixtureRoute route) => new(
        route.Number, route.StartWarehouseId, route.EndWarehouseId,
        route.Steps.Select(ToStep).ToArray(), route.Distance,
        route.InitialLT, route.CurrentLT, route.PeakLT);

    private static RouteStep ToStep(FixtureStep step) => step.Kind switch {
        "pickup" => new WarehousePickupStep(
            step.WarehouseId!, step.IslandId, step.Items, step.Load),
        "barter" => new BarterStep(
            step.RowId!, step.IslandId, step.Consumed!, step.Produced!, step.Load),
        "unload" => new WarehouseUnloadStep(
            step.WarehouseId!, step.IslandId, step.Items, step.Load),
        _ => throw new InvalidDataException(step.Kind),
    };

    private static (AutomaticRoutePlanningRequest Request, RoutePlan Plan) BuildPartialFixture(
        bool produceSameItem) {
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["X"] = new("X", "X", 1, 100),
            ["Y"] = new("Y", "Y", 1, 100),
            ["Z"] = new("Z", "Z", 1, 100),
        };
        var task = produceSameItem
            ? new RouteBarterTask("make-x", "Island", new RoutePoint(1, 0), "Y", 1, "X", 5)
            : new RouteBarterTask("use-x", "Island", new RoutePoint(1, 0), "X", 3, "Z", 3);
        var request = new AutomaticRoutePlanningRequest(
            [task], items,
            [new RouteWarehouse("W", "W", new RoutePoint(0, 0),
                new Dictionary<string, int> { ["X"] = 5, ["Y"] = 1 })],
            0, 10_000, new RouteSearchLimits(100, 10), "partial");
        return (request, BuildPartialPlan(request, produceSameItem));
    }

    private static RoutePlan BuildPartialPlan(
        AutomaticRoutePlanningRequest request,
        bool produceSameItem) {
        RouteItemQuantity[] pickupItems = produceSameItem
            ? [new("X", 5), new("Y", 1)]
            : [new("X", 5)];
        RouteItemQuantity[] unloadItems = produceSameItem
            ? [new("X", 10)]
            : [new("X", 2), new("Z", 3)];
        var task = request.Tasks[0];
        var raw = new RoutePlan(
            RoutePlanStatus.Optimal,
            [new PlannedRoute(1, "W", "W", [
                new WarehousePickupStep("W", "W", pickupItems, new(0, 0, 0)),
                new BarterStep(task.RowId, task.IslandId,
                    new(task.Item1Id, task.InputQuantity),
                    new(task.Item2Id, task.OutputQuantity), new(0, 0, 0)),
                new WarehouseUnloadStep("W", "W", unloadItems, new(0, 0, 0)),
            ], 0, 0, 0, 0)],
            null, [], RoutePlanFingerprint.Compute(request));
        return RouteReplay.ReplayPlan(request, raw).Plan!;
    }

    internal sealed class FixtureRoot {
        public FixturePlan Plan { get; set; } = new();
        public Dictionary<string, FixtureItem> Items { get; set; } = new();
        public List<FixtureWarehouse> Warehouses { get; set; } = [];
        public List<FixtureTask> Tasks { get; set; } = [];
        public int ExtraLT { get; set; }
        public int TotalLT { get; set; }
    }
    internal sealed class FixturePlan { public List<FixtureRoute> Routes { get; set; } = []; }
    internal sealed class FixtureRoute {
        public int Number { get; set; }
        public string StartWarehouseId { get; set; } = "";
        public string EndWarehouseId { get; set; } = "";
        public double Distance { get; set; }
        public int InitialLT { get; set; }
        public int CurrentLT { get; set; }
        public int PeakLT { get; set; }
        public List<FixtureStep> Steps { get; set; } = [];
    }
    internal sealed class FixtureStep {
        public string Kind { get; set; } = "";
        public string IslandId { get; set; } = "";
        public string? WarehouseId { get; set; }
        public string? RowId { get; set; }
        public List<RouteItemQuantity> Items { get; set; } = [];
        public RouteItemQuantity? Consumed { get; set; }
        public RouteItemQuantity? Produced { get; set; }
        public RouteLoadSnapshot Load { get; set; } = new(0, 0, 0);
    }
    internal sealed class FixtureItem { public int Level { get; set; } public int UnitWeight { get; set; } }
    internal sealed class FixtureWarehouse {
        public string Id { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public Dictionary<string, int> Stock { get; set; } = new();
    }
    internal sealed class FixtureTask {
        public string RowId { get; set; } = "";
        public string IslandId { get; set; } = "";
        public string Item1Id { get; set; } = "";
        public int InputQuantity { get; set; }
        public string Item2Id { get; set; } = "";
        public int OutputQuantity { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
    }
}
