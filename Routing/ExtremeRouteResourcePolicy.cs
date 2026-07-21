using System.Runtime.InteropServices;

namespace iBarter.Routing;

public readonly record struct ExtremeRouteResources(int WorkerCount, int MemoryLimitMb);

/// <summary>
/// Chooses a bounded CP-SAT resource budget from the machine that is actually
/// running iBarter. The WPF process is x86, so its process/GC ceiling is not a
/// reliable measure of the physical RAM available to the x64 solver.
/// </summary>
public static class ExtremeRouteResourcePolicy {
    private const long BytesPerMegabyte = 1024L * 1024L;

    public static ExtremeRouteResources Detect() {
        int logicalProcessors = Math.Max(1, Environment.ProcessorCount);
        if (TryGetPhysicalMemory(out long totalMemoryMb, out long availableMemoryMb))
            return ForMachine(logicalProcessors, totalMemoryMb, availableMemoryMb);

        long fallbackMb = Math.Max(512,
            GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / BytesPerMegabyte);
        return ForMachine(logicalProcessors, fallbackMb, fallbackMb);
    }

    public static ExtremeRouteResources ForMachine(
        int logicalProcessors,
        long totalMemoryMb,
        long availableMemoryMb) {
        logicalProcessors = Math.Max(1, logicalProcessors);
        totalMemoryMb = Math.Max(512, totalMemoryMb);
        availableMemoryMb = Math.Clamp(availableMemoryMb, 512, totalMemoryMb);

        // Keep roughly one quarter of the logical processors available for
        // WPF, OCR, Windows and other foreground applications.
        int reservedProcessors = Math.Max(1, (logicalProcessors + 3) / 4);
        int workers = Math.Max(1, logicalProcessors - reservedProcessors);

        // Use the tighter of half the installed RAM and two thirds of the RAM
        // that is free when planning starts. This shrinks under memory pressure.
        long installedBoundMb = totalMemoryMb / 2;
        long availableBoundMb = availableMemoryMb * 2 / 3;
        long memoryLimitMb = Math.Max(512, Math.Min(installedBoundMb, availableBoundMb));
        memoryLimitMb = Math.Min(memoryLimitMb, int.MaxValue);

        return new ExtremeRouteResources(workers, (int)memoryLimitMb);
    }

    private static bool TryGetPhysicalMemory(out long totalMemoryMb, out long availableMemoryMb) {
        totalMemoryMb = 0;
        availableMemoryMb = 0;
        if (!OperatingSystem.IsWindows()) return false;

        var status = new MemoryStatusEx {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>(),
        };
        if (!GlobalMemoryStatusEx(ref status)) return false;

        totalMemoryMb = checked((long)(status.TotalPhysical / (ulong)BytesPerMegabyte));
        availableMemoryMb = checked((long)(status.AvailablePhysical / (ulong)BytesPerMegabyte));
        return totalMemoryMb > 0 && availableMemoryMb > 0;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}
