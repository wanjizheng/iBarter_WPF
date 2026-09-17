using System.Diagnostics;
using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

// Headless reproduction of the slow "15-route crow-coin" scenario reported in
// the field (~184s in the WPF app). Unlike RouteTestData.IndependentTasks, this
// builds interdependent chains (raw -> intermediate -> crow coin) spread across
// four warehouses, which is what actually drives heavy beam expansion and the
// repeated local-optimization passes.
//
// Gated behind the ROUTE_BENCH environment variable so the normal test run does
// not pay the cost. Run with:
//   ROUTE_BENCH=1 AutomaticRoutePlanningTests.exe
public sealed class RoutePlannerBenchmark {
    private readonly ITestOutputHelper output;
    public RoutePlannerBenchmark(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Bench_interdependent_crowcoin_15() {
        if (Environment.GetEnvironmentVariable("ROUTE_BENCH") is null) return;

        var request = InterdependentCrowCoinPlan(15, new RouteSearchLimits(250_000, 2_000));
        foreach (var mode in new[] { RouteOptimizationMode.Quick, RouteOptimizationMode.Balanced, RouteOptimizationMode.Deep }) {
            BenchOnce(request, mode, output);
        }
    }

    private static void BenchOnce(
        AutomaticRoutePlanningRequest request,
        RouteOptimizationMode mode,
        ITestOutputHelper output) {
        var profile = RouteOptimizationProfile.For(mode);
        long allocBefore = GC.GetTotalAllocatedBytes(true);
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        using var _ = RouteSearchProfiler.Enable();
        var sw = Stopwatch.StartNew();

        var plan = new AutomaticRoutePlanner().Plan(request, profile, TestContext.Current.CancellationToken);

        sw.Stop();
        var snap = RouteSearchProfiler.Snapshot()?.Report();
        long allocAfter = GC.GetTotalAllocatedBytes(true);
        var verify = RoutePlanVerifier.Verify(request, plan);

        string line = $"[BENCH] mode={mode} status={plan.Status} elapsed_ms={sw.ElapsedMilliseconds} " +
            $"routes={plan.Objective?.RouteCount} distance={plan.Objective?.TotalDistance:F1} " +
            $"pickups={plan.Objective?.PickupStopCount} peak={plan.Objective?.MaxPeakLT} " +
            $"verify_ok={verify.Success} alloc_MB={(allocAfter - allocBefore) / (1024.0 * 1024.0):F1}";
        Console.WriteLine(line);
        output.WriteLine(line);
        if (snap is not null) {
            Console.WriteLine(snap);
            output.WriteLine(snap);
        }
    }

    // Four spread warehouses, six raw inputs, six produced intermediates, and a
    // zero-weight crow coin. Producer tasks turn raw -> intermediate; consumer
    // tasks turn intermediate -> crow coin (forcing sequencing across routes);
    // a few direct raw -> crow coin tasks compete for the same raw stock.
    public static AutomaticRoutePlanningRequest InterdependentCrowCoinPlan(
        int taskCount, RouteSearchLimits limits) {
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
            ["CC"] = new("CC", "CrowCoin", 1, 0),
        };
        for (int r = 0; r < 6; r++) {
            items[$"R{r}"] = new($"R{r}", $"Raw{r}", 1, 100);
            items[$"M{r}"] = new($"M{r}", $"Mid{r}", 3, 500);
        }

        var warehouses = new[] {
            Warehouse("Velia", 0, 0, ("R0", 12), ("R1", 12)),
            Warehouse("Iliya", 1000, 0, ("R2", 12), ("R3", 12)),
            Warehouse("Epheria", 0, 1000, ("R4", 12), ("R5", 12)),
            Warehouse("Ancado", 1000, 1000, ("R0", 6), ("R2", 6), ("R4", 6)),
        };

        var tasks = new List<RouteBarterTask>();
        // Producers: R_i x3 -> M_i x2
        for (int i = 0; i < 6; i++)
            tasks.Add(new RouteBarterTask(
                $"p{i}", $"P{i}", new RoutePoint(200 + i * 90, 150 + (i % 3) * 120),
                $"R{i}", 3, $"M{i}", 2));
        // Consumers: M_i x2 -> CC x100
        for (int i = 0; i < 6 && tasks.Count < taskCount; i++)
            tasks.Add(new RouteBarterTask(
                $"c{i}", $"Q{i}", new RoutePoint(700 - i * 80, 400 + (i % 3) * 110),
                $"M{i}", 2, "CC", 100));
        // Direct: R_i x2 -> CC x50 (competes for raw)
        for (int i = 0; i < 6 && tasks.Count < taskCount; i++)
            tasks.Add(new RouteBarterTask(
                $"d{i}", $"D{i}", new RoutePoint(450 + i * 60, 800 - (i % 2) * 90),
                $"R{i}", 2, "CC", 50));

        return new AutomaticRoutePlanningRequest(
            tasks.Take(taskCount).ToArray(), items, warehouses,
            extraLT: 200, totalLT: 1_500, limits, "bench-crowcoin-v1");
    }

    private static RouteWarehouse Warehouse(
        string id, double x, double y, params (string Item, int Qty)[] stock) =>
        new(id, id, new RoutePoint(x, y),
            stock.ToDictionary(s => s.Item, s => s.Qty, StringComparer.Ordinal));
}
