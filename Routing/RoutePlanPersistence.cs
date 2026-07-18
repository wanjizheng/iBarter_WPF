namespace iBarter.Routing;

using System.Text.Json;
using System.IO;

public sealed record PersistedRoutePlan(
    RoutePlan Plan,
    int? SelectedRouteNumber,
    bool ShowAll,
    string? SelectedBarterRowId,
    bool ShowRouteGuides = true);

/// <summary>
/// Discriminated result of an attempt to load a persisted route plan.
/// Each value tells the caller exactly why a load failed so the UI can
/// produce a useful diagnostic instead of swallowing every failure as
/// the same silent "false".
/// </summary>
public enum RoutePlanLoadStatus {
    Loaded,
    FileNotFound,
    UnsupportedSchema,
    CorruptFile,
    FingerprintMismatch,
}

/// <summary>
/// Full result of <see cref="RoutePlanPersistence.TryLoad"/>. Carries the
/// loaded snapshot on success and enough metadata on failure to log a
/// useful diagnostic (which mode was saved, which fingerprint was expected).
/// </summary>
public sealed record RoutePlanLoadResult(
    RoutePlanLoadStatus Status,
    PersistedRoutePlan? Snapshot = null,
    int SchemaVersion = 0,
    string? SavedFingerprint = null,
    string? ExpectedFingerprint = null,
    RouteOptimizationMode? SavedOptimizationMode = null,
    DateTimeOffset? SavedAtUtc = null);

