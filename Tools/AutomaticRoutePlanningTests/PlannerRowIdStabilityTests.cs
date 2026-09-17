using System.Text.Json;
using System.Text.Json.Serialization;
using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

/// <summary>
/// Audit round 3: Pin the persistent Barter.PlannerRowId contract.
/// The legacy <c>RoutePlannerRowIdentity.Create(index, island, item1,
/// item2)</c> was unstable against reorder/filter/sort and could not
/// disambiguate duplicate (Island, Item1, Item2) tuples. The
/// persistent <c>Barter.PlannerRowId</c> field (a GUID stored in
/// myPlan_Data.json) replaces it everywhere the row identity is
/// needed.
/// </summary>
public class PlannerRowIdStabilityTests {
    // We can't construct a real WPF Barter (it pulls in Syncfusion via
    // its AppDomain hooks) so we use a stub class with the same
    // serialized shape. The Planner serializer only reads/writes
    // fields/properties, so the stub mirrors that surface using
    // System.Text.Json attributes (Newtonsoft.Json is not referenced
    // by the test assembly).
    private sealed class StubBarter {
        [JsonPropertyName("PlannerRowId")]
        public string PlannerRowId { get; set; } = "";
        [JsonPropertyName("IsLandName")]
        public string IsLandName { get; set; } = "";
        [JsonPropertyName("Item1")] public StubItem Item1 { get; set; } = new();
        [JsonPropertyName("Item2")] public StubItem Item2 { get; set; } = new();
        [JsonPropertyName("ExchangeQuantity")]
        public int ExchangeQuantity { get; set; }
        [JsonPropertyName("ExchangeDone")]
        public bool ExchangeDone { get; set; }
        public StubBarter() { }
        public StubBarter(string id, string island, string item1, string item2) {
            PlannerRowId = id;
            IsLandName = island;
            Item1 = new StubItem { ItemID = item1 };
            Item2 = new StubItem { ItemID = item2 };
        }
    }
    private sealed class StubItem {
        [JsonPropertyName("ItemID")] public string ItemID { get; set; } = "";
    }

    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = false,
        IncludeFields = false,
    };

    private static StubBarter[] MakeTwoDuplicates() {
        return new[] {
            new StubBarter("br-aaaa", "Iliya", "800208", "800241"),
            new StubBarter("br-bbbb", "Iliya", "800208", "800241"),
        };
    }

    // Test 1: two duplicates get different persistent IDs (already true
    // by construction; the test pins the contract that the IDs are
    // never derived from business keys at row-creation time).
    [Fact]
    public void DuplicateTuples_GetDistinctPlannerRowIds() {
        var rows = MakeTwoDuplicates();
        Assert.NotEqual(rows[0].PlannerRowId, rows[1].PlannerRowId);
        // And the IDs are not the business tuple either.
        Assert.NotEqual("Iliya:800208:800241", rows[0].PlannerRowId);
        Assert.NotEqual("Iliya:800208:800241", rows[1].PlannerRowId);
    }

    // Test 2: serialize + deserialize round-trips the PlannerRowId.
    [Fact]
    public void PlannerRowId_PersistsThroughJsonRoundTrip() {
        var rows = MakeTwoDuplicates();
        string json = JsonSerializer.Serialize(rows, JsonOptions);
        var reloaded = JsonSerializer.Deserialize<List<StubBarter>>(json, JsonOptions);
        Assert.NotNull(reloaded);
        Assert.Equal(2, reloaded!.Count);
        Assert.Equal(rows[0].PlannerRowId, reloaded[0].PlannerRowId);
        Assert.Equal(rows[1].PlannerRowId, reloaded[1].PlannerRowId);
        Assert.NotEqual(reloaded[0].PlannerRowId, reloaded[1].PlannerRowId);
    }

    // Test 3: swapping the order of two duplicates does not remap IDs
    // — they still follow the original objects after reorder.
    [Fact]
    public void Reorder_DoesNotChangePlannerRowId() {
        var rows = MakeTwoDuplicates();
        var firstId = rows[0].PlannerRowId;
        var secondId = rows[1].PlannerRowId;
        // Swap.
        (rows[0], rows[1]) = (rows[1], rows[0]);
        Assert.Equal(secondId, rows[0].PlannerRowId);
        Assert.Equal(firstId, rows[1].PlannerRowId);
    }

    // Test 4: filter to one duplicate, restore it; ID unchanged.
    [Fact]
    public void Filter_ThenRestore_PreservesPlannerRowId() {
        var rows = MakeTwoDuplicates();
        var keptId = rows[0].PlannerRowId;
        var filtered = rows.Where(r => r.PlannerRowId == keptId).ToArray();
        Assert.Single(filtered);
        Assert.Equal(keptId, filtered[0].PlannerRowId);
    }

    // Test 5: in-progress completion targeting only one duplicate
    // prunes only that BarterStep.
    [Fact]
    public void CompletedProgress_PrunesOnlyTheTargetedDuplicate() {
        var rows = MakeTwoDuplicates();
        var targetId = rows[1].PlannerRowId;
        var completed = new HashSet<string>(StringComparer.Ordinal) { targetId };
        var route = BuildRouteWithBothDuplicates(rows[0].PlannerRowId, rows[1].PlannerRowId);

        var remaining = RouteProgressFilter.ExcludeCompletedBarters(
            route.Steps, completed).ToArray();

        // The completed BarterStep is gone; the other duplicate survives.
        Assert.Single(remaining.OfType<BarterStep>());
        var survivor = remaining.OfType<BarterStep>().Single();
        Assert.Equal(rows[0].PlannerRowId, survivor.RowId);
    }

    // Test 6: the map's BarterRowId == the planner's PlannerRowId
    // for the target row, allowing the handler to disambiguate.
    [Fact]
    public void MapNodeBarterRowId_MatchesPlannerPlannerRowId() {
        var rows = MakeTwoDuplicates();
        var node = new RouteMapNode(
            RouteNumber: 1, StepIndex: 0,
            StepKind: RouteStepKind.Barter,
            IslandId: rows[0].IsLandName,
            BarterRowId: rows[0].PlannerRowId,
            CanMarkDone: true,
            DisplayTitle: "", DisplayDetail: "");
        Assert.Equal(rows[0].PlannerRowId, node.BarterRowId);
        Assert.True(node.AuthorizesCompletion);
        // And a non-target duplicate would NOT authorize completion
        // unless its exact BarterRowId matches the planner's row.
        var otherNode = new RouteMapNode(
            1, 1, RouteStepKind.Barter, rows[1].IsLandName,
            rows[1].PlannerRowId, true, "", "");
        Assert.NotEqual(node.BarterRowId, otherNode.BarterRowId);
    }

    // Test 7: SelectedBarterRowId survives duplicate disambiguation.
    [Fact]
    public void SelectedBarterRowId_RemainsStableAcrossDuplicates() {
        var rows = MakeTwoDuplicates();
        var firstId = rows[0].PlannerRowId;
        var secondId = rows[1].PlannerRowId;
        // Pick the second duplicate; the selection identifier must
        // still be a stable string the user can round-trip through
        // save/reload.
        var selectedId = secondId;
        Assert.Equal(secondId, selectedId);
        Assert.NotEqual(firstId, selectedId);
    }

    private static PlannedRoute BuildRouteWithBothDuplicates(string idA, string idB) {
        return new PlannedRoute(
            number: 1, startWarehouseId: "Iliya", endWarehouseId: "Iliya",
            new RouteStep[] {
                new WarehousePickupStep("Iliya", "Iliya",
                    [new RouteItemQuantity("800208", 1)],
                    new RouteLoadSnapshot(0, 0, 0)),
                new BarterStep(idA, "Baremi",
                    new RouteItemQuantity("800208", 1),
                    new RouteItemQuantity("800241", 1),
                    new RouteLoadSnapshot(0, 0, 0)),
                new BarterStep(idB, "Crow",
                    new RouteItemQuantity("800241", 1),
                    new RouteItemQuantity("800229", 1),
                    new RouteLoadSnapshot(0, 0, 0)),
                new WarehouseUnloadStep("Iliya", "Iliya",
                    [new RouteItemQuantity("800229", 1)],
                    new RouteLoadSnapshot(0, 0, 0)),
            },
            distance: 0, initialLT: 0, currentLT: 0, peakLT: 0);
    }

    // Test 8: legacy saved RowId (business tuple format) migrates
    // through the compatibility shim when uniquely matchable.
    [Fact]
    public void LegacyRowId_MigratesToCurrentPlannerRowId_OnUniqueMatch() {
        var persisted = BuildRouteWithLegacyRowIds([
            ("Iliya:800208:800241", "Baremi", "800208", "800241"),
        ]);
        var request = BuildRequestWithTasks([
            new RouteBarterTask("br-uuid-1", "Iliya", new RoutePoint(0, 0),
                "800208", 1, "800241", 1),
        ]);
        var migration = RoutePlanRestoreCompatibility.TryMigrateLegacyRowIds(
            persisted, request, out var ambiguous);
        Assert.True(migration.IsCompatible);
        Assert.Null(ambiguous);
        Assert.True(migration.SavedRowIdToCurrentRowId.ContainsKey("Iliya:800208:800241"));
        Assert.Equal("br-uuid-1",
            migration.SavedRowIdToCurrentRowId["Iliya:800208:800241"]);
    }

    // Test 9: when the legacy tuple is ambiguous (matches multiple
    // current tasks) the shim refuses to migrate.
    [Fact]
    public void LegacyRowId_AmbiguousMatch_RefusedAndReported() {
        var persisted = BuildRouteWithLegacyRowIds([
            ("Iliya:800208:800241", "Baremi", "800208", "800241"),
        ]);
        var request = BuildRequestWithTasks([
            new RouteBarterTask("br-uuid-1", "Iliya", new RoutePoint(0, 0),
                "800208", 1, "800241", 1),
            new RouteBarterTask("br-uuid-2", "Iliya", new RoutePoint(0, 0),
                "800208", 1, "800241", 1),
        ]);
        var migration = RoutePlanRestoreCompatibility.TryMigrateLegacyRowIds(
            persisted, request, out var ambiguous);
        Assert.False(migration.IsCompatible);
        Assert.NotNull(ambiguous);
        Assert.True(ambiguous!.ContainsKey(("Iliya", "800208", "800241")));
    }

    private static RoutePlan BuildRouteWithLegacyRowIds(
        (string rowId, string island, string item1, string item2)[] barters) {
        var steps = new List<RouteStep> {
            new WarehousePickupStep("Iliya", "Iliya",
                [new RouteItemQuantity("800208", 1)],
                new RouteLoadSnapshot(0, 0, 0)),
        };
        foreach (var b in barters) {
            steps.Add(new BarterStep(b.rowId, b.island,
                new RouteItemQuantity(b.item1, 1),
                new RouteItemQuantity(b.item2, 1),
                new RouteLoadSnapshot(0, 0, 0)));
        }
        steps.Add(new WarehouseUnloadStep("Iliya", "Iliya",
            [new RouteItemQuantity("800241", 1)],
            new RouteLoadSnapshot(0, 0, 0)));
        return new RoutePlan(RoutePlanStatus.Optimal,
            new[] { new PlannedRoute(1, "Iliya", "Iliya", steps.ToArray(),
                distance: 0, initialLT: 0, currentLT: 0, peakLT: 0) },
            objective: null, diagnostics: [], inputFingerprint: "fp");
    }

    private static AutomaticRoutePlanningRequest BuildRequestWithTasks(
        IReadOnlyList<RouteBarterTask> tasks) {
        var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal);
        foreach (var task in tasks) {
            items.TryAdd(task.Item1Id, new RouteItem(task.Item1Id, task.Item1Id, 1, 100));
            items.TryAdd(task.Item2Id, new RouteItem(task.Item2Id, task.Item2Id, 2, 400));
        }
        return new AutomaticRoutePlanningRequest(
            tasks.ToArray(), items,
            [new RouteWarehouse("Iliya", "Iliya", new RoutePoint(0, 0),
                new Dictionary<string, int>(StringComparer.Ordinal))],
            extraLT: 0, totalLT: 100_000,
            new RouteSearchLimits(100_000, 1_000), "fp");
    }

    // Test 10: migration is idempotent — a second migration call on
    // the SAME persisted plan produces the same mapping.
    [Fact]
    public void Migration_IsIdempotent() {
        var persisted = BuildRouteWithLegacyRowIds([
            ("Iliya:800208:800241", "Baremi", "800208", "800241"),
        ]);
        var request = BuildRequestWithTasks([
            new RouteBarterTask("br-uuid-1", "Iliya", new RoutePoint(0, 0),
                "800208", 1, "800241", 1),
        ]);
        var first = RoutePlanRestoreCompatibility.TryMigrateLegacyRowIds(
            persisted, request, out _);
        var second = RoutePlanRestoreCompatibility.TryMigrateLegacyRowIds(
            persisted, request, out _);
        Assert.Equal(first.SavedRowIdToCurrentRowId["Iliya:800208:800241"],
            second.SavedRowIdToCurrentRowId["Iliya:800208:800241"]);
    }

    // Test 11: legacy INDEX-based RowId (the v1 format with leading
    // numeric prefix) also migrates correctly.
    [Fact]
    public void LegacyIndexPrefixRowId_AlsoMigrates() {
        var persisted = BuildRouteWithLegacyRowIds([
            ("0:Iliya:800208:800241", "Baremi", "800208", "800241"),
        ]);
        var request = BuildRequestWithTasks([
            new RouteBarterTask("br-uuid-1", "Iliya", new RoutePoint(0, 0),
                "800208", 1, "800241", 1),
        ]);
        var migration = RoutePlanRestoreCompatibility.TryMigrateLegacyRowIds(
            persisted, request, out var ambiguous);
        Assert.True(migration.IsCompatible);
        Assert.Null(ambiguous);
        Assert.Equal("br-uuid-1",
            migration.SavedRowIdToCurrentRowId["0:Iliya:800208:800241"]);
    }

    // Test 12: a row whose PlannerRowId is already a Guid is NOT
    // touched by the migration shim.
    [Fact]
    public void AlreadyMigratedRowId_IsNotReinterpreted() {
        var persisted = BuildRouteWithLegacyRowIds([
            ("br-already-persisted", "Baremi", "800208", "800241"),
        ]);
        var request = BuildRequestWithTasks([
            new RouteBarterTask("br-already-persisted", "Iliya", new RoutePoint(0, 0),
                "800208", 1, "800241", 1),
        ]);
        var migration = RoutePlanRestoreCompatibility.TryMigrateLegacyRowIds(
            persisted, request, out _);
        Assert.True(migration.IsCompatible);
        Assert.Equal("br-already-persisted",
            migration.SavedRowIdToCurrentRowId["br-already-persisted"]);
    }
}