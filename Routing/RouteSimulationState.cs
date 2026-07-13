using System.Collections.ObjectModel;

namespace iBarter.Routing;

public sealed class RouteSimulationState {
    public string CurrentIslandId { get; }
    public int CurrentRouteNumber { get; }
    public ulong CompletedMask { get; }
    public IReadOnlySet<string> VisitedWarehouseIds { get; }
    public IReadOnlyDictionary<string, int> OnBoard { get; }
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> WarehouseInventory { get; }
    public int CargoLT { get; }
    public int CurrentRoutePeakLT { get; }
    public IReadOnlyList<RouteStep> CurrentRouteSteps { get; }
    public IReadOnlyList<PlannedRoute> FinishedRoutes { get; }
    public double TotalDistance { get; }
    public int PickupStopCount { get; }

    internal RouteSimulationState(
        string currentIslandId,
        int currentRouteNumber,
        ulong completedMask,
        IEnumerable<string> visitedWarehouseIds,
        IEnumerable<KeyValuePair<string, int>> onBoard,
        IEnumerable<KeyValuePair<string, IReadOnlyDictionary<string, int>>> warehouseInventory,
        int cargoLT,
        int currentRoutePeakLT,
        IEnumerable<RouteStep> currentRouteSteps,
        IEnumerable<PlannedRoute> finishedRoutes,
        double totalDistance,
        int pickupStopCount) {
        CurrentIslandId = currentIslandId;
        CurrentRouteNumber = currentRouteNumber;
        CompletedMask = completedMask;
        VisitedWarehouseIds = new ReadOnlySet<string>(new HashSet<string>(visitedWarehouseIds, StringComparer.Ordinal));
        OnBoard = ModelCopies.Dictionary(onBoard);
        WarehouseInventory = CopyWarehouses(warehouseInventory);
        CargoLT = cargoLT;
        CurrentRoutePeakLT = currentRoutePeakLT;
        CurrentRouteSteps = ModelCopies.List(currentRouteSteps);
        FinishedRoutes = ModelCopies.List(finishedRoutes);
        TotalDistance = totalDistance;
        PickupStopCount = pickupStopCount;
    }

    public static RouteSimulationState CreateInitial(AutomaticRoutePlanningRequest request) {
        var inventory = request.Warehouses.Select(warehouse =>
            new KeyValuePair<string, IReadOnlyDictionary<string, int>>(
                warehouse.WarehouseId,
                ModelCopies.Dictionary(warehouse.Inventory)));
        return new RouteSimulationState(
            "", 1, 0, [], [], inventory, 0, request.ExtraLT,
            [], [], 0, 0);
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> CopyWarehouses(
        IEnumerable<KeyValuePair<string, IReadOnlyDictionary<string, int>>> source) {
        var copy = source.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyDictionary<string, int>)ModelCopies.Dictionary(pair.Value),
            StringComparer.Ordinal);
        return new ReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>(copy);
    }
}

internal sealed class ReadOnlySet<T> : IReadOnlySet<T> {
    private readonly HashSet<T> values;

    public ReadOnlySet(HashSet<T> values) => this.values = values;
    public int Count => values.Count;
    public bool Contains(T item) => values.Contains(item);
    public bool IsProperSubsetOf(IEnumerable<T> other) => values.IsProperSubsetOf(other);
    public bool IsProperSupersetOf(IEnumerable<T> other) => values.IsProperSupersetOf(other);
    public bool IsSubsetOf(IEnumerable<T> other) => values.IsSubsetOf(other);
    public bool IsSupersetOf(IEnumerable<T> other) => values.IsSupersetOf(other);
    public bool Overlaps(IEnumerable<T> other) => values.Overlaps(other);
    public bool SetEquals(IEnumerable<T> other) => values.SetEquals(other);
    public IEnumerator<T> GetEnumerator() => values.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
