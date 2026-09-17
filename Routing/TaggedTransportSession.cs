using System.IO;
using System.Text.Json;

namespace iBarter.Routing;

public sealed record TaggedTransportSession(int Version, TaggedTransportRequest Request, TaggedTransportPlan Plan, int CompletedSteps,
    TaggedTransportRequest? SettlementBaseline = null, bool Settled = false, bool MapPlanCompleted = false,
    TaggedTransportRequest? WorkspaceInputs = null, int? SelectedRouteNumber = null) {
    public TaggedTransportState Current() {
        var sim = new TaggedTransportSimulator(Request);
        if (!sim.Verify(Plan, out _, out string error) || !sim.Verify(Plan, out var state, out error, CompletedSteps))
            throw new InvalidDataException(error);
        return state;
    }
    public TaggedSettlementChange[] SettlementChanges() {
        var current = Current();
        if (CompletedSteps != Plan.Steps.Length) throw new InvalidOperationException("Finish every transfer and unload before settlement.");
        var baseline = SettlementBaseline ?? Request;
        return baseline.Warehouses.SelectMany(w => w.Value.Keys.Union(current.Cargo[TaggedTransportSimulator.Warehouse(w.Key)].Keys)
            .Where(id => Request.Items.TryGetValue(id, out var item) && item.UnitWeight > 0)
            .Select(id => new TaggedSettlementChange(w.Key, id, w.Value.GetValueOrDefault(id),
                current.Cargo[TaggedTransportSimulator.Warehouse(w.Key)].GetValueOrDefault(id))))
            .Where(x => x.Before != x.After).ToArray();
    }
    public TaggedTransportRequest RemainingRequest() {
        var state = Current();
        return Request with {
            Settings = Request.Settings with {
                StartIsland = state.Location, ActiveCharacter = state.Active, StartFromSelectedLocation = true,
                InitialCargo = state.Cargo.Where(x => !TaggedTransportSimulator.IsWarehouse(x.Key))
                    .SelectMany(x => x.Value.Select(i => new TaggedCargoEntry(x.Key, i.Key, i.Value))).ToArray()
            },
            Warehouses = state.Cargo.Where(x => TaggedTransportSimulator.IsWarehouse(x.Key))
                .ToDictionary(x => x.Key["warehouse:".Length..], x => new Dictionary<string, int>(x.Value)),
            Trades = Request.Trades.Select((t, i) => t with { Exchanges = state.Remaining[i] }).Where(t => t.Exchanges > 0).ToArray()
        };
    }
}
public sealed record TaggedSettlementChange(string WarehouseId, string ItemId, int Before, int After);

public static class TaggedTransportStorage {
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static void Save<T>(string path, T value) {
        if (value is TaggedTransportSession session) session.Current();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
        else File.Move(temp, path);
    }
    public static T? Load<T>(string path) {
        if (!File.Exists(path)) return default;
        try {
        string json = File.ReadAllText(path);
        var result = JsonSerializer.Deserialize<T>(json, Options)
            ?? throw new InvalidDataException("Empty TAG save.");
        if (result is TaggedTransportSession session) {
            if (session.Version != 1) throw new InvalidDataException("Unsupported TAG save version.");
            using var document = JsonDocument.Parse(json);
            var originalRequest = document.RootElement.GetProperty("Request");
            if (!originalRequest.GetProperty("Settings").TryGetProperty("ElephantSummonSeconds", out _)) {
                string originalFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(originalRequest))));
                if (originalFingerprint != session.Plan.Fingerprint) throw new InvalidDataException("Legacy TAG fingerprint mismatch.");
                session = UpgradeWhistles(session);
                result = (T)(object)session;
            }
            session.Current();
        }
        return result;
        }
        catch (Exception e) when (e is JsonException or NullReferenceException or ArgumentException or OverflowException or KeyNotFoundException) {
            throw new InvalidDataException("Invalid TAG save: " + e.Message, e);
        }
    }

    private static TaggedTransportSession UpgradeWhistles(TaggedTransportSession old) {
        if (old.CompletedSteps < 0 || old.CompletedSteps > old.Plan.Steps.Length) throw new InvalidDataException("Invalid TAG progress.");
        var sim = new TaggedTransportSimulator(old.Request);
        if (sim.Validate() is string invalid) throw new InvalidDataException(invalid);
        var state = sim.Initial(); int completed = 0;
        for (int i = 0; i < old.Plan.Steps.Length; i++) {
            var step = old.Plan.Steps[i];
            string? elephant = new[] { step.Action.From, step.Action.To }.FirstOrDefault(TaggedTransportSimulator.IsElephant);
            if (elephant is not null && !state.SummonedElephants.Contains(elephant)) {
                if (!sim.TryApply(state, new(TaggedActionKind.SummonElephant, state.Location, state.Active, elephant), out state, out string summonError))
                    throw new InvalidDataException(summonError);
            }
            if (!sim.TryApply(state, step.Action, out state, out string error)) throw new InvalidDataException(error);
            var actual = state.Steps[^1];
            if (actual.ShipLT != step.ShipLT || actual.MainLT != step.MainLT || actual.AltLT != step.AltLT)
                throw new InvalidDataException("Legacy TAG cargo mismatch.");
            if (i < old.CompletedSteps) completed = state.Steps.Count;
        }
        var plan = new TaggedTransportPlan(old.Request.Fingerprint(), state.Steps.ToArray(), state.Seconds,
            state.SailingSeconds, state.Seconds - state.SailingSeconds, state.Distance, "BestKnownWithinLimit");
        return old with { Plan = plan, CompletedSteps = completed };
    }
}
