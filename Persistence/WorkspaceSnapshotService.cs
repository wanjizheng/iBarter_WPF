using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Xml.Linq;

namespace iBarter.Persistence;

/// <summary>
/// Keeps bounded, operation-level snapshots of the Planner, storage ledger and
/// automatic route. A batch operation may write the same files many times, but
/// only the final, coherent state is captured when the outer batch completes.
/// </summary>
public static class WorkspaceSnapshotService {
    private const int SchemaVersion = 2;
    private const int LegacySchemaVersion = 1;
    private const int MaximumSnapshots = 30;
    private const string SnapshotDirectoryName = "StateSnapshots";
    private const string SnapshotFilePattern = "workspace-state-*.json";

    private static readonly object Gate = new();
    private static int batchDepth;
    private static int suppressDepth;
    private static bool capturePending;
    private static string pendingReason = "state-change";
    private static string? pendingResourcesDirectory;
    private static DateTime? restoreCursorCreatedUtc;

    public static IDisposable BeginBatch(
        string reason,
        string? resourcesDirectory = null) {
        lock (Gate) {
            if (batchDepth == 0) {
                if (suppressDepth == 0) restoreCursorCreatedUtc = null;
                capturePending = true;
                pendingReason = NormalizeReason(reason);
                pendingResourcesDirectory = resourcesDirectory;
            }
            batchDepth++;
        }

        return new SnapshotScope(() => {
            lock (Gate) {
                batchDepth = Math.Max(0, batchDepth - 1);
                if (batchDepth != 0 || !capturePending) return;

                string reasonToCapture = pendingReason;
                string? resourcesToCapture = pendingResourcesDirectory;
                capturePending = false;
                pendingResourcesDirectory = null;
                if (suppressDepth == 0) {
                    _ = CaptureCore(reasonToCapture, resourcesToCapture);
                }
            }
        });
    }

    public static IDisposable SuppressCapture() {
        lock (Gate) {
            suppressDepth++;
        }

        return new SnapshotScope(() => {
            lock (Gate) {
                suppressDepth = Math.Max(0, suppressDepth - 1);
                if (suppressDepth == 0 && batchDepth == 0) {
                    capturePending = false;
                    pendingResourcesDirectory = null;
                }
            }
        });
    }

    /// <summary>
    /// Captures the state after a completed save. Inside a batch this only
    /// marks the batch dirty; the outermost scope writes one final snapshot.
    /// </summary>
    public static bool CaptureCompletedState(
        string reason,
        string? resourcesDirectory = null) {
        lock (Gate) {
            if (suppressDepth > 0) return false;
            restoreCursorCreatedUtc = null;
            if (batchDepth > 0) {
                capturePending = true;
                pendingResourcesDirectory ??= resourcesDirectory;
                return false;
            }
            return CaptureCore(NormalizeReason(reason), resourcesDirectory);
        }
    }

