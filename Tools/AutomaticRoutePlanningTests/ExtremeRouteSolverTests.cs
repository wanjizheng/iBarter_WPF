using System.Diagnostics;
using System.Text.Json;
using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public sealed class ExtremeRouteSolverTests {
    [Fact]
    public void Continuous_attempt_uses_the_entire_remaining_budget() {
        Assert.Equal(
            TimeSpan.FromMinutes(10),
            ExtremeRouteSolverClient.SelectAttemptDuration(TimeSpan.FromMinutes(10)));
        Assert.Equal(
            TimeSpan.FromMilliseconds(250),
            ExtremeRouteSolverClient.SelectAttemptDuration(TimeSpan.FromMilliseconds(250)));
        Assert.Equal(
            TimeSpan.Zero,
            ExtremeRouteSolverClient.SelectAttemptDuration(TimeSpan.FromMilliseconds(-1)));
    }

    [Fact]
    public async Task Cp_sat_worker_gracefully_stops_and_writes_its_final_result() {
        string solver = SolverPath();
        Assert.True(File.Exists(solver), $"Build the x64 solver first: {solver}");

        string work = Path.Combine(
            Path.GetTempPath(), "iBarter", "ExtremeRouteSolverTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        string inputPath = Path.Combine(work, "input.json");
        string outputPath = Path.Combine(work, "best.json");
        string stopSignalPath = Path.Combine(work, "stop.signal");
        try {
            File.WriteAllText(inputPath, JsonSerializer.Serialize(GracefulStopInput()));
            File.WriteAllText(stopSignalPath, "NoImprovementConverged");

            using var process = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = solver,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            process.StartInfo.ArgumentList.Add(inputPath);
            process.StartInfo.ArgumentList.Add(outputPath);
            process.StartInfo.ArgumentList.Add(stopSignalPath);
            Assert.True(process.Start());

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(timeout.Token);

            string stderr = await process.StandardError.ReadToEndAsync(timeout.Token);
            Assert.True(File.Exists(outputPath),
                $"The graceful stop must preserve a final solver response. exit={process.ExitCode}; stderr={stderr}");
            ExtremeSolverOutputDto output = JsonSerializer.Deserialize<ExtremeSolverOutputDto>(
                File.ReadAllText(outputPath))!;
            Assert.Equal(ExtremeRouteSolverProtocol.Version, output.ProtocolVersion);
            Assert.DoesNotContain(output.Status, new[] { "Error", "ModelInvalid" });
        }
        finally {
            try { Directory.Delete(work, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Cp_sat_worker_returns_a_replay_verified_inventory_and_capacity_safe_plan() {
        string solver = SolverPath();
        Assert.True(File.Exists(solver), $"Build the x64 solver first: {solver}");

        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["raw"] = new("raw", "Raw", 1, 100),
            ["mid"] = new("mid", "Mid", 2, 200),
            ["coin"] = new("coin", "Coin", 0, 0),
        };
        var request = new AutomaticRoutePlanningRequest(
            [
                new RouteBarterTask("producer", "A", new RoutePoint(100, 0), "raw", 2, "mid", 1),
                new RouteBarterTask("consumer", "B", new RoutePoint(200, 0), "mid", 1, "coin", 10),
            ],
            items,
            [new RouteWarehouse("W", "W", new RoutePoint(0, 0),
                new Dictionary<string, int>(StringComparer.Ordinal) { ["raw"] = 2 })],
            extraLT: 0,
            totalLT: 200,
            new RouteSearchLimits(10_000, 100),
            "extreme-test-v1");

        ExtremeRouteSolverRunResult result = ExtremeRouteSolverClient.Solve(
            request, incumbent: null, TimeSpan.FromSeconds(10), new ExtremeRouteResources(2, 1_024),
            TestContext.Current.CancellationToken, solver);

        Assert.Null(result.Failure);
        Assert.NotNull(result.Plan);
        Assert.True(RoutePlanVerifier.Verify(request, result.Plan!).Success);
        Assert.Equal(new[] { "producer", "consumer" },
            result.Plan!.Routes.SelectMany(route => route.Steps).OfType<BarterStep>().Select(step => step.RowId));
        Assert.All(result.Plan.Routes, route => Assert.True(route.PeakLT <= request.TotalLT));
    }

    [Fact]
    public void Cp_sat_worker_splits_routes_when_one_starting_load_would_be_overweight() {
        string solver = SolverPath();
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["raw"] = new("raw", "Raw", 1, 100),
            ["coin"] = new("coin", "Coin", 0, 0),
        };
        var request = new AutomaticRoutePlanningRequest(
            [
                new RouteBarterTask("a", "A", new RoutePoint(100, 0), "raw", 5, "coin", 1),
                new RouteBarterTask("b", "B", new RoutePoint(200, 0), "raw", 5, "coin", 1),
            ],
            items,
            [new RouteWarehouse("W", "W", new RoutePoint(0, 0),
                new Dictionary<string, int>(StringComparer.Ordinal) { ["raw"] = 10 })],
            extraLT: 0,
            totalLT: 500,
            new RouteSearchLimits(10_000, 100),
            "extreme-test-v2");

        ExtremeRouteSolverRunResult result = ExtremeRouteSolverClient.Solve(
            request, incumbent: null, TimeSpan.FromSeconds(10), new ExtremeRouteResources(2, 1_024),
            TestContext.Current.CancellationToken, solver);

        Assert.Null(result.Failure);
        Assert.NotNull(result.Plan);
        Assert.Equal(2, result.Plan!.Routes.Count);
        Assert.True(RoutePlanVerifier.Verify(request, result.Plan).Success);
    }

    [Fact]
    public void Cp_sat_continuous_search_improves_a_verified_warm_start() {
        string solver = SolverPath();
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["raw"] = new("raw", "Raw", 1, 1),
            ["coin"] = new("coin", "Coin", 0, 0),
        };
        var request = new AutomaticRoutePlanningRequest(
            [
                new RouteBarterTask("east", "East", new RoutePoint(100, 0), "raw", 1, "coin", 1),
                new RouteBarterTask("north", "North", new RoutePoint(0, 100), "raw", 1, "coin", 1),
                new RouteBarterTask("north-east", "NorthEast", new RoutePoint(100, 100), "raw", 1, "coin", 1),
            ],
            items,
            [new RouteWarehouse("W", "W", new RoutePoint(0, 0),
                new Dictionary<string, int>(StringComparer.Ordinal) { ["raw"] = 3 })],
            extraLT: 0,
            totalLT: 3,
            new RouteSearchLimits(10_000, 100),
            "extreme-continuous-warm-start");

        RouteSimulationState state = RouteSimulationState.CreateInitial(request);
        state = AssertTransition(RouteStateTransition.TryPickup(
            request, state, "W", [new RouteItemQuantity("raw", 3)]));
        state = AssertTransition(RouteStateTransition.TryBarter(request, state, 0));
        state = AssertTransition(RouteStateTransition.TryBarter(request, state, 1));
        state = AssertTransition(RouteStateTransition.TryBarter(request, state, 2));
        state = AssertTransition(RouteStateTransition.TryUnload(request, state, "W"));
        RoutePlan seed = RoutePlanFactory.FromState(
            request, state, RoutePlanStatus.BestKnownWithinLimit, []);
        Assert.True(RoutePlanVerifier.Verify(request, seed).Success);

        ExtremeRouteSolverRunResult result = ExtremeRouteSolverClient.Solve(
            request, seed, TimeSpan.FromSeconds(20), new ExtremeRouteResources(2, 1_024),
            TestContext.Current.CancellationToken, solver);

        Assert.Null(result.Failure);
        Assert.Equal(1, result.AttemptCount);
        Assert.True(result.SolverCandidateCount > 0);
        Assert.True(result.SolverImprovementCount > 0);
        Assert.NotNull(result.LastSolverCandidate);
        Assert.NotNull(result.Plan);
        Assert.True(result.Plan!.Objective!.Value.TotalDistance
            < seed.Objective!.Value.TotalDistance);
        Assert.True(RoutePlanVerifier.Verify(request, result.Plan).Success);
    }

    [Fact]
    public void Cp_sat_candidate_is_normalized_before_redundant_cargo_can_reject_it() {
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["raw"] = new("raw", "Raw", 1, 100),
            ["coin"] = new("coin", "Coin", 0, 0),
        };
        var request = new AutomaticRoutePlanningRequest(
            [new RouteBarterTask("barter", "A", new RoutePoint(100, 0),
                "raw", 1, "coin", 10)],
            items,
            [new RouteWarehouse("Iliya", "Iliya", new RoutePoint(0, 0),
                new Dictionary<string, int>(StringComparer.Ordinal) { ["raw"] = 2 })],
            extraLT: 0,
            totalLT: 1_000,
            new RouteSearchLimits(10_000, 100),
            "extreme-redundant-cargo");

        var state = RouteSimulationState.CreateInitial(request);
        state = RouteStateTransition.TryPickup(
            request, state, "Iliya", [new RouteItemQuantity("raw", 2)]).State;
        state = RouteStateTransition.TryBarter(request, state, 0).State;
        state = RouteStateTransition.TryUnload(
            request, state, "Iliya", [new RouteItemQuantity("raw", 1)],
            finishRoute: true).State;
        var candidate = RoutePlanFactory.FromState(
            request, state, RoutePlanStatus.BestKnownWithinLimit, []);
        Assert.True(RoutePlanVerifier.TryDetectRouteRedundantCargoRoundTrip(
            request, candidate, out _));

        RoutePlan? accepted = ExtremeRouteSolverClient.PrepareCandidateForAcceptance(
            request, candidate, out string? failure);

        Assert.Null(failure);
        Assert.NotNull(accepted);
        var route = Assert.Single(accepted!.Routes);
        Assert.Equal(1, Assert.Single(route.Steps.OfType<WarehousePickupStep>())
            .Items.Single(item => item.ItemId == "raw").Quantity);
        Assert.Empty(Assert.Single(route.Steps.OfType<WarehouseUnloadStep>()).Items);
        Assert.False(RoutePlanVerifier.TryDetectRouteRedundantCargoRoundTrip(
            request, accepted, out _));
        Assert.True(RoutePlanVerifier.Verify(request, accepted).Success);
    }

    [Fact]
    public void Extreme_reuses_a_verified_incumbent_instead_of_rebuilding_its_seed() {
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["raw"] = new("raw", "Raw", 1, 100),
            ["coin"] = new("coin", "Coin", 0, 0),
        };
        var request = new AutomaticRoutePlanningRequest(
            [new RouteBarterTask("barter", "A", new RoutePoint(100, 0),
                "raw", 1, "coin", 10)],
            items,
            [new RouteWarehouse("W", "W", new RoutePoint(0, 0),
                new Dictionary<string, int>(StringComparer.Ordinal) { ["raw"] = 1 })],
            extraLT: 0,
            totalLT: 1_000,
            new RouteSearchLimits(10_000, 100),
            "extreme-reuse-seed");
        var planner = new AutomaticRoutePlanner();
        RoutePlan optimal = planner.Plan(
            request, RouteOptimizationProfile.For(RouteOptimizationMode.Balanced),
            TestContext.Current.CancellationToken);
        Assert.Equal(RoutePlanStatus.Optimal, optimal.Status);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        RoutePlan result = planner.Plan(
            request,
            RouteOptimizationProfile.For(RouteOptimizationMode.Extreme),
            optimal,
            TestContext.Current.CancellationToken);
        watch.Stop();

        Assert.Equal(RoutePlanStatus.Optimal, result.Status);
        Assert.Equal(optimal.Objective, result.Objective);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Detail?.Contains("seed-already-optimal") == true);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2),
            $"Reused optimal seed should return immediately, elapsed={watch.Elapsed}.");
    }

    private static string SolverPath() {
        DirectoryInfo directory = new(AppContext.BaseDirectory);
        for (int i = 0; i < 5; i++) directory = directory.Parent!;
        return Path.Combine(directory.FullName, "Tools", "ExtremeRouteSolver", "bin", "Release",
            "net10.0", "win-x64", "iBarter.ExtremeRouteSolver.exe");
    }

    private static RouteSimulationState AssertTransition(RouteTransitionResult transition) {
        Assert.True(
            transition.Success,
            transition.Diagnostic is null
                ? "transition failed without diagnostic"
                : $"{transition.Diagnostic.Code}:{transition.Diagnostic.Detail}");
        return transition.State;
    }

    private static ExtremeSolverInputDto GracefulStopInput() {
        const int taskCount = 12;
        var tasks = Enumerable.Range(0, taskCount)
            .Select(index => new ExtremeSolverTaskDto(
                index, $"task-{index}", "W", "raw", 1, "coin", 1))
            .ToArray();
        IReadOnlyList<IReadOnlyList<long>> Square(int size) => Enumerable.Range(0, size)
            .Select(_ => (IReadOnlyList<long>)new long[size]).ToArray();
        IReadOnlyList<IReadOnlyList<long>> Rectangle(int rows, int columns) =>
            Enumerable.Range(0, rows)
                .Select(_ => (IReadOnlyList<long>)new long[columns]).ToArray();

        return new ExtremeSolverInputDto(
            ExtremeRouteSolverProtocol.Version,
            TimeLimitSeconds: 10,
            MemoryLimitMb: 1_024,
            WorkerCount: 2,
            MaxRoutes: 1,
            CargoCapacityLT: taskCount,
            [
                new ExtremeSolverItemDto("raw", 1, taskCount),
                new ExtremeSolverItemDto("coin", 0, taskCount),
            ],
            [new ExtremeSolverWarehouseDto(
                "W", "W", new Dictionary<string, int>(StringComparer.Ordinal) {
                    ["raw"] = taskCount,
                    ["coin"] = 0,
                })],
            tasks,
            new Dictionary<string, int>(StringComparer.Ordinal),
            [new ExtremeSolverRouteDto(
                1, "W", "W", [new ExtremeSolverPickupDto("raw", taskCount)],
                Enumerable.Range(0, taskCount).ToArray())],
            Square(taskCount),
            Rectangle(1, taskCount),
            Rectangle(taskCount, 1),
            Square(1));
    }
}
