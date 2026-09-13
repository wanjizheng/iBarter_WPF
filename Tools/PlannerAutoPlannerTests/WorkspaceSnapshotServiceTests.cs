using iBarter.Persistence;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PlannerAutoPlannerTests;

public sealed class WorkspaceSnapshotServiceTests {
    [Fact]
    public void Batch_captures_once_and_restore_walks_back_through_non_empty_states() {
        using var temp = new SnapshotTempDirectory();
        WriteState(temp.Path, """[{"state":"A"}]""", """[{"ItemID":"1"}]""", """{"route":"A"}""", """{"ExtraLT":1,"TotalLT":100}""");
        Assert.True(WorkspaceSnapshotService.CaptureCompletedState("baseline", temp.Path));

        using (WorkspaceSnapshotService.BeginBatch("auto-plan", temp.Path)) {
            WriteState(temp.Path, """[{"state":"B1"}]""", """[{"ItemID":"2"}]""", """{"route":"B1"}""", """{"ExtraLT":2,"TotalLT":200}""");
            WorkspaceSnapshotService.CaptureCompletedState("cell-1", temp.Path);
            WriteState(temp.Path, """[{"state":"B"}]""", """[{"ItemID":"2"}]""", """{"route":"B"}""", """{"ExtraLT":2,"TotalLT":200}""");
            WorkspaceSnapshotService.CaptureCompletedState("cell-2", temp.Path);
        }

        Assert.Equal(2, SnapshotFiles(temp.Path).Length);

        WriteState(temp.Path, "[]", """[{"ItemID":"3"}]""", """{"route":"done"}""", """{"ExtraLT":3,"TotalLT":300}""");
        Assert.True(WorkspaceSnapshotService.CaptureCompletedState("done", temp.Path));

        WorkspaceSnapshotRestoreResult first =
            WorkspaceSnapshotService.RestorePreviousNonEmpty(temp.Path);
        Assert.True(first.Success, first.Error);
        Assert.Equal("""[{"state":"B"}]""", Read(temp.Path, "myPlan_Data.json"));
        Assert.Equal("""[{"ItemID":"2"}]""", Read(temp.Path, "myStorage_Data.json"));
        Assert.Equal("""{"route":"B"}""", Read(temp.Path, "automatic-route-plan.json"));
        Assert.Equal("""{"ExtraLT":2,"TotalLT":200}""", Read(temp.Path, "myShipProperty_Data.json"));

        // Route restore may normalize/rewrite its JSON. The history cursor must
        // still continue backward rather than selecting B again as "different".
        File.WriteAllText(
            Path.Combine(temp.Path, "automatic-route-plan.json"),
            """{"route":"B-normalized"}""");
        WorkspaceSnapshotRestoreResult second =
            WorkspaceSnapshotService.RestorePreviousNonEmpty(temp.Path);
        Assert.True(second.Success, second.Error);
        Assert.Equal("""[{"state":"A"}]""", Read(temp.Path, "myPlan_Data.json"));
        Assert.Equal("""[{"ItemID":"1"}]""", Read(temp.Path, "myStorage_Data.json"));
        Assert.Equal("""{"route":"A"}""", Read(temp.Path, "automatic-route-plan.json"));
        Assert.Equal("""{"ExtraLT":1,"TotalLT":100}""", Read(temp.Path, "myShipProperty_Data.json"));
    }

    [Fact]
    public void Legacy_snapshot_restores_without_deleting_the_current_cargo_configuration() {
        using var temp = new SnapshotTempDirectory();
        WriteState(temp.Path, """[{"state":"current"}]""", """[{"ItemID":"current"}]""", """{"route":"current"}""", """{"ExtraLT":9,"TotalLT":900}""");

        string planner = """[{"state":"legacy"}]""";
        string storage = """[{"ItemID":"legacy"}]""";
        string route = """{"route":"legacy"}""";
        string combined = string.Join("\u001e", planner, "\u0000", storage, route);
        string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(combined)));
        var legacy = new {
            SchemaVersion = 1,
            CreatedUtc = DateTime.UtcNow,
            Reason = "legacy",
            Fingerprint = fingerprint,
            State = new {
                PlannerJson = planner,
                PlannerSettingsXml = (string?)null,
                StorageJson = storage,
                AutomaticRouteJson = route,
            },
        };
        string directory = WorkspaceSnapshotService.GetSnapshotDirectory(temp.Path);
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "workspace-state-legacy.json"),
            JsonSerializer.Serialize(legacy));

        WorkspaceSnapshotRestoreResult restore = WorkspaceSnapshotService.RestorePreviousNonEmpty(temp.Path);

        Assert.True(restore.Success, restore.Error);
        Assert.Equal(planner, Read(temp.Path, "myPlan_Data.json"));
        Assert.Equal(route, Read(temp.Path, "automatic-route-plan.json"));
        Assert.Equal("""{"ExtraLT":9,"TotalLT":900}""", Read(temp.Path, "myShipProperty_Data.json"));
    }

    private static void WriteState(
        string directory,
        string planner,
        string storage,
        string route,
        string cargoProperty) {
        File.WriteAllText(Path.Combine(directory, "myPlan_Data.json"), planner);
        File.WriteAllText(Path.Combine(directory, "myStorage_Data.json"), storage);
        File.WriteAllText(Path.Combine(directory, "automatic-route-plan.json"), route);
        File.WriteAllText(Path.Combine(directory, "myShipProperty_Data.json"), cargoProperty);
    }

    private static string Read(string directory, string fileName) =>
        File.ReadAllText(Path.Combine(directory, fileName));

    private static string[] SnapshotFiles(string directory) =>
        Directory.GetFiles(
            WorkspaceSnapshotService.GetSnapshotDirectory(directory),
            "workspace-state-*.json");

    private sealed class SnapshotTempDirectory : IDisposable {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "iBarter-snapshot-tests-" + Guid.NewGuid().ToString("N"));

        public SnapshotTempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose() {
            try {
                Directory.Delete(Path, recursive: true);
            }
            catch {
            }
        }
    }
}