    /// <summary>
    /// Restores the snapshot immediately before the current state. If the
    /// current state is not in history, restores the newest distinct state.
    /// Empty-Planner snapshots are skipped as requested by the undo workflow.
    /// </summary>
    public static WorkspaceSnapshotRestoreResult RestorePreviousNonEmpty(
        string? resourcesDirectory = null) {
        lock (Gate) {
            string resources = ResolveResourcesDirectory(resourcesDirectory);
            WorkspaceState current = ReadCurrentState(resources);
            string snapshotDirectory = Path.Combine(resources, SnapshotDirectoryName);
            if (!Directory.Exists(snapshotDirectory)) {
                return WorkspaceSnapshotRestoreResult.Failed("NO_SNAPSHOT");
            }

            WorkspaceStateSnapshot[] snapshots;
            try {
                snapshots = Directory.GetFiles(snapshotDirectory, SnapshotFilePattern)
                    .Select(TryReadSnapshot)
                    .Where(snapshot => snapshot is not null)
                    .Cast<WorkspaceStateSnapshot>()
                    .OrderByDescending(snapshot => snapshot.CreatedUtc)
                    .ToArray();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException) {
                return WorkspaceSnapshotRestoreResult.Failed(
                    "SNAPSHOT_READ_FAILED: " + exception.Message);
            }

            if (snapshots.Length == 0) {
                return WorkspaceSnapshotRestoreResult.Failed("NO_SNAPSHOT");
            }

            int currentIndex = restoreCursorCreatedUtc.HasValue
                ? Array.FindIndex(
                    snapshots,
                    snapshot => snapshot.CreatedUtc == restoreCursorCreatedUtc.Value)
                : Array.FindIndex(
                    snapshots,
                    snapshot => SnapshotMatchesState(snapshot, current));
            int startIndex = currentIndex >= 0 ? currentIndex + 1 : 0;
            WorkspaceStateSnapshot? selected = snapshots
                .Skip(startIndex)
                .FirstOrDefault(snapshot =>
                    IsRestorable(snapshot.State)
                    && !SnapshotMatchesState(snapshot, current));

            if (selected is null) {
                return WorkspaceSnapshotRestoreResult.Failed("NO_PREVIOUS_NON_EMPTY_SNAPSHOT");
            }

            try {
                ApplyState(resources, selected.State, selected.SchemaVersion);
                restoreCursorCreatedUtc = selected.CreatedUtc;
                return new WorkspaceSnapshotRestoreResult(
                    true,
                    null,
                    selected.CreatedUtc,
                    selected.Reason,
                    CountPlannerRows(selected.State.PlannerJson));
            }
            catch (Exception exception) when (
                exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException) {
                try {
                    ApplyState(resources, current, SchemaVersion);
                }
                catch {
                    // The original error remains the useful diagnostic. Each
                    // individual file write is atomic, and its .bak is retained.
                }
                return WorkspaceSnapshotRestoreResult.Failed(
                    "RESTORE_FAILED: " + exception.Message);
            }
        }
    }

    internal static string GetSnapshotDirectory(string resourcesDirectory) =>
        Path.Combine(Path.GetFullPath(resourcesDirectory), SnapshotDirectoryName);

    private static bool CaptureCore(string reason, string? resourcesDirectory) {
        try {
            string resources = ResolveResourcesDirectory(resourcesDirectory);
            WorkspaceState state = ReadCurrentState(resources);
            if (!IsSnapshotStateValid(state, SchemaVersion)) return false;

            string fingerprint = ComputeFingerprint(state, SchemaVersion);
            string snapshotDirectory = Path.Combine(resources, SnapshotDirectoryName);
            Directory.CreateDirectory(snapshotDirectory);

            WorkspaceStateSnapshot? newest = Directory
                .GetFiles(snapshotDirectory, SnapshotFilePattern)
                .Select(TryReadSnapshot)
                .Where(snapshot => snapshot is not null)
                .Cast<WorkspaceStateSnapshot>()
                .OrderByDescending(snapshot => snapshot.CreatedUtc)
                .FirstOrDefault();
            if (newest is not null && SnapshotMatchesState(newest, state)) {
                return false;
            }

            DateTime createdUtc = DateTime.UtcNow;
            var snapshot = new WorkspaceStateSnapshot(
                SchemaVersion,
                createdUtc,
                reason,
                fingerprint,
                state);
            string json = JsonSerializer.Serialize(snapshot);
            string path = Path.Combine(
                snapshotDirectory,
                $"workspace-state-{createdUtc:yyyyMMdd-HHmmssfffffff}-{Guid.NewGuid():N}.json");
            AtomicFileStore.WriteValidated(path, json, IsValidSnapshotJson);
            PruneSnapshots(snapshotDirectory);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException) {
            System.Diagnostics.Debug.WriteLine(
                $"[WorkspaceSnapshot] capture failed: {exception.Message}");
            return false;
        }
    }

