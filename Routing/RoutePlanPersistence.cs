namespace iBarter.Routing;

using System.Text.Json;
using System.IO;

public sealed record PersistedRoutePlan(
    RoutePlan Plan,
    int? SelectedRouteNumber,
    bool ShowAll,
    string? SelectedBarterRowId);

public static class RoutePlanPersistence {
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static void Save(
        string path,
        RoutePlan plan,
        int? selectedRouteNumber,
        bool showAll,
        string? selectedBarterRowId = null) {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string temp = path + ".tmp";
        var dto = ToDto(plan, selectedRouteNumber, showAll, selectedBarterRowId);
        File.WriteAllText(temp, JsonSerializer.Serialize(dto, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    public static bool TryLoad(
        string path,
        string expectedFingerprint,
        out PersistedRoutePlan? snapshot) {
        if (!TryRead(path, out snapshot) || snapshot is null
            || !StringComparer.Ordinal.Equals(snapshot.Plan.InputFingerprint, expectedFingerprint)) {
            snapshot = null;
            return false;
        }
        return true;
    }

    public static bool TryLoadAfterProgress(
        string path,
        AutomaticRoutePlanningRequest currentRequest,
        IReadOnlySet<string> completedBarterRowIds,
        out PersistedRoutePlan? snapshot) {
        if (!TryRead(path, out snapshot) || snapshot is null
            || !RoutePlanRestoreCompatibility.IsCompatibleAfterProgress(
                currentRequest, snapshot.Plan, completedBarterRowIds)) {
            snapshot = null;
            return false;
        }
        return true;
    }

    private static bool TryRead(string path, out PersistedRoutePlan? snapshot) {
        snapshot = null;
        try {
            if (!File.Exists(path)) return false;
            var dto = JsonSerializer.Deserialize<EnvelopeDto>(File.ReadAllText(path), JsonOptions);
            if (dto is null || dto.SchemaVersion != SchemaVersion) return false;
            var plan = FromDto(dto);
            int? selected = plan.Routes.Any(x => x.Number == dto.SelectedRouteNumber)
                ? dto.SelectedRouteNumber
                : plan.Routes.FirstOrDefault()?.Number;
            snapshot = new PersistedRoutePlan(
                plan,
                selected,
                dto.ShowAll && plan.Routes.Count > 0,
                dto.SelectedBarterRowId);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) {
            return false;
        }
    }

    private static EnvelopeDto ToDto(
        RoutePlan plan,
        int? selectedRouteNumber,
        bool showAll,
        string? selectedBarterRowId) => new(
        SchemaVersion,
        plan.InputFingerprint,
        plan.Status,
        plan.Objective,
        plan.Diagnostics.ToArray(),
        plan.Routes.Select(route => new RouteDto(
            route.Number,
            route.StartWarehouseId,
            route.EndWarehouseId,
            route.Steps.Select(ToDto).ToArray(),
            route.Distance,
            route.InitialLT,
            route.CurrentLT,
            route.PeakLT)).ToArray(),
        selectedRouteNumber,
        showAll,
        selectedBarterRowId);

    private static StepDto ToDto(RouteStep step) => step switch {
        WarehousePickupStep pickup => new(
            "pickup", pickup.IslandId, pickup.Load, pickup.WarehouseId, "",
            pickup.Items.ToArray(), null, null),
        WarehouseUnloadStep unload => new(
            "unload", unload.IslandId, unload.Load, unload.WarehouseId, "",
            unload.Items.ToArray(), null, null),
        BarterStep barter => new(
            "barter", barter.IslandId, barter.Load, "", barter.RowId,
            [], barter.Consumed, barter.Produced),
        _ => throw new InvalidDataException($"Unsupported route step '{step.GetType().Name}'."),
    };

    private static RoutePlan FromDto(EnvelopeDto dto) {
        var routes = dto.Routes.Select(route => new PlannedRoute(
            route.Number,
            route.StartWarehouseId,
            route.EndWarehouseId,
            route.Steps.Select(FromDto).ToArray(),
            route.Distance,
            route.InitialLT,
            route.CurrentLT,
            route.PeakLT)).ToArray();
        return new RoutePlan(
            dto.Status,
            routes,
            dto.Objective,
            dto.Diagnostics ?? [],
            dto.InputFingerprint);
    }

    private static RouteStep FromDto(StepDto step) => step.Kind switch {
        "pickup" => new WarehousePickupStep(
            step.WarehouseId, step.IslandId, step.Items ?? [], step.Load),
        "unload" => new WarehouseUnloadStep(
            step.WarehouseId, step.IslandId, step.Items ?? [], step.Load),
        "barter" when step.Consumed is not null && step.Produced is not null => new BarterStep(
            step.RowId, step.IslandId, step.Consumed, step.Produced, step.Load),
        _ => throw new InvalidDataException($"Unsupported persisted route step '{step.Kind}'."),
    };

    private sealed record EnvelopeDto(
        int SchemaVersion,
        string InputFingerprint,
        RoutePlanStatus Status,
        RoutePlanObjective? Objective,
        RouteDiagnostic[]? Diagnostics,
        RouteDto[] Routes,
        int? SelectedRouteNumber,
        bool ShowAll,
        string? SelectedBarterRowId = null);

    private sealed record RouteDto(
        int Number,
        string StartWarehouseId,
        string EndWarehouseId,
        StepDto[] Steps,
        double Distance,
        int InitialLT,
        int CurrentLT,
        int PeakLT);

    private sealed record StepDto(
        string Kind,
        string IslandId,
        RouteLoadSnapshot Load,
        string WarehouseId,
        string RowId,
        RouteItemQuantity[]? Items,
        RouteItemQuantity? Consumed,
        RouteItemQuantity? Produced);
}
