using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

/// <summary>
/// Bug 1 + Audit 5 (round 2): RowId stability. The legacy
/// <c>RoutePlannerRowIdentity.Create(index, ...)</c> baked the
/// planner collection's current index into the RowId, which made it
/// unstable against reorder/filter/sort. The audit fixed the schema
/// to drop <c>index</c> entirely so two planners with identical
/// (Island, Item1, Item2) tuples always agree on a RowId.
/// </summary>
public class RoutePlannerRowIdentityTests {
    [Fact]
    public void Create_IgnoresIndex_TwoCallsProduceSameId() {
        string a = RoutePlannerRowIdentity.Create(
            0, "Iliya", "800208", "800241");
        string b = RoutePlannerRowIdentity.Create(
            7, "Iliya", "800208", "800241");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Create_DifferentIslands_ProduceDifferentIds() {
        string a = RoutePlannerRowIdentity.Create(0, "Iliya", "800208", "800241");
        string b = RoutePlannerRowIdentity.Create(0, "Velia", "800208", "800241");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Create_DifferentItems_ProduceDifferentIds() {
        string a = RoutePlannerRowIdentity.Create(0, "Iliya", "800208", "800241");
        string b = RoutePlannerRowIdentity.Create(0, "Iliya", "800208", "800229");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void CreateForRows_AssignsDeterministicDuplicateSuffix() {
        // Two rows with identical business keys get #1 and #2 in
        // first-seen order. A second call with the same input order
        // produces the same suffixes (determinism).
        var rows = new (int, string, string, string)[] {
            (0, "Iliya", "800208", "800241"),
            (1, "Iliya", "800208", "800241"), // duplicate
            (2, "Velia", "800049", "10"),
        };
        var first = RoutePlannerRowIdentity.CreateForRows(rows);
        var second = RoutePlannerRowIdentity.CreateForRows(rows);
        Assert.Equal(first.Count, second.Count);
        for (int i = 0; i < first.Count; i++)
            Assert.Equal(first[i], second[i]);
        // Duplicates get #1 and #2.
        Assert.EndsWith("#1", first[0]);
        Assert.EndsWith("#2", first[1]);
        Assert.Equal("Velia:800049:10", first[2]);
    }

    [Fact]
    public void CreateForRows_ReorderingPreservesRowIds() {
        // Audit round 2: even when the Planner collection is
        // reordered, the same (Island, Item1, Item2) tuple receives
        // the same RowId. The test pins the contract.
        var rows = new (int, string, string, string)[] {
            (5, "Iliya", "800208", "800241"),  // first row but index 5
            (2, "Velia", "800049", "10"),       // second row but index 2
        };
        var ids = RoutePlannerRowIdentity.CreateForRows(rows);
        Assert.Equal("Iliya:800208:800241", ids[0]);
        Assert.Equal("Velia:800049:10", ids[1]);

        // Same rows in reverse input order — same output (the
        // index parameter doesn't leak into the id).
        var reversed = new (int, string, string, string)[] {
            (2, "Velia", "800049", "10"),
            (5, "Iliya", "800208", "800241"),
        };
        var reversedIds = RoutePlannerRowIdentity.CreateForRows(reversed);
        // The non-duplicate rows always produce the same bare id.
        Assert.Contains("Iliya:800208:800241", reversedIds);
        Assert.Contains("Velia:800049:10", reversedIds);
    }

    [Fact]
    public void CreateForRows_NoDuplicates_NoSuffix() {
        var rows = new (int, string, string, string)[] {
            (0, "Iliya", "800208", "800241"),
            (1, "Velia", "800049", "10"),
        };
        var ids = RoutePlannerRowIdentity.CreateForRows(rows);
        Assert.Equal("Iliya:800208:800241", ids[0]);
        Assert.Equal("Velia:800049:10", ids[1]);
        Assert.DoesNotContain("#", ids[0]);
        Assert.DoesNotContain("#", ids[1]);
    }

    [Fact]
    public void EmptyIsland_ReturnsInvalid() {
        string a = RoutePlannerRowIdentity.Create(0, "", "800208", "800241");
        Assert.StartsWith("INVALID:", a);
    }
}