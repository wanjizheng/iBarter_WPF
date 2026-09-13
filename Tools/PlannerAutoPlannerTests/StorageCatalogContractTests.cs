using System.Runtime.CompilerServices;
using Xunit;

namespace PlannerAutoPlannerTests;

public sealed class StorageCatalogContractTests {
    [Fact]
    public void Golden_flour_sack_stays_level_7_and_is_not_tracked_by_storage() {
        string repositoryRoot = RepositoryRoot();
        string[] catalogLines = File.ReadAllLines(
            Path.Combine(repositoryRoot, "Resources", "Items.csv"));
        string storageViewModelSource = File.ReadAllText(
            Path.Combine(repositoryRoot, "ViewModel", "StorageViewModel.cs"));

        Assert.Contains("Golden Flour Sack,800226,7,1", catalogLines);
        Assert.DoesNotContain("800226", storageViewModelSource);
        Assert.DoesNotContain("Golden Flour Sack", storageViewModelSource);
    }

    [Fact]
    public void Rust_repair_tool_stays_in_catalog_but_is_removed_from_storage_loads() {
        string repositoryRoot = RepositoryRoot();
        string[] catalogLines = File.ReadAllLines(
            Path.Combine(repositoryRoot, "Resources", "Items.csv"));
        string storageViewModelSource = File.ReadAllText(
            Path.Combine(repositoryRoot, "ViewModel", "StorageViewModel.cs"));

        Assert.Contains("Rust Repair Tool,800073,5,1", catalogLines);
        Assert.Contains(
            ".Where(item => !string.Equals(item.ItemID, \"800073\", StringComparison.Ordinal))",
            storageViewModelSource);
    }

    private static string RepositoryRoot([CallerFilePath] string sourcePath = "") {
        return Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(sourcePath)!,
            "..",
            ".."));
    }
}
