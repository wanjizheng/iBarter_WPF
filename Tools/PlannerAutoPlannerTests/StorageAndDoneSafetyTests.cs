using iBarter.Persistence;
using iBarter.Planning;
using Xunit;

namespace PlannerAutoPlannerTests;

public sealed class StorageAndDoneSafetyTests {
    [Fact]
    public void Atomic_save_keeps_previous_file_as_backup() {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "storage.json");
        File.WriteAllText(path, "old-valid");

        AtomicFileStore.WriteValidated(path, "new-valid", text => text.EndsWith("valid"));

        Assert.Equal("new-valid", File.ReadAllText(path));
        Assert.Equal("old-valid", File.ReadAllText(path + ".bak"));
    }

    [Fact]
    public void Invalid_new_content_never_replaces_valid_file() {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "storage.json");
        File.WriteAllText(path, "valid");

        Assert.Throws<InvalidDataException>(() =>
            AtomicFileStore.WriteValidated(path, "broken", text => text == "valid"));

        Assert.Equal("valid", File.ReadAllText(path));
    }

    [Fact]
    public void Destructive_state_transition_creates_timestamped_recovery_copy() {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "storage.json");
        File.WriteAllText(path, "nonzero");

        AtomicFileStore.WriteValidated(path, "zero", _ => true, text => text == "nonzero");

        string recovery = Assert.Single(Directory.GetFiles(temp.Path, "storage.json.recovery-*.json"));
        Assert.Equal("nonzero", File.ReadAllText(recovery));
    }

    [Fact]
    public void Invalid_primary_loads_valid_backup() {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "storage.json");
        File.WriteAllText(path, "broken");
        File.WriteAllText(path + ".bak", "valid");

        Assert.True(AtomicFileStore.TryReadValidated(path, text => text == "valid", out string content, out bool recovered));
        Assert.True(recovered);
        Assert.Equal("valid", content);
    }

    [Fact]
    public void Done_nets_chain_and_adds_final_output() {
        var reconciler = new PlannerInventoryReconciler();
        var inventory = new[] {
            Item("A", velia: 10), Item("B"), Item("C"), Item("10")
        };
        var exchanges = new[] {
            Exchange("1", "A", 2, "B", 1, 3),
            Exchange("2", "B", 1, "C", 2, 3),
            Exchange("3", "C", 1, "10", 100, 1)
        };

        var result = reconciler.Reconcile(inventory, exchanges, defaultWarehouseIndex: 1,
            ignoredOutputItemIds: new HashSet<string> { "10" });

        Assert.True(result.Success);
        Assert.Equal(4, result.Inventory["A"].Total);
        Assert.Equal(0, result.Inventory["B"].Total);
        Assert.Equal(5, result.Inventory["C"].Total);
        Assert.Equal(0, result.Inventory["10"].Total);
        Assert.Equal(5, result.Inventory["C"].Iliya);
    }

    [Fact]
    public void Done_deducts_from_existing_warehouses_without_duplicating_total() {
        var reconciler = new PlannerInventoryReconciler();
        var inventory = new[] { Item("A", velia: 2, iliya: 5), Item("B") };

        var result = reconciler.Reconcile(inventory,
            [Exchange("1", "A", 4, "B", 1, 1)], defaultWarehouseIndex: 0);

        Assert.True(result.Success);
        Assert.Equal(3, result.Inventory["A"].Total);
        Assert.Equal(2, result.Inventory["A"].Velia);
        Assert.Equal(1, result.Inventory["A"].Iliya);
        Assert.Equal(1, result.Inventory["B"].Velia);
    }

    [Fact]
    public void Done_does_not_require_or_store_ignored_level7_output() {
        var reconciler = new PlannerInventoryReconciler();
        var inventory = new[] { Item("A", velia: 5) };

        var result = reconciler.Reconcile(
            inventory,
            [Exchange("1", "A", 1, "800231", 1, 5)],
            defaultWarehouseIndex: 0,
            ignoredOutputItemIds: new HashSet<string>(StringComparer.Ordinal) { "800231" });

        Assert.True(result.Success);
        Assert.Equal(0, result.Inventory["A"].Total);
        Assert.DoesNotContain("800231", result.Inventory.Keys);
    }

    [Fact]
    public void Done_shortage_rejects_entire_transaction() {
        var reconciler = new PlannerInventoryReconciler();
        var inventory = new[] { Item("A", velia: 1), Item("B", iliya: 7) };

        var result = reconciler.Reconcile(inventory,
            [Exchange("1", "A", 2, "B", 1, 1)], defaultWarehouseIndex: 0);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Code == "INSUFFICIENT_STOCK" && error.ItemId == "A");
        Assert.Equal(1, result.Inventory["A"].Total);
        Assert.Equal(7, result.Inventory["B"].Total);
    }

    private static PlannerWarehouseInventory Item(string id, int velia = 0, int iliya = 0, int epheria = 0, int ancado = 0) =>
        new(id, velia, iliya, epheria, ancado);

    private static PlannerInventoryExchange Exchange(string row, string input, int inputQty, string output, int outputQty, int multiplier) =>
        new(row, input, inputQty, output, outputQty, multiplier);

    private sealed class TempDirectory : IDisposable {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "iBarter-tests-" + Guid.NewGuid().ToString("N"));
        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
