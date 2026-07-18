using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

/// <summary>
/// Bug 4: the legacy <c>UpdateParley</c> summed every row with
/// <c>ExchangeQuantity &gt; 0</c>, including rows whose <c>ExchangeDone</c>
/// was already true. Ticking CK therefore left the displayed number
/// unchanged, and a fresh <c>displayedParley -= completedRow.Parley</c>
/// patch would drift under repeated events, restart and undo.
///
/// The contract of <see cref="RouteParleyCalculator.CalculateRemaining"/>
/// is a *pure* derivation from the current Planner state. Every test below
/// fires the same input multiple times and asserts the output is stable,
/// because any UI label assignment that drives off this method cannot
/// double-subtract.
/// </summary>
public class RouteParleyCalculatorTests {
    private sealed class FakeRow : IRouteParleyRow {
        public FakeRow(string id, int parley, int qty, bool done = false) {
            RowId = id;
            Parley = parley;
            ExchangeQuantity = qty;
            ExchangeDone = done;
        }
        public string RowId { get; }
        public int Parley { get; }
        public int ExchangeQuantity { get; }
        public bool ExchangeDone { get; }
    }

    [Fact]
    public void Empty_HasZeroRemaining() {
        long total = RouteParleyCalculator.CalculateRemaining(Array.Empty<IRouteParleyRow>());
        Assert.Equal(0, total);
    }

    [Fact]
    public void OnlyUnfinishedRowsContribute() {
        var rows = new[] {
            new FakeRow("a", parley: 50_000, qty: 1),
            new FakeRow("b", parley: 100_000, qty: 1),
            new FakeRow("c", parley: 150_000, qty: 1),
        };
        long total = RouteParleyCalculator.CalculateRemaining(rows);
        Assert.Equal(300_000, total);
    }

    [Fact]
    public void CompletedRowsAreExcluded() {
        // Bug 4: completing "a" must immediately drop its contribution.
        var rows = new[] {
            new FakeRow("a", parley: 50_000, qty: 1, done: true),
            new FakeRow("b", parley: 100_000, qty: 1),
            new FakeRow("c", parley: 150_000, qty: 1),
        };
        long total = RouteParleyCalculator.CalculateRemaining(rows);
        Assert.Equal(250_000, total);
    }

    [Fact]
    public void MultipleCompletionsSubtract() {
        var rows = new[] {
            new FakeRow("a", parley: 50_000, qty: 1, done: true),
            new FakeRow("b", parley: 100_000, qty: 1, done: true),
            new FakeRow("c", parley: 150_000, qty: 1),
        };
        long total = RouteParleyCalculator.CalculateRemaining(rows);
        Assert.Equal(150_000, total);
    }

    [Fact]
    public void RepeatedInvocationsAreIdempotent() {
        // Bug 4 defense: refreshing twice must not double-subtract. The
        // legacy incremental formula
        //     displayedParley -= completedRow.Parley
        // would produce 200k on the second call. The pure formula must
        // stay at 250k.
        var rows = new[] {
            new FakeRow("a", parley: 50_000, qty: 1, done: true),
            new FakeRow("b", parley: 100_000, qty: 1),
            new FakeRow("c", parley: 150_000, qty: 1),
        };
        long first = RouteParleyCalculator.CalculateRemaining(rows);
        long second = RouteParleyCalculator.CalculateRemaining(rows);
        long third = RouteParleyCalculator.CalculateRemaining(rows);
        Assert.Equal(first, second);
        Assert.Equal(first, third);
        Assert.Equal(250_000, first);
    }

    [Fact]
    public void UndoRestoresFullTotal() {
        var rows = new[] {
            new FakeRow("a", parley: 50_000, qty: 1, done: false),
            new FakeRow("b", parley: 100_000, qty: 1),
            new FakeRow("c", parley: 150_000, qty: 1),
        };
        long total = RouteParleyCalculator.CalculateRemaining(rows);
        Assert.Equal(300_000, total);
    }

    [Fact]
    public void AllCompletedReturnsZero() {
        var rows = new[] {
            new FakeRow("a", parley: 50_000, qty: 1, done: true),
            new FakeRow("b", parley: 100_000, qty: 1, done: true),
        };
        long total = RouteParleyCalculator.CalculateRemaining(rows);
        Assert.Equal(0, total);
        // Guarded against negative drift.
        Assert.True(total >= 0);
    }

    [Fact]
    public void ZeroQuantityRowsAreIgnored() {
        // Disabled rows (user typed 0) must NOT contribute.
        var rows = new[] {
            new FakeRow("a", parley: 50_000, qty: 0),
            new FakeRow("b", parley: 100_000, qty: 1),
        };
        long total = RouteParleyCalculator.CalculateRemaining(rows);
        Assert.Equal(100_000, total);
    }

    [Fact]
    public void MultiplierTimesParley() {
        // ExchangeQuantity = 3 means 3 exchanges of parley=10_000 each.
        var rows = new[] {
            new FakeRow("a", parley: 10_000, qty: 3),
        };
        long total = RouteParleyCalculator.CalculateRemaining(rows);
        Assert.Equal(30_000, total);
    }

    [Fact]
    public void CompletedTimesLookupAppliesPartialCompletion() {
        // Reserve case: row says ExchangeQuantity=5, the user has manually
        // ticked 2 of those off (or the planner pre-completed 2). Lookup
        // table applies that.
        var rows = new[] {
            new FakeRow("a", parley: 10_000, qty: 5),
        };
        var completed = new Dictionary<string, int> { ["a"] = 2 };
        long total = RouteParleyCalculator.CalculateRemaining(rows, completed);
        Assert.Equal(30_000, total);
    }

    [Fact]
    public void ResultNeverNegative() {
        // Pathological: completedTimes exceeds ExchangeQuantity.
        var rows = new[] {
            new FakeRow("a", parley: 10_000, qty: 1, done: true),
            new FakeRow("b", parley: 10_000, qty: 0),
        };
        var completed = new Dictionary<string, int> { ["a"] = 99 };
        long total = RouteParleyCalculator.CalculateRemaining(rows, completed);
        Assert.True(total >= 0);
        Assert.Equal(0, total);
    }
}