    private static WorkspaceState ReadCurrentState(string resourcesDirectory) =>
        new(
            ReadOptionalFile(Path.Combine(resourcesDirectory, "myPlan_Data.json")) ?? "[]",
            ReadOptionalFile(Path.Combine(resourcesDirectory, "myPlan_Setting.xml")),
            ReadOptionalFile(Path.Combine(resourcesDirectory, "myStorage_Data.json")) ?? "[]",
            ReadOptionalFile(Path.Combine(resourcesDirectory, "automatic-route-plan.json")),
            ReadOptionalFile(Path.Combine(resourcesDirectory, "myShipProperty_Data.json")));

    private static void ApplyState(
        string resourcesDirectory,
        WorkspaceState state,
        int schemaVersion) {
        if (!IsSnapshotStateValid(state, schemaVersion)) {
            throw new InvalidDataException("Snapshot state is invalid.");
        }

        AtomicFileStore.WriteValidated(
            Path.Combine(resourcesDirectory, "myStorage_Data.json"),
            state.StorageJson,
            IsNonEmptyJsonArray);
        AtomicFileStore.WriteValidated(
            Path.Combine(resourcesDirectory, "myPlan_Data.json"),
            state.PlannerJson,
            IsJsonArray);
        RestoreOptionalFile(
            Path.Combine(resourcesDirectory, "myPlan_Setting.xml"),
            state.PlannerSettingsXml,
            IsXml);
        RestoreOptionalFile(
            Path.Combine(resourcesDirectory, "automatic-route-plan.json"),
            state.AutomaticRouteJson,
            IsJsonDocument);
        if (schemaVersion >= SchemaVersion) {
            // The route-plan fingerprint includes ExtraLT and TotalLT. Restore
            // the same cargo-capacity file before the Planner reloads it,
            // otherwise a restored automatic-route JSON can never match the
            // current request. Legacy snapshots intentionally leave the
            // current file untouched because they never captured it.
            RestoreOptionalFile(
                Path.Combine(resourcesDirectory, "myShipProperty_Data.json"),
                state.CargoPropertyJson,
                IsJsonDocument);
        }
    }

    private static void RestoreOptionalFile(
        string path,
        string? content,
        Func<string, bool> validator) {
        if (content is not null) {
            AtomicFileStore.WriteValidated(path, content, validator);
            return;
        }

        DeleteIfPresent(path);
        DeleteIfPresent(path + ".bak");
    }

    private static void DeleteIfPresent(string path) {
        if (File.Exists(path)) File.Delete(path);
    }

    private static WorkspaceStateSnapshot? TryReadSnapshot(string path) {
        try {
            string json = File.ReadAllText(path, Encoding.UTF8);
            var snapshot = JsonSerializer.Deserialize<WorkspaceStateSnapshot>(json);
            if (snapshot is null
                || !IsSupportedSchema(snapshot.SchemaVersion)
                || snapshot.State is null
                || !IsSnapshotStateValid(snapshot.State, snapshot.SchemaVersion)
                || !string.Equals(
                    snapshot.Fingerprint,
                    ComputeFingerprint(snapshot.State, snapshot.SchemaVersion),
                    StringComparison.Ordinal)) {
                return null;
            }
            return snapshot;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or JsonException) {
            return null;
        }
    }

    private static void PruneSnapshots(string snapshotDirectory) {
        string[] files = Directory.GetFiles(snapshotDirectory, SnapshotFilePattern)
            .OrderByDescending(File.GetCreationTimeUtc)
            .ThenByDescending(path => path, StringComparer.Ordinal)
            .ToArray();
        foreach (string path in files.Skip(MaximumSnapshots)) {
            DeleteIfPresent(path);
            DeleteIfPresent(path + ".bak");
        }
    }

    private static bool IsSupportedSchema(int schemaVersion) =>
        schemaVersion is LegacySchemaVersion or SchemaVersion;

    private static bool SnapshotMatchesState(
        WorkspaceStateSnapshot snapshot,
        WorkspaceState state) =>
        string.Equals(
            snapshot.Fingerprint,
            ComputeFingerprint(state, snapshot.SchemaVersion),
            StringComparison.Ordinal);

