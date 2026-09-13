namespace iBarter.Routing;

public static class ExtremeRouteSolverProtocol {
    public const int Version = 1;
    public const long DistanceScale = 1_000;
    public const int DefaultTimeLimitSeconds = 600;
    public const int DefaultNoImprovementTimeoutSeconds = 90;
    public const int ProcessPollMilliseconds = 100;
    public const int GracefulStopTimeoutSeconds = 5;
    public const int MaximumTasks = 32;

    public static readonly TimeSpan ExtremeMaxSearchDuration =
        TimeSpan.FromSeconds(DefaultTimeLimitSeconds);
    public static readonly TimeSpan ExtremeNoImprovementTimeout =
        TimeSpan.FromSeconds(DefaultNoImprovementTimeoutSeconds);
}

public sealed record ExtremeSolverItemDto(
    string ItemId,
    int UnitWeight,
    long QuantityUpperBound);

public sealed record ExtremeSolverWarehouseDto(
    string WarehouseId,
    string IslandId,
    IReadOnlyDictionary<string, int> Inventory);

public sealed record ExtremeSolverTaskDto(
    int Index,
    string RowId,
    string IslandId,
    string Item1Id,
    int InputQuantity,
    string Item2Id,
    int OutputQuantity);

public sealed record ExtremeSolverInputDto(
    int ProtocolVersion,
    int TimeLimitSeconds,
    int MemoryLimitMb,
    int WorkerCount,
    int MaxRoutes,
    int CargoCapacityLT,
    IReadOnlyList<ExtremeSolverItemDto> Items,
    IReadOnlyList<ExtremeSolverWarehouseDto> Warehouses,
    IReadOnlyList<ExtremeSolverTaskDto> Tasks,
    IReadOnlyDictionary<string, int> InitialOnBoard,
    IReadOnlyList<ExtremeSolverRouteDto> InitialRoutes,
    IReadOnlyList<IReadOnlyList<long>> TaskToTaskDistance,
    IReadOnlyList<IReadOnlyList<long>> WarehouseToTaskDistance,
    IReadOnlyList<IReadOnlyList<long>> TaskToWarehouseDistance,
    IReadOnlyList<IReadOnlyList<long>> WarehouseToWarehouseDistance);

public sealed record ExtremeSolverPickupDto(string ItemId, int Quantity);

public sealed record ExtremeSolverRouteDto(
    int Number,
    string? StartWarehouseId,
    string EndWarehouseId,
    IReadOnlyList<ExtremeSolverPickupDto> Pickup,
    IReadOnlyList<int> TaskIndices);

public sealed record ExtremeSolverOutputDto(
    int ProtocolVersion,
    string Status,
    int RouteLimit,
    long BestDistanceScaled,
    long BestBoundScaled,
    double RelativeGap,
    double WallTimeSeconds,
    long Conflicts,
    long Branches,
    IReadOnlyList<ExtremeSolverRouteDto> Routes,
    int WorkerCount,
    int MemoryLimitMb,
    string? Error = null);
