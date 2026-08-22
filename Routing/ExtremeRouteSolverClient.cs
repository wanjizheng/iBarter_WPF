using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using iBarter.Navigation;
using Microsoft.Win32.SafeHandles;

namespace iBarter.Routing;

public sealed record ExtremeRouteSolverRunResult(
    RoutePlan? Plan,
    string SolverStatus,
    int RouteLimit,
    bool FullRouteSpace,
    double BestBound,
    double RelativeGap,
    TimeSpan Elapsed,
    long Conflicts,
    long Branches,
    int WorkerCount,
    int MemoryLimitMb,
    int AttemptCount,
    string? Failure,
    ExtremeSearchTerminationReason TerminationReason = ExtremeSearchTerminationReason.None,
    RoutePlan? LastSolverCandidate = null,
    int SolverCandidateCount = 0,
    int SolverImprovementCount = 0);

public static class ExtremeRouteSolverClient {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true,
    };

    public static ExtremeRouteSolverRunResult Solve(
        AutomaticRoutePlanningRequest request,
        RoutePlan? incumbent,
        TimeSpan timeLimit,
        ExtremeRouteResources resources,
        CancellationToken cancellationToken,
        string? solverPath = null) {
        var controller = new ExtremeSearchController(
            timeLimit,
            ExtremeRouteSolverProtocol.ExtremeNoImprovementTimeout);
        controller.TryAcceptCandidate(incumbent);
        return Solve(
            request, incumbent, resources, cancellationToken,
            controller, progress: null, solverPath);
    }

    internal static ExtremeRouteSolverRunResult Solve(
        AutomaticRoutePlanningRequest request,
        RoutePlan? incumbent,
        ExtremeRouteResources resources,
        CancellationToken cancellationToken,
        ExtremeSearchController controller,
        Action<ExtremeSearchProgressSnapshot>? progress,
        string? solverPath = null) {
        var failures = new List<string>();
        ExtremeRouteResources attemptResources = resources;
        int attempts = 0;
        ExtremeRouteSolverRunResult? lastSuccessful = null;
        RoutePlan? lastSolverCandidate = null;
        int solverCandidateCount = 0;
        int solverImprovementCount = 0;

        controller.TryAcceptCandidate(incumbent);
        progress?.Invoke(controller.Snapshot());

        while (true) {
            ExtremeSearchTerminationReason reason = controller.CheckTermination(
                cancellationToken.IsCancellationRequested);
            if (reason != ExtremeSearchTerminationReason.None) {
                controller.Finish(reason);
                return Finish(reason, null);
            }

            TimeSpan attemptLimit = SelectAttemptDuration(
                controller.RemainingUntilMaximum);
            if (attemptLimit <= TimeSpan.Zero)
                return Finish(controller.Finish(
                    ExtremeSearchTerminationReason.MaxDurationReached), null);

            attempts++;
            ExtremeRouteSolverRunResult result = SolveOnce(
                request,
                controller.BestPlan,
                attemptLimit,
                attemptResources,
                cancellationToken,
                controller,
                progress,
                solverPath);
            if (!StringComparer.Ordinal.Equals(result.SolverStatus, "Unavailable"))
                lastSuccessful = result;

            bool improved = false;
            if (result.Plan is not null) {
                lastSolverCandidate = result.Plan;
                solverCandidateCount++;
                improved = controller.TryAcceptCandidate(result.Plan);
                if (improved)
                    solverImprovementCount++;
            }
            progress?.Invoke(controller.Snapshot());

            if (result.TerminationReason != ExtremeSearchTerminationReason.None) {
                reason = result.TerminationReason;
                // A candidate found inside the attempt before the graceful stop
                // invalidates a pending no-improvement decision. Its accepted
                // timestamp starts a fresh 90-second convergence window.
                if (reason == ExtremeSearchTerminationReason.NoImprovementConverged
                    && improved)
                    continue;
                controller.Finish(reason, result.Failure);
                return Finish(reason, result.Failure);
            }

            if (StringComparer.OrdinalIgnoreCase.Equals(result.SolverStatus, "Optimal")) {
                reason = controller.Finish(ExtremeSearchTerminationReason.Completed);
                return Finish(reason, result.Failure);
            }
            if (StringComparer.OrdinalIgnoreCase.Equals(result.SolverStatus, "Infeasible")
                && controller.BestPlan is null) {
                reason = controller.Finish(ExtremeSearchTerminationReason.Completed);
                return Finish(reason, result.Failure);
            }

            if (StringComparer.Ordinal.Equals(result.SolverStatus, "Unavailable")) {
                if (IsNativeExit(result.Failure) && attemptResources.WorkerCount > 1) {
                    failures.Add($"workers={attemptResources.WorkerCount}:{result.Failure}");
                    int reducedWorkers = Math.Max(1, attemptResources.WorkerCount / 2);
                    attemptResources = attemptResources with { WorkerCount = reducedWorkers };
                    continue;
                }
                reason = controller.Finish(ExtremeSearchTerminationReason.Error, result.Failure);
                return Finish(reason, result.Failure);
            }
            if (StringComparer.OrdinalIgnoreCase.Equals(result.SolverStatus, "Error")
                || StringComparer.OrdinalIgnoreCase.Equals(result.SolverStatus, "ModelInvalid")) {
                reason = controller.Finish(ExtremeSearchTerminationReason.Error, result.Failure);
                return Finish(reason, result.Failure);
            }

            // A normal Feasible/Unknown response is the final result of the
            // one helper that owned the full remaining budget. Restarting here
            // cannot resume its presolve, bound, solution pool, or LNS state;
            // only the explicit Unavailable/native-failure path above may
            // launch a replacement helper with reduced resources.
            reason = controller.Finish(
                ExtremeSearchTerminationReason.MaxDurationReached);
            return Finish(reason, result.Failure);
        }

        ExtremeRouteSolverRunResult Finish(
            ExtremeSearchTerminationReason reason,
            string? failure) {
            string? combinedFailure = failure;
            if (reason == ExtremeSearchTerminationReason.Error && failures.Count > 0)
                combinedFailure = string.IsNullOrWhiteSpace(failure)
                    ? string.Join(" | ", failures)
                    : $"{string.Join(" | ", failures)} | final={failure}";
            ExtremeSearchProgressSnapshot snapshot = controller.Snapshot();
            progress?.Invoke(snapshot);
            return new ExtremeRouteSolverRunResult(
                controller.BestPlan,
                lastSuccessful?.SolverStatus ?? "NotRun",
                lastSuccessful?.RouteLimit ?? 0,
                lastSuccessful?.FullRouteSpace ?? false,
                lastSuccessful?.BestBound ?? 0,
                lastSuccessful?.RelativeGap ?? 0,
                snapshot.Elapsed,
                lastSuccessful?.Conflicts ?? 0,
                lastSuccessful?.Branches ?? 0,
                lastSuccessful?.WorkerCount ?? attemptResources.WorkerCount,
                lastSuccessful?.MemoryLimitMb ?? attemptResources.MemoryLimitMb,
                attempts,
                combinedFailure,
                reason,
                lastSolverCandidate,
                solverCandidateCount,
                solverImprovementCount);
        }
    }

    /// <summary>
    /// A normal Extreme/Custom attempt owns the entire remaining wall-clock
    /// budget. Restarting CP-SAT every few seconds discards presolve, bounds,
    /// the solution pool, and LNS history; retries are reserved for an actual
    /// helper failure or an early terminal solver result.
    /// </summary>
    internal static TimeSpan SelectAttemptDuration(TimeSpan remaining) =>
        remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;

    private static ExtremeRouteSolverRunResult SolveOnce(
        AutomaticRoutePlanningRequest request,
        RoutePlan? incumbent,
        TimeSpan timeLimit,
        ExtremeRouteResources resources,
        CancellationToken cancellationToken,
        ExtremeSearchController controller,
        Action<ExtremeSearchProgressSnapshot>? progress,
        string? solverPath) {
        var watch = Stopwatch.StartNew();
        if (request.Tasks.Count > ExtremeRouteSolverProtocol.MaximumTasks)
            return Failure($"task-limit:{request.Tasks.Count}");

        solverPath ??= ResolveSolverPath();
        if (string.IsNullOrWhiteSpace(solverPath) || !File.Exists(solverPath))
            return Failure("solver-not-found");

        string tempRoot = Path.Combine(Path.GetTempPath(), "iBarter", "ExtremeRouteSolver");
        string work = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        string inputPath = Path.Combine(work, "input.json");
        string outputPath = Path.Combine(work, "best.json");
        string stopSignalPath = Path.Combine(work, "stop.signal");
        try {
            var dto = BuildInput(request, incumbent, timeLimit, resources);
            File.WriteAllText(inputPath, JsonSerializer.Serialize(dto, JsonOptions));

            using var process = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = solverPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    WorkingDirectory = Path.GetDirectoryName(solverPath)!,
                },
            };
            process.StartInfo.ArgumentList.Add(inputPath);
            process.StartInfo.ArgumentList.Add(outputPath);
            process.StartInfo.ArgumentList.Add(stopSignalPath);
            if (!process.Start()) return Failure("solver-start-failed");

            using var job = WindowsProcessMemoryJob.TryCreate(process, resources.MemoryLimitMb);
            TimeSpan hardLimit = timeLimit + TimeSpan.FromSeconds(15);
            ExtremeSearchTerminationReason interrupted = ExtremeSearchTerminationReason.None;
            Stopwatch? gracefulStopWatch = null;
            string? gracefulStopFailure = null;
            bool hardLimitExceeded = false;
            while (!process.WaitForExit(ExtremeRouteSolverProtocol.ProcessPollMilliseconds)) {
                interrupted = controller.CheckTermination(
                    cancellationToken.IsCancellationRequested);
                progress?.Invoke(controller.Snapshot());
                if (interrupted != ExtremeSearchTerminationReason.None) {
                    if (gracefulStopWatch is null) {
                        try {
                            File.WriteAllText(stopSignalPath, interrupted.ToString());
                            gracefulStopWatch = Stopwatch.StartNew();
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                            gracefulStopFailure =
                                $"stop-signal:{ex.GetType().Name}:{SingleLine(ex.Message)}";
                            Kill(process);
                            break;
                        }
                    }
                    else if (gracefulStopWatch.Elapsed >= TimeSpan.FromSeconds(
                                 ExtremeRouteSolverProtocol.GracefulStopTimeoutSeconds)) {
                        gracefulStopFailure = "graceful-stop-timeout";
                        Kill(process);
                        break;
                    }
                }
                else if (watch.Elapsed >= hardLimit) {
                    hardLimitExceeded = true;
                    Kill(process);
                    break;
                }
            }

            if (interrupted == ExtremeSearchTerminationReason.None
                && cancellationToken.IsCancellationRequested)
                interrupted = controller.CheckTermination(userCancellationRequested: true);

            if (!process.HasExited)
                process.WaitForExit(1_000);

            ExtremeSolverOutputDto? output = TryReadOutput(outputPath);
            if (output is null) {
                string stderr = process.HasExited ? process.StandardError.ReadToEnd().Trim() : "";
                string exit = process.HasExited
                    ? process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "running";
                string detail = gracefulStopFailure
                    ?? (hardLimitExceeded
                        ? "solver-hard-limit"
                        : string.IsNullOrEmpty(stderr)
                            ? $"solver-exit:{exit}"
                            : $"solver-exit:{exit}:{SingleLine(stderr)}");
                return Failure(detail, interrupted);
            }
            if (output.ProtocolVersion != ExtremeRouteSolverProtocol.Version)
                return Failure($"solver-protocol:{output.ProtocolVersion}");

            RoutePlan? candidate = null;
            string? replayFailure = null;
            if (output.Routes.Count > 0) {
                candidate = Replay(request, output, out replayFailure);
                if (candidate is not null) {
                    candidate = PrepareCandidateForAcceptance(
                        request, candidate, out string? preparationFailure);
                    replayFailure ??= preparationFailure;
                }
            }

            double bound = output.BestBoundScaled / (double)ExtremeRouteSolverProtocol.DistanceScale;
            string? outputFailure = interrupted == ExtremeSearchTerminationReason.None
                ? output.Error is null ? null : SingleLine(output.Error)
                : output.Status is "Error" or "ModelInvalid"
                    ? output.Error is null ? null : SingleLine(output.Error)
                    : null;
            return new ExtremeRouteSolverRunResult(
                candidate,
                output.Status,
                output.RouteLimit,
                output.RouteLimit >= request.Tasks.Count,
                bound,
                output.RelativeGap,
                TimeSpan.FromSeconds(Math.Max(output.WallTimeSeconds, watch.Elapsed.TotalSeconds)),
                output.Conflicts,
                output.Branches,
                output.WorkerCount,
                output.MemoryLimitMb,
                1,
                replayFailure ?? gracefulStopFailure ?? outputFailure,
                interrupted);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) {
            return Failure($"{ex.GetType().Name}:{SingleLine(ex.Message)}");
        }
        finally {
            try { Directory.Delete(work, recursive: true); } catch { }
        }

        ExtremeRouteSolverRunResult Failure(
            string detail,
            ExtremeSearchTerminationReason terminationReason =
                ExtremeSearchTerminationReason.None) => new(
            null, "Unavailable", 0, false, 0, 1, watch.Elapsed, 0, 0,
            resources.WorkerCount, resources.MemoryLimitMb, 1, detail,
            terminationReason);
    }

    private static bool IsNativeExit(string? failure) =>
        failure?.StartsWith("solver-exit:", StringComparison.Ordinal) == true;

    /// <summary>
    /// CP-SAT minimizes sailing distance, so pickup quantities that do not
    /// affect distance may contain harmless surplus cargo. Run the same
    /// normalization/replay/full-verification contract used at publication
    /// before deciding whether the exact candidate can compete with the
    /// incumbent. This also preserves cargo that is staged for later routes.
    /// </summary>
    internal static RoutePlan? PrepareCandidateForAcceptance(
        AutomaticRoutePlanningRequest request,
        RoutePlan candidate,
        out string? failure) {
        var prepared = RoutePlanPublication.PreparePlanForPublication(
            request,
            candidate,
            RoutePlanPublicationSource.FreshGeneration);
        if (prepared.Success && prepared.Plan is not null) {
            failure = null;
            return prepared.Plan;
        }

        failure = prepared.Failure is null
            ? "candidate-preparation-failed"
            : $"{prepared.Failure.Code}:{prepared.Failure.Detail}";
        return null;
    }

    private static ExtremeSolverInputDto BuildInput(
        AutomaticRoutePlanningRequest request,
        RoutePlan? incumbent,
        TimeSpan timeLimit,
        ExtremeRouteResources resources) {
        string[] relevantIds = request.Tasks
            .SelectMany(task => new[] { task.Item1Id, task.Item2Id })
            .Concat(request.InitialOnBoard.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        var items = relevantIds.Select(id => {
            RouteItem item = request.Items[id];
            long upper = request.InitialOnBoard.GetValueOrDefault(id);
            upper += request.Warehouses.Sum(warehouse => (long)warehouse.Inventory.GetValueOrDefault(id));
            upper += request.Tasks.Where(task => task.Item2Id == id).Sum(task => (long)task.OutputQuantity);
            return new ExtremeSolverItemDto(id, item.UnitWeight, Math.Max(1, upper));
        }).ToArray();
        var warehouses = request.Warehouses.Select(warehouse => new ExtremeSolverWarehouseDto(
            warehouse.WarehouseId,
            warehouse.IslandId,
            relevantIds.ToDictionary(id => id, id => warehouse.Inventory.GetValueOrDefault(id), StringComparer.Ordinal)))
            .ToArray();
        var tasks = request.Tasks.Select((task, index) => new ExtremeSolverTaskDto(
            index, task.RowId, task.IslandId, task.Item1Id, task.InputQuantity,
            task.Item2Id, task.OutputQuantity)).ToArray();

        int routeLimit = Math.Min(
            request.Tasks.Count,
            incumbent?.Routes.Count is { } incumbentRoutes
                ? Math.Max(incumbentRoutes, Math.Min(request.Tasks.Count, incumbentRoutes + 2))
                : Math.Min(request.Tasks.Count, 12));
        return new ExtremeSolverInputDto(
            ExtremeRouteSolverProtocol.Version,
            Math.Max(1, (int)Math.Ceiling(timeLimit.TotalSeconds)),
            Math.Max(256, resources.MemoryLimitMb),
            Math.Clamp(resources.WorkerCount, 1, Math.Max(1, Environment.ProcessorCount)),
            routeLimit,
            checked(request.TotalLT - request.ExtraLT),
            items,
            warehouses,
            tasks,
            request.InitialOnBoard,
            BuildSeedRoutes(request, incumbent),
            Matrix(request.Tasks, request.Tasks, Distance),
            Matrix(request.Warehouses, request.Tasks, Distance),
            Matrix(request.Tasks, request.Warehouses, Distance),
            Matrix(request.Warehouses, request.Warehouses, Distance));
    }

    private static IReadOnlyList<ExtremeSolverRouteDto> BuildSeedRoutes(
        AutomaticRoutePlanningRequest request,
        RoutePlan? incumbent) {
        if (incumbent is null) return [];
        var taskIndexes = request.Tasks.Select((task, index) => (task.RowId, index))
            .ToDictionary(x => x.RowId, x => x.index, StringComparer.Ordinal);
        var result = new List<ExtremeSolverRouteDto>();
        foreach (PlannedRoute route in incumbent.Routes) {
            WarehousePickupStep? pickup = route.Steps.FirstOrDefault() as WarehousePickupStep;
            string? startWarehouse = pickup?.WarehouseId;
            string endWarehouse = route.Steps.OfType<WarehouseUnloadStep>().LastOrDefault()?.WarehouseId
                ?? route.EndWarehouseId;
            int[] routeTasks = route.Steps.OfType<BarterStep>()
                .Where(step => taskIndexes.ContainsKey(step.RowId))
                .Select(step => taskIndexes[step.RowId]).ToArray();
            result.Add(new ExtremeSolverRouteDto(
                route.Number,
                startWarehouse,
                endWarehouse,
                pickup?.Items.Select(item => new ExtremeSolverPickupDto(item.ItemId, item.Quantity)).ToArray() ?? [],
                routeTasks));
        }
        return result;
    }

    private static IReadOnlyList<IReadOnlyList<long>> Matrix<TFrom, TTo>(
        IReadOnlyList<TFrom> from,
        IReadOnlyList<TTo> to,
        Func<TFrom, TTo, double> distance) => from.Select(a =>
            (IReadOnlyList<long>)to.Select(b => Scale(distance(a, b))).ToArray()).ToArray();

    private static double Distance(RouteBarterTask from, RouteBarterTask to) =>
        ShippingCorridorGraph.Distance(
            from.IslandId, new NavigationPoint(from.Point.X, from.Point.Y),
            to.IslandId, new NavigationPoint(to.Point.X, to.Point.Y));

    private static double Distance(RouteWarehouse from, RouteBarterTask to) =>
        ShippingCorridorGraph.Distance(
            from.IslandId, new NavigationPoint(from.Point.X, from.Point.Y),
            to.IslandId, new NavigationPoint(to.Point.X, to.Point.Y));

    private static double Distance(RouteBarterTask from, RouteWarehouse to) =>
        ShippingCorridorGraph.Distance(
            from.IslandId, new NavigationPoint(from.Point.X, from.Point.Y),
            to.IslandId, new NavigationPoint(to.Point.X, to.Point.Y));

    private static double Distance(RouteWarehouse from, RouteWarehouse to) =>
        ShippingCorridorGraph.Distance(
            from.IslandId, new NavigationPoint(from.Point.X, from.Point.Y),
            to.IslandId, new NavigationPoint(to.Point.X, to.Point.Y));

    private static long Scale(double distance) => checked((long)Math.Round(
        distance * ExtremeRouteSolverProtocol.DistanceScale,
        MidpointRounding.AwayFromZero));

    private static RoutePlan? Replay(
        AutomaticRoutePlanningRequest request,
        ExtremeSolverOutputDto output,
        out string? failure) {
        var state = RouteSimulationState.CreateInitial(request);
        foreach (ExtremeSolverRouteDto route in output.Routes.OrderBy(route => route.Number)) {
            if (route.StartWarehouseId is not null) {
                var picked = route.Pickup
                    .Where(item => item.Quantity > 0)
                    .Select(item => new RouteItemQuantity(item.ItemId, item.Quantity)).ToArray();
                var transition = RouteStateTransition.TryPickup(request, state, route.StartWarehouseId, picked);
                if (!transition.Success) { failure = Describe(transition.Diagnostic); return null; }
                state = transition.State;
            }
            foreach (int taskIndex in route.TaskIndices) {
                var transition = RouteStateTransition.TryBarter(request, state, taskIndex);
                if (!transition.Success) { failure = Describe(transition.Diagnostic); return null; }
                state = transition.State;
            }
            var unloadItems = state.OnBoard
                .Where(pair => pair.Value > 0 && request.Items[pair.Key].UnitWeight > 0)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new RouteItemQuantity(pair.Key, pair.Value)).ToArray();
            var unload = RouteStateTransition.TryUnload(
                request, state, route.EndWarehouseId, unloadItems, finishRoute: true);
            if (!unload.Success) { failure = Describe(unload.Diagnostic); return null; }
            state = unload.State;
        }
        failure = null;
        var status = StringComparer.OrdinalIgnoreCase.Equals(output.Status, "Optimal")
            ? RoutePlanStatus.Optimal
            : RoutePlanStatus.BestKnownWithinLimit;
        return RoutePlanFactory.FromState(request, state, status, []);
    }

    private static ExtremeSolverOutputDto? TryReadOutput(string path) {
        try {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<ExtremeSolverOutputDto>(File.ReadAllText(path), JsonOptions)
                : null;
        }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
    }

    private static string? ResolveSolverPath() {
        string? configured = Environment.GetEnvironmentVariable("IBARTER_EXTREME_SOLVER_PATH");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidates = [
            Path.Combine(baseDirectory, "ExtremeSolver", "iBarter.ExtremeRouteSolver.exe"),
            Path.Combine(baseDirectory, "iBarter.ExtremeRouteSolver.exe"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    private static void Kill(Process process) {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
    }

    private static string Describe(RouteDiagnostic? diagnostic) => diagnostic is null
        ? "replay-failed"
        : $"{diagnostic.Code}:{diagnostic.RowId}:{diagnostic.ItemId}:{diagnostic.Detail}";

    private static string SingleLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ').Trim();
}

internal sealed class WindowsProcessMemoryJob : IDisposable {
    private readonly SafeFileHandle handle;

    private WindowsProcessMemoryJob(SafeFileHandle handle) => this.handle = handle;

    public static WindowsProcessMemoryJob? TryCreate(Process process, int memoryLimitMb) {
        if (!OperatingSystem.IsWindows()) return null;
        SafeFileHandle job = Native.CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid) { job.Dispose(); return null; }
        long requestedBytes = checked((long)memoryLimitMb * 1024L * 1024L);
        bool applyHardMemoryLimit = CanApplyHardMemoryLimit(
            memoryLimitMb, Environment.Is64BitProcess);
        uint limitFlags = Native.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        nuint processMemoryLimit = 0;
        if (applyHardMemoryLimit) {
            limitFlags |= Native.JOB_OBJECT_LIMIT_PROCESS_MEMORY;
            processMemoryLimit = checked((nuint)requestedBytes);
        }
        var limits = new Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION {
            BasicLimitInformation = new Native.JOBOBJECT_BASIC_LIMIT_INFORMATION {
                LimitFlags = limitFlags,
            },
            ProcessMemoryLimit = processMemoryLimit,
        };
        int size = Marshal.SizeOf<Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        IntPtr pointer = Marshal.AllocHGlobal(size);
        try {
            Marshal.StructureToPtr(limits, pointer, false);
            if (!Native.SetInformationJobObject(job, 9, pointer, (uint)size)
                || !Native.AssignProcessToJobObject(job, process.SafeHandle)) {
                job.Dispose();
                return null;
            }
            return new WindowsProcessMemoryJob(job);
        }
        finally {
            Marshal.FreeHGlobal(pointer);
        }
    }

    internal static bool CanApplyHardMemoryLimit(int memoryLimitMb, bool is64BitProcess) {
        long requestedBytes = checked((long)memoryLimitMb * 1024L * 1024L);
        // JOBOBJECT_EXTENDED_LIMIT_INFORMATION uses SIZE_T. An x86 caller
        // cannot represent a limit above 4 GiB even when the job's target is
        // the x64 solver. Truncating it silently turns (for example) 21 GiB
        // into a sub-1-GiB limit, so omit the job memory flag and let the x64
        // CP-SAT max_memory_in_mb parameter enforce the requested ceiling.
        return is64BitProcess || requestedBytes <= uint.MaxValue;
    }

    public void Dispose() => handle.Dispose();

    private static class Native {
        internal const uint JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x00000100;
        internal const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

        [StructLayout(LayoutKind.Sequential)]
        internal struct JOBOBJECT_BASIC_LIMIT_INFORMATION {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public nuint MinimumWorkingSetSize;
            public nuint MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public nuint Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct IO_COUNTERS {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public nuint ProcessMemoryLimit;
            public nuint JobMemoryLimit;
            public nuint PeakProcessMemoryUsed;
            public nuint PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateJobObject(IntPtr securityAttributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetInformationJobObject(
            SafeFileHandle job, int informationClass, IntPtr information, uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeHandle process);
    }
}