public static class RoutePlanPersistence {
    /// <summary>
    /// Bumped from 1 to 2 when <see cref="OptimizationMode"/> and
    /// <see cref="SavedAtUtc"/> were added to the envelope. v1 files are
    /// still readable: the missing fields deserialize to null and the
    /// caller is expected to try each profile until one matches.
    /// </summary>
    public const int SchemaVersion = 2;
    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static void Save(
        string path,
        RoutePlan plan,
        int? selectedRouteNumber,
        bool showAll,
        string? selectedBarterRowId = null,
        RouteOptimizationMode? optimizationMode = null,
        DateTimeOffset? savedAtUtc = null,
        bool showRouteGuides = true) {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string temp = path + ".tmp";
        var dto = ToDto(plan, selectedRouteNumber, showAll, selectedBarterRowId,
            optimizationMode, savedAtUtc ?? DateTimeOffset.UtcNow, showRouteGuides);
        File.WriteAllText(temp, JsonSerializer.Serialize(dto, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    public static RoutePlanLoadResult TryLoad(
        string path,
        string expectedFingerprint) {
        if (!TryReadEnvelope(path, out var dto, out _)) {
            // Distinguish file-missing from corrupt: TryReadEnvelope returns
            // false in both cases, so check File.Exists to set the right status.
            if (!File.Exists(path)) {
                return new RoutePlanLoadResult(
                    RoutePlanLoadStatus.FileNotFound,
                    ExpectedFingerprint: expectedFingerprint);
            }
            return new RoutePlanLoadResult(
                RoutePlanLoadStatus.CorruptFile,
                ExpectedFingerprint: expectedFingerprint);
        }

        if (dto!.SchemaVersion != SchemaVersion && dto.SchemaVersion != 1) {
            // 1 is the legacy schema we still read; anything else is unsupported.
            return new RoutePlanLoadResult(
                RoutePlanLoadStatus.UnsupportedSchema,
                SchemaVersion: dto.SchemaVersion,
                ExpectedFingerprint: expectedFingerprint);
        }

        if (!StringComparer.Ordinal.Equals(dto.InputFingerprint, expectedFingerprint)) {
            return new RoutePlanLoadResult(
                RoutePlanLoadStatus.FingerprintMismatch,
                SchemaVersion: dto.SchemaVersion,
                SavedFingerprint: dto.InputFingerprint,
                ExpectedFingerprint: expectedFingerprint,
                SavedOptimizationMode: dto.OptimizationMode,
                SavedAtUtc: dto.SavedAtUtc);
        }

        try {
            var plan = FromDto(dto);
            int? selected = plan.Routes.Any(x => x.Number == dto.SelectedRouteNumber)
                ? dto.SelectedRouteNumber
                : plan.Routes.FirstOrDefault()?.Number;
            var snapshot = new PersistedRoutePlan(
                plan,
                selected,
                dto.ShowAll && plan.Routes.Count > 0,
                dto.SelectedBarterRowId,
                dto.ShowRouteGuides ?? true);
            return new RoutePlanLoadResult(
                RoutePlanLoadStatus.Loaded,
                snapshot,
                dto.SchemaVersion,
                dto.InputFingerprint,
                expectedFingerprint,
                dto.OptimizationMode,
                dto.SavedAtUtc);
        }
        catch (Exception ex) when (ex is InvalidDataException
                                       or IOException or UnauthorizedAccessException) {
            return new RoutePlanLoadResult(
                RoutePlanLoadStatus.CorruptFile,
                SchemaVersion: dto.SchemaVersion,
                SavedFingerprint: dto.InputFingerprint,
                ExpectedFingerprint: expectedFingerprint,
                SavedOptimizationMode: dto.OptimizationMode,
                SavedAtUtc: dto.SavedAtUtc);
        }
    }

    public static bool TryLoadAfterProgress(
        string path,
        AutomaticRoutePlanningRequest currentRequest,
        IReadOnlySet<string> completedBarterRowIds,
        out PersistedRoutePlan? snapshot) {
        if (!File.Exists(path)) {
            snapshot = null;
            return false;
        }
        if (!TryRead(path, out snapshot) || snapshot is null) {
            snapshot = null;
            return false;
        }
        // Audit round 7: with-CK restore.  If the user has marked
        // any barters done, the strict progress path is the right
        // one — every saved RowId must match a current task.
        if (completedBarterRowIds.Count > 0) {
            if (!RoutePlanRestoreCompatibility.IsCompatibleAfterProgress(
                    currentRequest, snapshot.Plan, completedBarterRowIds)) {
                snapshot = null;
                return false;
            }
            return true;
        }
        // Audit round 7: no-CK restore.  The strict progress path
        // would fail because no saved RowId is in the (empty) CK
        // set; instead, try identity migration.  The persisted
        // plan's BarterStep.RowIds may be legacy business-tuple
        // strings ("Iliya:800208:800241") or "br-*" GUIDs from a
        // prior migration run.
        var identity = RoutePlanRestoreCompatibility.TryMigrateIdentityOnly(
            currentRequest, snapshot.Plan, snapshot.SelectedBarterRowId);
        if (identity.IsCompatible && identity.MappedPlan is not null) {
            // Audit round 8: verify the migrated plan via
            // RoutePlanVerifier before publishing. If the
            // verifier fails, the migration produced a plan that
            // the planner cannot accept — refuse the restore, do
            // not save back, do not return a snapshot.
            var verification = RoutePlanVerifier.Verify(
                currentRequest, identity.MappedPlan);
            if (!verification.Success || verification.VerifiedPlan is null) {
                System.Diagnostics.Debug.WriteLine(
                    $"[RoutePlan] identity migration refused: verifier failure: " +
                    $"{verification.Diagnostic?.Code} " +
                    $"{verification.Diagnostic?.Detail}");
                snapshot = null;
                return false;
            }
            // Re-host the snapshot on the migrated plan so callers
            // see the new RowIds; also save back to disk so the
            // next reload doesn't re-migrate.
            string migratedSelected = identity.MigratedSelectedBarterRowId
                ?? snapshot.SelectedBarterRowId;
            snapshot = new PersistedRoutePlan(
                identity.MappedPlan,
                snapshot.SelectedRouteNumber,
                snapshot.ShowAll,
                migratedSelected,
                snapshot.ShowRouteGuides);
            try {
                Save(path, identity.MappedPlan,
                    snapshot.SelectedRouteNumber, snapshot.ShowAll,
                    migratedSelected,
                    showRouteGuides: snapshot.ShowRouteGuides);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                // The restore itself succeeded; the save-back is
                // best-effort.  Log so the user can save manually.
                System.Diagnostics.Debug.WriteLine(
                    $"[RoutePlan] identity migration save-back failed: {ex.GetType().Name}: {ex.Message}");
            }
            return true;
        }
        // Log the precise mismatch so the user knows what to fix.
        if (identity.AmbiguousTuples is { Count: > 0 }) {
            System.Diagnostics.Debug.WriteLine(
                $"[RoutePlan] identity migration refused: " +
                $"{identity.AmbiguousTuples.Count} ambiguous business tuples");
        }
        if (identity.MismatchComponents is { Count: > 0 }) {
            System.Diagnostics.Debug.WriteLine(
                $"[RoutePlan] identity migration mismatch: " +
                string.Join("; ", identity.MismatchComponents));
        }
        snapshot = null;
        return false;
    }

    private static bool TryRead(string path, out PersistedRoutePlan? snapshot) {
        // Progress-restore helper: reads the envelope without validating the
        // fingerprint. The caller (RoutePlanRestoreCompatibility) does its own
        // row-id + warehouse + LT comparison, so we must not reject the file
        // just because the saved fingerprint no longer matches the current
        // request's fingerprint.
        snapshot = null;
        if (!TryReadEnvelope(path, out var dto, out _)) return false;
        if (dto is null) return false;
        if (dto.SchemaVersion != SchemaVersion && dto.SchemaVersion != 1) return false;
        try {
            var plan = FromDto(dto);
            int? selected = plan.Routes.Any(x => x.Number == dto.SelectedRouteNumber)
                ? dto.SelectedRouteNumber
                : plan.Routes.FirstOrDefault()?.Number;
            snapshot = new PersistedRoutePlan(
                plan,
                selected,
                dto.ShowAll && plan.Routes.Count > 0,
                dto.SelectedBarterRowId,
                dto.ShowRouteGuides ?? true);
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException
                                       or IOException or UnauthorizedAccessException) {
            return false;
        }
    }

    /// <summary>
    /// Lightweight metadata peek used by the restore flow to discover which
    /// mode was used to generate the saved plan. Does not validate the
    /// fingerprint - the caller supplies the expected fingerprint and calls
    /// <see cref="TryLoad"/> afterwards.
    /// </summary>
    public static bool TryReadMetadata(
        string path,
        out int schemaVersion,
        out string? savedFingerprint,
        out RouteOptimizationMode? savedOptimizationMode,
        out DateTimeOffset? savedAtUtc) {
        schemaVersion = 0;
        savedFingerprint = null;
        savedOptimizationMode = null;
        savedAtUtc = null;
        if (!TryReadEnvelope(path, out var dto, out _)) return false;
        if (dto is null) return false;
        schemaVersion = dto.SchemaVersion;
        savedFingerprint = dto.InputFingerprint;
        savedOptimizationMode = dto.OptimizationMode;
        savedAtUtc = dto.SavedAtUtc;
        return true;
    }

    private static bool TryReadEnvelope(
        string path, out EnvelopeDto? dto, out string rawContent) {
        dto = null;
        rawContent = string.Empty;
        if (!File.Exists(path)) return false;
        try {
            rawContent = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return false;
        }
        try {
            dto = JsonSerializer.Deserialize<EnvelopeDto>(rawContent, JsonOptions);
            return dto is not null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException
                                       or IOException or UnauthorizedAccessException) {
            dto = null;
            return false;
        }
    }

    private static EnvelopeDto ToDto(
        RoutePlan plan,
        int? selectedRouteNumber,
        bool showAll,
        string? selectedBarterRowId,
        RouteOptimizationMode? optimizationMode,
        DateTimeOffset savedAtUtc,
        bool showRouteGuides) => new(
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
        selectedBarterRowId,
        optimizationMode,
        savedAtUtc,
        showRouteGuides);

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
        string? SelectedBarterRowId = null,
        RouteOptimizationMode? OptimizationMode = null,
        DateTimeOffset? SavedAtUtc = null,
        bool? ShowRouteGuides = null);

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
