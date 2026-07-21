using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public sealed class ExtremeCurrentDataSmokeTests {
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

        Console.WriteLine($"[EXTREME-SMOKE] tasks={request.Tasks.Count} seed={load.Status}/{seed?.Routes.Count}/" +
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
        RouteOptimizationProfile profile) {
        using JsonDocument plan = JsonDocument.Parse(File.ReadAllText(Path.Combine(resources, "myPlan_Data.json")));
        var plannerRows = plan.RootElement.EnumerateArray().Select(row => {
            JsonElement item1 = row.GetProperty("Item1");
            JsonElement item2 = row.GetProperty("Item2");
            return new PlannerRouteSnapshot(
                row.GetProperty("PlannerRowId").GetString()!,
                row.GetProperty("ExchangeDone").GetBoolean(),
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

        var islands = File.ReadLines(Path.Combine(resources, "Islands.csv"))
            .Select(line => line.Split(','))
            .Where(parts => parts.Length >= 8)
            .Select(parts => new IslandRouteSnapshot(
                parts[0],
                new RoutePoint(
                    double.Parse(parts[6], CultureInfo.InvariantCulture),
                    double.Parse(parts[7], CultureInfo.InvariantCulture))))
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
        DirectoryInfo directory = new(AppContext.BaseDirectory);
        for (int i = 0; i < 5; i++) directory = directory.Parent!;
        return Path.Combine(directory.FullName, "Tools", "ExtremeRouteSolver", "bin", "Release",
            "net10.0", "win-x64", "iBarter.ExtremeRouteSolver.exe");
    }
}
