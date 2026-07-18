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
    public void ResultNeverNegative() {
        // The pure-derivation formula cannot produce negative values
        // because ExchangeDone rows are filtered out before accumulation.
        var rows = new[] {
            new FakeRow("a", parley: 10_000, qty: 1, done: true),
            new FakeRow("b", parley: 10_000, qty: 0),
        };
        long total = RouteParleyCalculator.CalculateRemaining(rows);
        Assert.True(total >= 0);
        Assert.Equal(0, total);
    }

    [Fact]
    public void ExchangeQuantityAlreadyRemaining_DoesNotSubtractAgain() {
        // Audit round 2: the v1 formula subtracted a "completedTimes"
        // counter from ExchangeQuantity. That was wrong because
        // ExchangeQuantity already represents the REMAINING planned
        // executions (it is clamped by IslandRemaining and overwritten
        // by Auto Plan). This test pins the contract: ExchangeQuantity=5
        // means 5 exchanges left, regardless of UI counters.
        var rows = new[] {
            new FakeRow("a", parley: 10_000, qty: 5),
        };
        long total = RouteParleyCalculator.CalculateRemaining(rows);
        Assert.Equal(50_000, total);
    }

    [Fact]
    public void PlannerAndMapCKProduceSameResult() {
        // Both entry points (Planner CK and map double-click) end up
        // calling the same authoritative calculator with the same
        // Planner state, so they MUST produce the same number.
        var rowsBefore = new[] {
            new FakeRow("a", parley: 50_000, qty: 1),
            new FakeRow("b", parley: 100_000, qty: 1),
            new FakeRow("c", parley: 150_000, qty: 1),
        };
        var rowsAfterPlannerCK = new[] {
            new FakeRow("a", parley: 50_000, qty: 1, done: true),
            new FakeRow("b", parley: 100_000, qty: 1),
            new FakeRow("c", parley: 150_000, qty: 1),
        };
        var rowsAfterMapDoubleClick = new[] {
            new FakeRow("a", parley: 50_000, qty: 1, done: true),
            new FakeRow("b", parley: 100_000, qty: 1),
            new FakeRow("c", parley: 150_000, qty: 1),
        };
        long plannerResult = RouteParleyCalculator.CalculateRemaining(rowsAfterPlannerCK);
        long mapResult = RouteParleyCalculator.CalculateRemaining(rowsAfterMapDoubleClick);
        long before = RouteParleyCalculator.CalculateRemaining(rowsBefore);
        Assert.Equal(plannerResult, mapResult);
        // 'a' is done; b (100k) + c (150k) = 250k.
        Assert.Equal(250_000, plannerResult);
        // All three: 50k + 100k + 150k = 300k.
        Assert.Equal(300_000, before);
    }

    [Fact]
    public void CompletionPlusUndoDoesNotDrift() {
        // Bug 4 (drift): a UI counter that did displayedParley -= completedRow.Parley
        // would fail to recover the original value when the user unticks the row.
        // The pure formula has no such state.
        var rowsBefore = new[] {
            new FakeRow("a", parley: 50_000, qty: 1),
            new FakeRow("b", parley: 100_000, qty: 1),
            new FakeRow("c", parley: 150_000, qty: 1),
        };
        var rowsAfterCK = new[] {
            new FakeRow("a", parley: 50_000, qty: 1, done: true),
            new FakeRow("b", parley: 100_000, qty: 1),
            new FakeRow("c", parley: 150_000, qty: 1),
        };
        long before = RouteParleyCalculator.CalculateRemaining(rowsBefore);
        long afterCK = RouteParleyCalculator.CalculateRemaining(rowsAfterCK);
        long afterUndo = RouteParleyCalculator.CalculateRemaining(rowsBefore);
        Assert.Equal(before, afterUndo);
        // After ticking 'a': b (100k) + c (150k) = 250k.
        Assert.Equal(250_000, afterCK);
        // Round-trip back to before.
        Assert.Equal(300_000, afterUndo);
    }
}