    private static string ComputeFingerprint(WorkspaceState state, int schemaVersion) {
        using var sha = SHA256.Create();
        var values = new List<string> {
            state.PlannerJson,
            state.PlannerSettingsXml ?? "\u0000",
            state.StorageJson,
            state.AutomaticRouteJson ?? "\u0000",
        };
        if (schemaVersion >= SchemaVersion) {
            values.Add(state.CargoPropertyJson ?? "\u0000");
        }
        string combined = string.Join(
            "\u001e",
            values);
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(combined)));
    }

    private static bool IsSnapshotStateValid(WorkspaceState state, int schemaVersion) =>
        IsJsonArray(state.PlannerJson)
        && IsNonEmptyJsonArray(state.StorageJson)
        && (state.PlannerSettingsXml is null || IsXml(state.PlannerSettingsXml))
        && (state.AutomaticRouteJson is null || IsJsonDocument(state.AutomaticRouteJson))
        && (schemaVersion == LegacySchemaVersion
            || state.CargoPropertyJson is null
            || IsJsonDocument(state.CargoPropertyJson));

    private static bool IsRestorable(WorkspaceState state) =>
        IsSnapshotStateValid(state, SchemaVersion)
        && CountPlannerRows(state.PlannerJson) > 0;

    private static bool IsValidSnapshotJson(string json) {
        try {
            var snapshot = JsonSerializer.Deserialize<WorkspaceStateSnapshot>(json);
            return snapshot is not null
                && IsSupportedSchema(snapshot.SchemaVersion)
                && snapshot.State is not null
                && IsSnapshotStateValid(snapshot.State, snapshot.SchemaVersion)
                && string.Equals(
                    snapshot.Fingerprint,
                    ComputeFingerprint(snapshot.State, snapshot.SchemaVersion),
                    StringComparison.Ordinal);
        }
        catch (JsonException) {
            return false;
        }
    }

    private static bool IsJsonArray(string json) {
        try {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException) {
            return false;
        }
    }

    private static bool IsNonEmptyJsonArray(string json) =>
        CountPlannerRows(json) > 0;

    private static int CountPlannerRows(string json) {
        try {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.GetArrayLength()
                : 0;
        }
        catch (JsonException) {
            return 0;
        }
    }

    private static bool IsJsonDocument(string json) {
        try {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
        }
        catch (JsonException) {
            return false;
        }
    }

    private static bool IsXml(string xml) {
        try {
            _ = XDocument.Parse(xml);
            return true;
        }
        catch {
            return false;
        }
    }

    private static string? ReadOptionalFile(string path) {
        try {
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }
        catch (IOException) {
            return null;
        }
        catch (UnauthorizedAccessException) {
            return null;
        }
    }

    private static string ResolveResourcesDirectory(string? resourcesDirectory) =>
        Path.GetFullPath(resourcesDirectory ?? Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Resources"));

    private static string NormalizeReason(string reason) =>
        string.IsNullOrWhiteSpace(reason) ? "state-change" : reason.Trim();

    private sealed class SnapshotScope(Action onDispose) : IDisposable {
        private Action? disposeAction = onDispose;

        public void Dispose() {
            Interlocked.Exchange(ref disposeAction, null)?.Invoke();
        }
    }

    private sealed record WorkspaceStateSnapshot(
        int SchemaVersion,
        DateTime CreatedUtc,
        string Reason,
        string Fingerprint,
        WorkspaceState State);

    private sealed record WorkspaceState(
        string PlannerJson,
        string? PlannerSettingsXml,
        string StorageJson,
        string? AutomaticRouteJson,
        string? CargoPropertyJson = null);
}

public sealed record WorkspaceSnapshotRestoreResult(
    bool Success,
    string? Error,
    DateTime? SnapshotCreatedUtc,
    string? Reason,
    int PlannerRowCount) {
    internal static WorkspaceSnapshotRestoreResult Failed(string error) =>
        new(false, error, null, null, 0);
}
