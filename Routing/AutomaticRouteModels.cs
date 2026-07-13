using System.Collections.ObjectModel;

namespace iBarter.Routing;

public readonly record struct RoutePoint(double X, double Y) {
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
}

public sealed record RouteItem(string ItemId, string DisplayName, int Level, int UnitWeight);

public sealed record RouteItemQuantity(string ItemId, int Quantity);

public sealed record RouteWarehouse {
    public string WarehouseId { get; init; }
    public string IslandId { get; init; }
    public RoutePoint Point { get; init; }
    public IReadOnlyDictionary<string, int> Inventory { get; }

    public RouteWarehouse(
        string warehouseId,
        string islandId,
        RoutePoint point,
        IReadOnlyDictionary<string, int> inventory) {
        WarehouseId = warehouseId;
        IslandId = islandId;
        Point = point;
        Inventory = ModelCopies.Dictionary(inventory);
    }
}

public sealed record RouteBarterTask(
    string RowId,
    string IslandId,
    RoutePoint Point,
    string Item1Id,
    int InputQuantity,
    string Item2Id,
    int OutputQuantity);

public sealed record RouteSearchLimits(int MaxExpandedStates, int MaxLocalMoves);

public sealed class AutomaticRoutePlanningRequest {
    public IReadOnlyList<RouteBarterTask> Tasks { get; }
    public IReadOnlyDictionary<string, RouteItem> Items { get; }
    public IReadOnlyList<RouteWarehouse> Warehouses { get; }
    public int ExtraLT { get; }
    public int TotalLT { get; }
    public RouteSearchLimits Limits { get; }
    public string ConfigurationVersion { get; }

    public AutomaticRoutePlanningRequest(
        IReadOnlyList<RouteBarterTask> tasks,
        IReadOnlyDictionary<string, RouteItem> items,
        IReadOnlyList<RouteWarehouse> warehouses,
        int extraLT,
        int totalLT,
        RouteSearchLimits limits,
        string configurationVersion) {
        Tasks = ModelCopies.List(tasks);
        Items = ModelCopies.Dictionary(items);
        Warehouses = ModelCopies.List(warehouses.Select(warehouse => new RouteWarehouse(
            warehouse.WarehouseId,
            warehouse.IslandId,
            warehouse.Point,
            warehouse.Inventory)));
        ExtraLT = extraLT;
        TotalLT = totalLT;
        Limits = limits;
        ConfigurationVersion = configurationVersion;
    }
}

public enum RoutePlanStatus {
    Optimal,
    BestKnownWithinLimit,
    Infeasible,
    NoFeasibleSolutionWithinLimit,
    Cancelled,
    InvalidInput,
}

public sealed record RouteDiagnostic(
    string Code,
    string RowId = "",
    string ItemId = "",
    string Detail = "");

public sealed record RouteLoadSnapshot(int CargoLT, int TotalWithExtraLT, int PeakTotalLT);

public abstract record RouteStep {
    public string IslandId { get; init; }
    public RouteLoadSnapshot Load { get; init; }

    protected RouteStep(string islandId, RouteLoadSnapshot load) {
        IslandId = islandId;
        Load = load;
    }
}

public sealed record WarehousePickupStep : RouteStep {
    public string WarehouseId { get; init; }
    public IReadOnlyList<RouteItemQuantity> Items { get; }

    public WarehousePickupStep(
        string warehouseId,
        string islandId,
        IReadOnlyList<RouteItemQuantity> items,
        RouteLoadSnapshot load) : base(islandId, load) {
        WarehouseId = warehouseId;
        Items = ModelCopies.List(items);
    }
}

public sealed record BarterStep : RouteStep {
    public string RowId { get; init; }
    public RouteItemQuantity Consumed { get; init; }
    public RouteItemQuantity Produced { get; init; }

    public BarterStep(
        string rowId,
        string islandId,
        RouteItemQuantity consumed,
        RouteItemQuantity produced,
        RouteLoadSnapshot load) : base(islandId, load) {
        RowId = rowId;
        Consumed = consumed;
        Produced = produced;
    }
}

public sealed record WarehouseUnloadStep : RouteStep {
    public string WarehouseId { get; init; }
    public IReadOnlyList<RouteItemQuantity> Items { get; }

    public WarehouseUnloadStep(
        string warehouseId,
        string islandId,
        IReadOnlyList<RouteItemQuantity> items,
        RouteLoadSnapshot load) : base(islandId, load) {
        WarehouseId = warehouseId;
        Items = ModelCopies.List(items);
    }
}

public sealed class PlannedRoute {
    public int Number { get; }
    public string StartWarehouseId { get; }
    public string EndWarehouseId { get; }
    public IReadOnlyList<RouteStep> Steps { get; }
    public double Distance { get; }
    public int InitialLT { get; }
    public int CurrentLT { get; }
    public int PeakLT { get; }

    public PlannedRoute(
        int number,
        string startWarehouseId,
        string endWarehouseId,
        IReadOnlyList<RouteStep> steps,
        double distance,
        int initialLT,
        int currentLT,
        int peakLT) {
        Number = number;
        StartWarehouseId = startWarehouseId;
        EndWarehouseId = endWarehouseId;
        Steps = ModelCopies.List(steps);
        Distance = distance;
        InitialLT = initialLT;
        CurrentLT = currentLT;
        PeakLT = peakLT;
    }
}

public readonly record struct RoutePlanObjective(
    int RouteCount,
    double TotalDistance,
    int PickupStopCount,
    int MaxPeakLT,
    string StableTieBreak) : IComparable<RoutePlanObjective> {
    public int CompareTo(RoutePlanObjective other) {
        int result = RouteCount.CompareTo(other.RouteCount);
        if (result != 0) return result;

        result = TotalDistance.CompareTo(other.TotalDistance);
        if (result != 0) return result;

        result = PickupStopCount.CompareTo(other.PickupStopCount);
        if (result != 0) return result;

        result = MaxPeakLT.CompareTo(other.MaxPeakLT);
        if (result != 0) return result;

        return StringComparer.Ordinal.Compare(StableTieBreak, other.StableTieBreak);
    }
}

public sealed class RoutePlan {
    public RoutePlanStatus Status { get; }
    public IReadOnlyList<PlannedRoute> Routes { get; }
    public RoutePlanObjective? Objective { get; }
    public IReadOnlyList<RouteDiagnostic> Diagnostics { get; }
    public string InputFingerprint { get; }

    public RoutePlan(
        RoutePlanStatus status,
        IReadOnlyList<PlannedRoute> routes,
        RoutePlanObjective? objective,
        IReadOnlyList<RouteDiagnostic> diagnostics,
        string inputFingerprint) {
        Status = status;
        Routes = ModelCopies.List(routes);
        Objective = objective;
        Diagnostics = ModelCopies.List(diagnostics);
        InputFingerprint = inputFingerprint;
    }
}

internal static class ModelCopies {
    public static IReadOnlyList<T> List<T>(IEnumerable<T> source) =>
        Array.AsReadOnly(source.ToArray());

    public static IReadOnlyDictionary<TKey, TValue> Dictionary<TKey, TValue>(
        IEnumerable<KeyValuePair<TKey, TValue>> source)
        where TKey : notnull =>
        new ReadOnlyDictionary<TKey, TValue>(source.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            EqualityComparer<TKey>.Default));
}
