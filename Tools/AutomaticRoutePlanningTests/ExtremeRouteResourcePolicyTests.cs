using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public sealed class ExtremeRouteResourcePolicyTests {
    [Theory]
    [InlineData(1, 2_048, 1_024)]
    [InlineData(4, 8_192, 4_096)]
    [InlineData(12, 32_768, 12_288)]
    [InlineData(28, 65_536, 34_816)]
    public void Adaptive_budget_leaves_cpu_and_memory_headroom(
        int processors,
        long totalMemoryMb,
        long availableMemoryMb) {
        ExtremeRouteResources resources = ExtremeRouteResourcePolicy.ForMachine(
            processors, totalMemoryMb, availableMemoryMb);

        Assert.InRange(resources.WorkerCount, 1, Math.Max(1, processors - 1));
        Assert.InRange(resources.MemoryLimitMb, 512, checked((int)(totalMemoryMb / 2)));
        Assert.True(resources.MemoryLimitMb <= availableMemoryMb * 2 / 3);
    }

    [Fact]
    public void Larger_machine_receives_a_larger_budget_without_fixed_machine_values() {
        ExtremeRouteResources small = ExtremeRouteResourcePolicy.ForMachine(4, 8_192, 4_096);
        ExtremeRouteResources large = ExtremeRouteResourcePolicy.ForMachine(28, 65_536, 34_816);

        Assert.True(large.WorkerCount > small.WorkerCount);
        Assert.True(large.MemoryLimitMb > small.MemoryLimitMb);
        Assert.NotEqual(8, large.WorkerCount);
        Assert.NotEqual(8_192, large.MemoryLimitMb);
    }

    [Fact]
    public void Live_detection_returns_a_usable_budget() {
        ExtremeRouteResources resources = ExtremeRouteResourcePolicy.Detect();

        Assert.InRange(resources.WorkerCount, 1, Math.Max(1, Environment.ProcessorCount));
        Assert.True(resources.MemoryLimitMb >= 512);
    }
}
