using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public sealed class ExtremeCurrentDataSmokeTests {
    private readonly ITestOutputHelper output;

    public ExtremeCurrentDataSmokeTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Current_deployed_data_reports_one_route_warehouse_relief_candidate() {
        if (Environment.GetEnvironmentVariable("EXTREME_CURRENT_DATA") is null) return;
        const string resources = @"D:\Games\iBarter\Resources";
        AutomaticRoutePlanningRequest request = BuildRequest(
            resources,
            RouteOptimizationProfile.For(RouteOptimizationMode.Extreme));
        var taskIndexes = request.Tasks
            .Select((task, index) => (task, index))
            .ToDictionary(
                pair => pair.task.RowId,
                pair => pair.index,
                StringComparer.Ordinal);
        int finish = taskIndexes[
            "br-8b66ace4b9af45f5832733ba4e768ad8"];
        int oben = taskIndexes[
            "br-08d8cb0d59e64c8c9c26b8cfe00f33a0"];
        int tigris = taskIndexes[
            "br-c4144cce4ece40de89f493fc22f72e0a"];
        int almai = taskIndexes[
            "br-f40fb8b3dd644100b12e4e315833de7c"];

        RouteSimulationState state = RouteSimulationState.CreateInitial(request);
        state = Apply(RouteStateTransition.TryPickup(
            request, state, "Velia", [new("800205", 5)]));
        state = Apply(RouteStateTransition.TryBarter(request, state, finish));
        state = Apply(RouteStateTransition.TryUnload(
            request,
            state,
            "Iliya",
            [new("800242", 5)],
            finishRoute: false));
        state = Apply(RouteStateTransition.TryPickup(
            request,
            state,
            "Iliya",
            [
                new("800031", 10),
                new("800038", 10),
                new("800054", 5),
            ]));
        state = Assert.IsType<RouteSimulationState>(
            FindShortestOneRouteCompletion(
                request,
                state,
                [oben, tigris, almai]));
        RoutePlan candidate = RoutePlanFactory.FromState(
            request,
            state,
            RoutePlanStatus.BestKnownWithinLimit,
            []);
        Assert.Single(candidate.Routes);
        Assert.True(RoutePlanVerifier.Verify(request, candidate).Success);

        string fingerprint = RoutePlanFingerprint.Compute(request);
        RoutePlanLoadResult load = RoutePlanPersistence.TryLoad(
            Path.Combine(resources, "automatic-route-plan.json"), fingerprint);
        Assert.Equal(RoutePlanLoadStatus.Loaded, load.Status);
        RoutePlan original = Assert.IsType<RoutePlan>(load.Snapshot?.Plan);
        output.WriteLine(
            $"[CURRENT-ONE-ROUTE] routes={original.Routes.Count}->" +
            $"{candidate.Routes.Count} totalDistance=" +
            $"{original.Objective?.TotalDistance:F1}->" +
            $"{candidate.Objective?.TotalDistance:F1} " +
            $"peak={candidate.Routes[0].PeakLT}");
    }

    [Fact]
    public void Publication_current_deployed_plan_finishes_iliya_terminal_barter_on_first_visit() {
        if (Environment.GetEnvironmentVariable("EXTREME_CURRENT_DATA") is null) return;
        const string resources = @"D:\Games\iBarter\Resources";
        AutomaticRoutePlanningRequest request = BuildRequest(
            resources,
            RouteOptimizationProfile.For(RouteOptimizationMode.Extreme));
        string fingerprint = RoutePlanFingerprint.Compute(request);
        RoutePlanLoadResult load = RoutePlanPersistence.TryLoad(
            Path.Combine(resources, "automatic-route-plan.json"), fingerprint);
        Assert.Equal(RoutePlanLoadStatus.Loaded, load.Status);
        RoutePlan original = Assert.IsType<RoutePlan>(load.Snapshot?.Plan);

        RoutePlanPublicationResult publication =
            RoutePlanPublication.PreparePlanForPublication(
                request,
                original,
                RoutePlanPublicationSource.FreshGeneration);

        Assert.True(
            publication.Success,
            publication.Failure is null
                ? "publication failed without diagnostic"
                : $"{publication.Failure.Code}: {publication.Failure.Detail}");
        RoutePlan optimized = Assert.IsType<RoutePlan>(publication.Plan);
        PlannedRoute route = Assert.Single(
            optimized.Routes,
            candidate => candidate.Steps
                .OfType<BarterStep>()
                .Any(step => step.Produced.ItemId == "800242"));
        int barterIndex = route.Steps
            .Select((step, index) => (step, index))
            .Single(pair => pair.step is BarterStep barter
                && barter.Produced.ItemId == "800242")
            .index;
        int iliyaPickupIndex = route.Steps
            .Select((step, index) => (step, index))
            .Single(pair => pair.step is WarehousePickupStep pickup
                && pickup.WarehouseId == "Iliya")
            .index;
        Assert.Equal(barterIndex + 2, iliyaPickupIndex);
        WarehouseUnloadStep immediateUnload =
            Assert.IsType<WarehouseUnloadStep>(route.Steps[barterIndex + 1]);
        Assert.Contains(
            immediateUnload.Items,
            item => item.ItemId == "800242" && item.Quantity == 5);
        Assert.True(
            optimized.Objective?.TotalDistance
                <= original.Objective?.TotalDistance);
        PlannedRoute originalRoute = Assert.Single(
            original.Routes,
            candidate => candidate.Steps
                .OfType<BarterStep>()
                .Any(step => step.Produced.ItemId == "800242"));
        Assert.True(route.PeakLT < originalRoute.PeakLT);
        output.WriteLine(
            $"[CURRENT-PUBLICATION] routes={original.Routes.Count}->{optimized.Routes.Count} " +
            $"totalDistance={original.Objective?.TotalDistance:F1}->" +
            $"{optimized.Objective?.TotalDistance:F1} route={route.Number} " +
            $"peak={originalRoute.PeakLT}->{route.PeakLT} " +
            $"distance={originalRoute.Distance:F1}->{route.Distance:F1}");
        foreach (PlannedRoute publishedRoute in optimized.Routes) {
            output.WriteLine(
                $"[CURRENT-PUBLICATION-ROUTE] route={publishedRoute.Number} " +
                $"distance={publishedRoute.Distance:F1} peak={publishedRoute.PeakLT} :: " +
                string.Join(" -> ", publishedRoute.Steps.Select(DescribeStep)));
        }
    }

    [Fact]
    public void Completion_progress_current_deployed_plan_starts_with_iliya_telescope_unload() {
        if (Environment.GetEnvironmentVariable("EXTREME_CURRENT_DATA") is null) return;
        const string resources = @"D:\Games\iBarter\Resources";
        var profile = RouteOptimizationProfile.For(RouteOptimizationMode.Extreme);
        AutomaticRoutePlanningRequest publishedRequest = BuildRequest(
            resources,
            profile,
            "myPlan_Data.json.bak");
        string fingerprint = RoutePlanFingerprint.Compute(publishedRequest);
        RoutePlanLoadResult load = RoutePlanPersistence.TryLoad(
            Path.Combine(resources, "automatic-route-plan.json"), fingerprint);
        Assert.Equal(RoutePlanLoadStatus.Loaded, load.Status);
        RoutePlan plan = Assert.IsType<RoutePlan>(load.Snapshot?.Plan);
        AutomaticRoutePlanningRequest liveRequest = BuildRequest(
            resources,
            profile,
            "myPlan_Data.json");
        AutomaticRoutePlanningRequest progressRequest =
            RouteProgressRequestBuilder.MergePublishedExecutionState(
                publishedRequest,
                liveRequest);
        const string completedRow =
            "br-8b66ace4b9af45f5832733ba4e768ad8";

        RoutePlanPublicationResult prepared =
            RoutePlanPublication.PreparePlanForPublication(
                progressRequest,
                plan,
                RoutePlanPublicationSource.CompletionProgress,
                new HashSet<string>(StringComparer.Ordinal) {
                    completedRow,
                });

        Assert.True(
            prepared.Success,
            prepared.Failure is null
                ? "publication failed without diagnostic"
                : $"{prepared.Failure.Code}: {prepared.Failure.Detail}");
        PlannedRoute firstRoute = prepared.Plan!.Routes[0];
        WarehouseUnloadStep first =
            Assert.IsType<WarehouseUnloadStep>(firstRoute.Steps[0]);
        Assert.Equal("Iliya", first.WarehouseId);
        Assert.Contains(
            first.Items,
            item => item.ItemId == "800242" && item.Quantity == 5);
        Assert.Contains(
            firstRoute.Steps,
            step => step is BarterStep barter
                && barter.RowId != completedRow);
        output.WriteLine(
            "[CURRENT-PROGRESS] " +
            string.Join(" -> ", firstRoute.Steps.Select(DescribeStep)));
    }

    [Fact]
    public void Completion_progress_current_deployed_plan_preserves_later_warehouse_handoffs() {
        if (Environment.GetEnvironmentVariable("EXTREME_CURRENT_DATA") is null) return;
        const string resources = @"D:\Games\iBarter\Resources";
        var profile = RouteOptimizationProfile.For(RouteOptimizationMode.Extreme);
        using JsonDocument savedPlan = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(resources, "automatic-route-plan.json")));
        var plannedRowIds = savedPlan.RootElement
            .GetProperty("Routes")
            .EnumerateArray()
            .SelectMany(route => route.GetProperty("Steps").EnumerateArray())
            .Where(step => StringComparer.Ordinal.Equals(
                step.GetProperty("Kind").GetString(), "barter"))
            .Select(step => step.GetProperty("RowId").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        AutomaticRoutePlanningRequest publishedRequest = BuildRequest(
            resources,
            profile,
            forceIncompleteRowIds: plannedRowIds);
        string fingerprint = RoutePlanFingerprint.Compute(publishedRequest);
        RoutePlanLoadResult load = RoutePlanPersistence.TryLoad(
            Path.Combine(resources, "automatic-route-plan.json"), fingerprint);
        Assert.Equal(RoutePlanLoadStatus.Loaded, load.Status);
        RoutePlan plan = Assert.IsType<RoutePlan>(load.Snapshot?.Plan);
        AutomaticRoutePlanningRequest liveRequest = BuildRequest(resources, profile);
        AutomaticRoutePlanningRequest progressRequest =
            RouteProgressRequestBuilder.MergePublishedExecutionState(
                publishedRequest,
                liveRequest);
        HashSet<string> completedRows = plannedRowIds
            .Except(liveRequest.Tasks.Select(task => task.RowId), StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        RoutePlanPublicationResult prepared =
            RoutePlanPublication.PreparePlanForPublication(
                progressRequest,
                plan,
                RoutePlanPublicationSource.CompletionProgress,
                completedRows);

        Assert.True(
            prepared.Success,
            prepared.Failure is null
                ? "publication failed without diagnostic"
                : $"{prepared.Failure.Code}: {prepared.Failure.Detail}; changes=" +
                  string.Join("|", prepared.Changes.Select(change =>
                      $"r{change.RouteNumber}:{change.ItemId}:" +
                      $"{change.PickedQuantity}->{change.RequiredQuantity}")));
    }

    [Fact]
    public void Smoke_current_deployed_data_with_bounded_cp_sat_worker() {
        if (Environment.GetEnvironmentVariable("EXTREME_CURRENT_DATA") is null) return;
        const string resources = @"D:\Games\iBarter\Resources";
        var profile = RouteOptimizationProfile.For(RouteOptimizationMode.Extreme);
        AutomaticRoutePlanningRequest request = BuildRequest(resources, profile);
        string fingerprint = RoutePlanFingerprint.Compute(request);
        RoutePlanLoadResult load = RoutePlanPersistence.TryLoad(
            Path.Combine(resources, "automatic-route-plan.json"), fingerprint);
        RoutePlan? seed = load.Snapshot?.Plan;
        if (Environment.GetEnvironmentVariable("EXTREME_REFRESH_SEED") is not null) {
            var deep = RouteOptimizationProfile.For(RouteOptimizationMode.Deep) with {
                TotalTarget = TimeSpan.FromSeconds(30),
                FinalizationReserve = TimeSpan.FromSeconds(2),
            };
            seed = new AutomaticRoutePlanner().Plan(request, deep, TestContext.Current.CancellationToken);
        }
        string solver = ExtremeRouteSolverTestsPath();
        int seconds = int.TryParse(Environment.GetEnvironmentVariable("EXTREME_SMOKE_SECONDS"), out int configured)
            ? Math.Max(1, configured)
            : 60;

        var watch = Stopwatch.StartNew();
        ExtremeRouteSolverRunResult result = ExtremeRouteSolverClient.Solve(
            request, seed, TimeSpan.FromSeconds(seconds),
            ExtremeRouteResourcePolicy.Detect(),
            TestContext.Current.CancellationToken, solver);
        watch.Stop();

        output.WriteLine(
            $"[EXTREME-SMOKE] tasks={request.Tasks.Count} seed={load.Status}/{seed?.Routes.Count}/" +
            $"{seed?.Objective?.TotalDistance:F1} " +
            $"routeLimit={result.RouteLimit} status={result.SolverStatus} " +
            $"elapsed={watch.Elapsed.TotalSeconds:F1}s distance={result.Plan?.Objective?.TotalDistance:F1} " +
            $"bound={result.BestBound:F1} gap={result.RelativeGap:P2} " +
            $"workers={result.WorkerCount} memory={result.MemoryLimitMb}MB " +
            $"attempts={result.AttemptCount} failure={result.Failure}");
        Assert.InRange(request.Tasks.Count, 1, ExtremeRouteSolverProtocol.MaximumTasks);
        Assert.NotNull(result.Plan);
        Assert.True(RoutePlanVerifier.Verify(request, result.Plan!).Success);
        if (seed?.Objective is { } seedObjective) {
            Assert.True(result.Plan!.Objective?.TotalDistance <= seedObjective.TotalDistance,
                $"CP-SAT distance {result.Plan.Objective?.TotalDistance:F1} must not exceed seed {seedObjective.TotalDistance:F1}.");
        }
    }

    private static AutomaticRoutePlanningRequest BuildRequest(
        string resources,
        RouteOptimizationProfile profile,
        string plannerFileName = "myPlan_Data.json",
        IReadOnlySet<string>? forceIncompleteRowIds = null) {
        using JsonDocument plan = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(resources, plannerFileName)));
        var plannerRows = plan.RootElement.EnumerateArray().Select(row => {
            JsonElement item1 = row.GetProperty("Item1");
            JsonElement item2 = row.GetProperty("Item2");
            return new PlannerRouteSnapshot(
                row.GetProperty("PlannerRowId").GetString()!,
                row.GetProperty("ExchangeDone").GetBoolean()
                    && !(forceIncompleteRowIds?.Contains(
                        row.GetProperty("PlannerRowId").GetString()!) ?? false),
                row.GetProperty("ExchangeQuantity").GetInt32(),
                row.GetProperty("IsLandName").GetString()!,
                item1.GetProperty("ItemID").GetString()!,
                item1.GetProperty("ItemNameDisplay").GetString()!,
                int.Parse(item1.GetProperty("ItemLV").GetString()!, CultureInfo.InvariantCulture),
                row.GetProperty("Item1Number").GetInt32(),
                item2.GetProperty("ItemID").GetString()!,
                item2.GetProperty("ItemNameDisplay").GetString()!,
                int.Parse(item2.GetProperty("ItemLV").GetString()!, CultureInfo.InvariantCulture),
                row.GetProperty("Item2Number").GetInt32());
        }).ToArray();

        using JsonDocument storage = JsonDocument.Parse(File.ReadAllText(Path.Combine(resources, "myStorage_Data.json")));
        var storageRows = storage.RootElement.EnumerateArray().Select(item => new StorageItemSnapshot(
            item.GetProperty("ItemID").GetString()!,
            int.Parse(item.GetProperty("ItemLV").GetString()!, CultureInfo.InvariantCulture),
            item.GetProperty("StorageVeliaQuantity_Velia").GetInt32(),
            item.GetProperty("StorageVeliaQuantity_Iliya").GetInt32(),
            item.GetProperty("StorageVeliaQuantity_Epheria").GetInt32(),
            item.GetProperty("StorageVeliaQuantity_Ancado").GetInt32())).ToArray();

        var islandPoints = File.ReadLines(Path.Combine(resources, "Islands.csv"))
            .Select(line => line.Split(','))
            .Where(parts => parts.Length >= 8)
            .ToDictionary(
                parts => parts[0],
                parts => new RoutePoint(
                    double.Parse(parts[6], CultureInfo.InvariantCulture),
                    double.Parse(parts[7], CultureInfo.InvariantCulture)),
                StringComparer.Ordinal);
        foreach (string line in File.ReadLines(
                     Path.Combine(resources, "IslandBarterLocations.csv")).Skip(1)) {
            string[] parts = line.Split(',');
            if (parts.Length < 3 || !islandPoints.ContainsKey(parts[0]))
                continue;
            islandPoints[parts[0]] = new RoutePoint(
                double.Parse(parts[1], CultureInfo.InvariantCulture),
                double.Parse(parts[2], CultureInfo.InvariantCulture));
        }
        IslandRouteSnapshot[] islands = islandPoints
            .Select(pair => new IslandRouteSnapshot(pair.Key, pair.Value))
            .ToArray();

        using JsonDocument cargo = JsonDocument.Parse(File.ReadAllText(Path.Combine(resources, "myShipProperty_Data.json")));
        var capacity = new CargoCapacitySnapshot(
            (int)Math.Round(cargo.RootElement.GetProperty("ExtraLT").GetDouble(), MidpointRounding.AwayFromZero),
            (int)Math.Round(cargo.RootElement.GetProperty("TotalLT").GetDouble(), MidpointRounding.AwayFromZero));
        return AutomaticRoutePlanningAdapter.BuildRequest(
            plannerRows, storageRows, islands, capacity,
            new RouteSearchLimits(250_000, profile.MaxLocalEvaluations), profile);
    }

    private static string ExtremeRouteSolverTestsPath() {
        string? configured = Environment.GetEnvironmentVariable("EXTREME_TEST_SOLVER_PATH");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        DirectoryInfo directory = new(AppContext.BaseDirectory);
        for (int i = 0; i < 5; i++) directory = directory.Parent!;
        return Path.Combine(directory.FullName, "Tools", "ExtremeRouteSolver", "bin", "Release",
            "net10.0", "win-x64", "iBarter.ExtremeRouteSolver.exe");
    }

    private static RouteSimulationState Apply(RouteTransitionResult transition) {
        Assert.True(
            transition.Success,
            transition.Diagnostic is null
                ? "transition failed without diagnostic"
                : $"{transition.Diagnostic.Code}: {transition.Diagnostic.Detail}");
        return transition.State;
    }

    private static string DescribeStep(RouteStep step) => step switch {
        WarehousePickupStep pickup =>
            $"pickup@{pickup.IslandId}[" +
            string.Join(",", pickup.Items.Select(item =>
                $"{item.ItemId}x{item.Quantity}")) +
            $"] load={pickup.Load.CargoLT}",
        BarterStep barter =>
            $"barter@{barter.IslandId}[" +
            $"{barter.Consumed.ItemId}x{barter.Consumed.Quantity}>" +
            $"{barter.Produced.ItemId}x{barter.Produced.Quantity}] " +
            $"load={barter.Load.CargoLT}",
        WarehouseUnloadStep unload =>
            $"unload@{unload.IslandId}[" +
            string.Join(",", unload.Items.Select(item =>
                $"{item.ItemId}x{item.Quantity}")) +
            $"] load={unload.Load.CargoLT}",
        _ => step.GetType().Name,
    };

    private static RouteSimulationState? FindShortestOneRouteCompletion(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        IReadOnlyList<int> remainingTaskIndexes) {
        if (remainingTaskIndexes.Count == 0) {
            RouteTransitionResult finalUnload =
                RouteStateTransition.TryUnload(request, state, "Iliya");
            return finalUnload.Success ? finalUnload.State : null;
        }

        RouteSimulationState? best = null;
        for (int position = 0; position < remainingTaskIndexes.Count; position++) {
            int taskIndex = remainingTaskIndexes[position];
            RouteTransitionResult barter =
                RouteStateTransition.TryBarter(request, state, taskIndex);
            if (!barter.Success)
                continue;
            int[] nextIndexes = remainingTaskIndexes
                .Where((_, index) => index != position)
                .ToArray();
            Consider(FindShortestOneRouteCompletion(
                request, barter.State, nextIndexes));

            if (nextIndexes.Length == 0)
                continue;
            RouteItemQuantity[] terminalOutputs = barter.State.OnBoard
                .Where(pair => pair.Value > 0
                    && request.Items[pair.Key].UnitWeight > 0
                    && !request.Tasks.Any(task =>
                        StringComparer.Ordinal.Equals(
                            task.Item1Id, pair.Key)))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new RouteItemQuantity(
                    pair.Key, pair.Value))
                .ToArray();
            if (terminalOutputs.Length == 0)
                continue;
            RouteTransitionResult relief = RouteStateTransition.TryUnload(
                request,
                barter.State,
                "Iliya",
                terminalOutputs,
                finishRoute: false);
            if (relief.Success) {
                Consider(FindShortestOneRouteCompletion(
                    request, relief.State, nextIndexes));
            }
        }
        return best;

        void Consider(RouteSimulationState? candidate) {
            if (candidate is null)
                return;
            if (best is null
                || candidate.TotalDistance < best.TotalDistance) {
                best = candidate;
            }
        }
    }
}
