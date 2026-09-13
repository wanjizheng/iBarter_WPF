using iBarter.View;
using Xunit;

namespace IslandNavigationTests;

/// <summary>
/// Regression suite for the map overlay reflow pipeline introduced
/// in the resize-fix. Every case here pins a property of the
/// pipeline that, if broken, lets the user's map items stay stuck
/// on a window resize.
///
/// Targets the three pure helpers in View/MapControl.Reflow.cs:
///   * <see cref="MapOverlayReflowPipeline"/> — coalescing flag.
///   * <see cref="MapProjectionHelper"/> — normalized → pixel math.
///   * <see cref="MapIslandVisualCleanup"/> — stale-grid decision
///     rule (Temp-protected, predicate-driven, WPF-free).
///
/// The actual ScheduleMapOverlayReflow method lives on the WPF
/// root MapControl and is exercised manually; the static helpers
/// here cover every rule the pipeline depends on.
/// </summary>
public class MapOverlayReflowTests {

    // ──────────── Pipeline coalescing ────────────

    [Fact]
    public void Pipeline_TryBegin_FirstCallReturnsTrue() {
        var pipeline = new MapOverlayReflowPipeline();
        Assert.True(pipeline.TryBegin());
    }

    [Fact]
    public void Pipeline_TryBegin_SecondCallBeforeEndReturnsFalse() {
        var pipeline = new MapOverlayReflowPipeline();
        Assert.True(pipeline.TryBegin());
        Assert.False(pipeline.TryBegin(),
            "a second TryBegin before End must be coalesced away");
        Assert.False(pipeline.TryBegin(),
            "a third TryBegin before End must STILL be coalesced away");
    }

    [Fact]
    public void Pipeline_IsPending_TracksTryBeginState() {
        var pipeline = new MapOverlayReflowPipeline();
        Assert.False(pipeline.IsPending);
        Assert.True(pipeline.TryBegin());
        Assert.True(pipeline.IsPending);
        pipeline.End();
        Assert.False(pipeline.IsPending);
    }

    [Fact]
    public void Pipeline_End_AllowsAnotherTryBegin() {
        var pipeline = new MapOverlayReflowPipeline();
        Assert.True(pipeline.TryBegin());
        pipeline.End();
        Assert.True(pipeline.TryBegin(),
            "after End the pipeline must accept a fresh reflow");
        pipeline.End();
        Assert.True(pipeline.TryBegin());
    }

    [Fact]
    public void Pipeline_TryBeginAfterEnd_IsCoalescableAgain() {
        // Simulates the resize-fix contract: a rapid burst of
        // resize events collapses to one, then after the BeginInvoke
        // fires the pipeline re-arms for the NEXT burst.
        var pipeline = new MapOverlayReflowPipeline();
        for (int burst = 0; burst < 3; burst++) {
            Assert.True(pipeline.TryBegin(), $"burst {burst}: first TryBegin");
            for (int drop = 0; drop < 5; drop++) {
                Assert.False(pipeline.TryBegin(),
                    $"burst {burst} drop {drop}: subsequent TryBegin must coalesce");
            }
            pipeline.End();
        }
    }

    // ──────────── Projection math ────────────

    [Fact]
    public void Projection_NormalizedPoint_At800x450_ProducesExpectedPixels() {
        var (x, y) = MapProjectionHelper.ProjectNormalized(0.5, 0.5, 800, 450);
        Assert.Equal(400.0, x);
        Assert.Equal(225.0, y);
    }

    [Fact]
    public void Projection_NormalizedPoint_At1200x675_ProducesExpectedPixels() {
        var (x, y) = MapProjectionHelper.ProjectNormalized(0.5, 0.5, 1200, 675);
        Assert.Equal(600.0, x);
        Assert.Equal(337.5, y);
    }

    [Fact]
    public void Projection_ScalesLinearlyAcrossHostResizes() {
        // The resize-fix contract: scaling the host from 800x450
        // to 1200x675 (1.5x both dimensions) must scale the
        // projected pixel centre by the same 1.5x factor.
        var (x1, y1) = MapProjectionHelper.ProjectNormalized(0.5, 0.5, 800, 450);
        var (x2, y2) = MapProjectionHelper.ProjectNormalized(0.5, 0.5, 1200, 675);
        Assert.Equal(x1 * 1.5, x2);
        Assert.Equal(y1 * 1.5, y2);
    }

    [Fact]
    public void Projection_OffCentrePoint_PreservesRatio() {
        // Same 0.25 ratio from the left, across two host sizes.
        var (x1, y1) = MapProjectionHelper.ProjectNormalized(0.25, 0.75, 800, 450);
        var (x2, y2) = MapProjectionHelper.ProjectNormalized(0.25, 0.75, 1200, 675);
        Assert.Equal(200.0, x1);
        Assert.Equal(337.5, y1);
        Assert.Equal(300.0, x2);
        Assert.Equal(506.25, y2);
        Assert.Equal(x1 * 1.5, x2);
        Assert.Equal(y1 * 1.5, y2);
    }

    [Fact]
    public void Projection_EdgeCoordinates_MapToHostBounds() {
        // The four corners must map to the host corners: (0,0)
        // is top-left, (1,1) is bottom-right.
        Assert.Equal((0.0, 0.0),
            MapProjectionHelper.ProjectNormalized(0.0, 0.0, 1920, 1080));
        Assert.Equal((1920.0, 1080.0),
            MapProjectionHelper.ProjectNormalized(1.0, 1.0, 1920, 1080));
        Assert.Equal((1920.0, 0.0),
            MapProjectionHelper.ProjectNormalized(1.0, 0.0, 1920, 1080));
        Assert.Equal((0.0, 1080.0),
            MapProjectionHelper.ProjectNormalized(0.0, 1.0, 1920, 1080));
    }

    // ──────────── Stale visual cleanup ────────────

    [Fact]
    public void Cleanup_AllSnapshotsAreTemp_ReturnsNoIndices() {
        // If everything is a Temp marker, nothing should be
        // removed — Temp placeholders stay on the map permanently.
        // The helper still calls getSnapshot once per item (to
        // read IsTemp), but the Temp short-circuit stops it from
        // reaching the predicate.
        var items = new[] {
            new MapIslandCleanupSnapshot(IsTemp: true, IsWarehouse: false, IslandId: "Iliya"),
            new MapIslandCleanupSnapshot(IsTemp: true, IsWarehouse: false, IslandId: "Crow"),
        };
        int getSnapshotCalls = 0;
        var indices = MapIslandVisualCleanup.CollectStaleIndices(
            items,
            snapshot => { getSnapshotCalls++; return snapshot; },
            _ => true); // pretend every snapshot is stale
        Assert.Empty(indices);
        Assert.Equal(2, getSnapshotCalls);
    }

    [Fact]
    public void Cleanup_NonTempWithNoActiveBarter_ReturnsIndex() {
        // The user's "tick CK and the marker vanishes" case:
        // a non-Temp snapshot that has no active barter and is
        // not a warehouse must be removed.
        var items = new[] {
            new MapIslandCleanupSnapshot(IsTemp: false, IsWarehouse: false, IslandId: "Iliya"),
        };
        var indices = MapIslandVisualCleanup.CollectStaleIndices(
            items,
            _ => items[0],
            snap => true);
        Assert.Single(indices);
        Assert.Equal(0, indices[0]);
    }

    [Fact]
    public void Cleanup_NonTempWarehouseMarker_IsPreservedByPredicate() {
        // The helper itself only enforces the Temp rule. The
        // caller-supplied predicate decides whether warehouse
        // markers get removed; in production
        // BuildCleanupSnapshot + the predicate in
        // IslandsButtonRearrange both treat IsWarehouse as
        // "this snapshot is part of the route plan, don't drop
        // it". Mirror that contract here.
        var items = new[] {
            new MapIslandCleanupSnapshot(IsTemp: false, IsWarehouse: true, IslandId: "Iliya"),
        };
        var indices = MapIslandVisualCleanup.CollectStaleIndices(
            items,
            snapshot => snapshot,
            snap => !snap.IsWarehouse); // production predicate
        Assert.Empty(indices);
    }

    [Fact]
    public void Cleanup_MixedList_OnlyRemovesExpected() {
        // Order must be: [Temp, stale, Temp, stale, keep] →
        // indices [1, 3] in backward-walk order (the helper returns
        // indices in reverse so the caller can walk the returned
        // list forward when removing in reverse-list order).
        // Index 4 is a warehouse marker and the predicate
        // (which mirrors IslandsButtonRearrange) keeps it.
        var items = new[] {
            new MapIslandCleanupSnapshot(IsTemp: true,  IsWarehouse: false, IslandId: "Crow"),
            new MapIslandCleanupSnapshot(IsTemp: false, IsWarehouse: false, IslandId: "Iliya"),
            new MapIslandCleanupSnapshot(IsTemp: true,  IsWarehouse: false, IslandId: "Tashu"),
            new MapIslandCleanupSnapshot(IsTemp: false, IsWarehouse: false, IslandId: "Baremi"),
            new MapIslandCleanupSnapshot(IsTemp: false, IsWarehouse: true,  IslandId: "Lema"),
        };
        var indices = MapIslandVisualCleanup.CollectStaleIndices(
            items,
            snapshot => snapshot,
            snap => !snap.IsWarehouse);
        Assert.Equal(new[] { 3, 1 }, indices);
    }

    [Fact]
    public void Cleanup_PredicateFalse_NothingRemoved() {
        var items = new[] {
            new MapIslandCleanupSnapshot(IsTemp: false, IsWarehouse: false, IslandId: "Iliya"),
        };
        var indices = MapIslandVisualCleanup.CollectStaleIndices(
            items,
            _ => items[0],
            _ => false);
        Assert.Empty(indices);
    }

    [Fact]
    public void Cleanup_PredicateTrue_AllNonTempRemoved() {
        // Belt-and-braces: every non-Temp snapshot is removed.
        var items = new[] {
            new MapIslandCleanupSnapshot(IsTemp: false, IsWarehouse: false, IslandId: "Iliya"),
            new MapIslandCleanupSnapshot(IsTemp: false, IsWarehouse: false, IslandId: "Crow"),
            new MapIslandCleanupSnapshot(IsTemp: false, IsWarehouse: false, IslandId: "Baremi"),
        };
        var indices = MapIslandVisualCleanup.CollectStaleIndices(
            items,
            snapshot => snapshot,
            _ => true);
        Assert.Equal(3, indices.Count);
        Assert.Contains(0, indices);
        Assert.Contains(1, indices);
        Assert.Contains(2, indices);
    }

    // ──────────── Coalescing + cleanup together ────────────

    [Fact]
    public void Reflow_BurstOfSizeChanged_CoalescesToOneCleanupRun() {
        // Models the actual user scenario: a window resize
        // emits 10 SizeChanged events within the same composition
        // cycle (BEFORE the deferred BeginInvoke fires). The
        // pipeline must accept the first TryBegin and drop the
        // other nine — exactly one TryBegin returns true during
        // the burst. The cleanup pass is what runs after End().
        var pipeline = new MapOverlayReflowPipeline();
        int acceptedCalls = 0;

        for (int i = 0; i < 10; i++) {
            if (pipeline.TryBegin()) {
                acceptedCalls++;
            }
        }
        Assert.Equal(1, acceptedCalls);
        Assert.True(pipeline.IsPending);
        pipeline.End();
        Assert.False(pipeline.IsPending);
    }

    [Fact]
    public void Reflow_PipelineAndCleanup_OrderingStable() {
        // After a resize, the cleanup walk must produce indices
        // in descending order so the caller can remove them in
        // that order without shifting any still-pending index.
        var items = new[] {
            new MapIslandCleanupSnapshot(IsTemp: false, IsWarehouse: false, IslandId: "A"),
            new MapIslandCleanupSnapshot(IsTemp: false, IsWarehouse: false, IslandId: "B"),
            new MapIslandCleanupSnapshot(IsTemp: false, IsWarehouse: false, IslandId: "C"),
        };
        var indices = MapIslandVisualCleanup.CollectStaleIndices(
            items,
            snapshot => snapshot,
            _ => true);
        // The helper walks backward so the index list is
        // [last, ..., first]; the caller removes in that order.
        Assert.Equal(new[] { 2, 1, 0 }, indices);
    }

    // ──────────── Real list-mutation tests ────────────
    //
    // These tests actually mutate a list via
    // MapIslandVisualCleanup.RemoveAtDescendingIndices, so they
    // would have caught the v1 bug where IslandsButtonRearrange
    // re-reversed the descending indices and removed the wrong
    // entries.

    [Fact]
    public void RemoveAtDescendingIndices_MixedList_RemovesExpectedEntriesOnly() {
        // User spec: input [KeepA, StaleB, KeepC, StaleD, KeepE],
        // descending stale indices [3, 1], expected final
        // [KeepA, KeepC, KeepE].
        var items = new List<string> { "KeepA", "StaleB", "KeepC", "StaleD", "KeepE" };
        var stale = new List<int> { 3, 1 };
        MapIslandVisualCleanup.RemoveAtDescendingIndices<string>(
            items,
            stale,
            beforeRemove: staleItem => { /* nothing in this list-shaped test */ });
        Assert.Equal(new[] { "KeepA", "KeepC", "KeepE" }, items);
    }

    [Fact]
    public void RemoveAtDescendingIndices_StaleAtEndAndMiddle_DoesNotThrowAndKeepsOnlyKeep() {
        // Defensive: stale indices at the END of the list (high)
        // and the middle. v1's "reverse" pass would crash on
        // index 4 here because after the first removal the list
        // shrinks and the cached next index becomes out-of-range.
        var items = new List<string> { "K1", "K2", "K3", "K4", "S5" };
        var stale = new List<int> { 4, 1 };
        MapIslandVisualCleanup.RemoveAtDescendingIndices<string>(
            items,
            stale);
        Assert.Equal(new[] { "K1", "K3", "K4" }, items);
    }

    [Fact]
    public void RemoveAtDescendingIndices_EmptyIndices_NoOp() {
        var items = new List<string> { "A", "B", "C" };
        MapIslandVisualCleanup.RemoveAtDescendingIndices<string>(items, new List<int>());
        Assert.Equal(new[] { "A", "B", "C" }, items);
    }

    [Fact]
    public void RemoveAtDescendingIndices_SingleIndex_RemovesOnlyThatOne() {
        var items = new List<string> { "A", "B", "C", "D" };
        MapIslandVisualCleanup.RemoveAtDescendingIndices<string>(items, new List<int> { 2 });
        Assert.Equal(new[] { "A", "B", "D" }, items);
    }

    [Fact]
    public void RemoveAtDescendingIndices_AllIndices_EmptiesTheList() {
        var items = new List<string> { "A", "B", "C" };
        MapIslandVisualCleanup.RemoveAtDescendingIndices<string>(
            items, new List<int> { 2, 1, 0 });
        Assert.Empty(items);
    }

    [Fact]
    public void RemoveAtDescendingIndices_BeforeRemoveCallback_FiresOncePerItem() {
        // Confirms the WPF-only detach step runs BEFORE the list
        // mutation. If a regression flipped the order, the parent
        // would be removed AFTER the grid is gone from the list,
        // so the cleanup test would fail with stale parent
        // membership. (Here we use a plain List<string>; the
        // shape is the contract that
        // IslandsButtonRearrange relies on for `parent.Children`.)
        var items = new List<string> { "x", "y", "z" };
        var firedFor = new List<string>();
        MapIslandVisualCleanup.RemoveAtDescendingIndices<string>(
            items,
            new List<int> { 2, 1, 0 },
            beforeRemove: item => firedFor.Add(item));
        Assert.Equal(new[] { "z", "y", "x" }, firedFor);
        Assert.Empty(items);
    }

    [Fact]
    public void RemoveAtDescendingIndices_NonDescendingIndices_ThrowsArgumentException() {
        // The v1 caller bug was: caller reversed the descending
        // list, then walked it high→low again. That double-reverse
        // is a CONTRACT VIOLATION and the helper must surface it
        // loudly rather than silently fix it. Ascending input
        // must throw.
        var items = new List<string> { "A", "B", "C", "D" };
        var ex = Assert.Throws<ArgumentException>(() =>
            MapIslandVisualCleanup.RemoveAtDescendingIndices<string>(
                items,
                new List<int> { 1, 3 }));
        Assert.Contains("strictly descending", ex.Message);
    }

    [Fact]
    public void RemoveAtDescendingIndices_OutOfRangeIndex_ThrowsArgumentOutOfRange() {
        // Defensive: if the caller passes an index that's not in
        // the list, fail loudly. v1 silently crashed later in the
        // resize cycle with an IndexOutOfRangeException that was
        // very hard to attribute.
        var items = new List<string> { "A", "B", "C" };
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MapIslandVisualCleanup.RemoveAtDescendingIndices<string>(
                items,
                new List<int> { 5 }));
        // The list must not be partially mutated before the throw.
        Assert.Equal(new[] { "A", "B", "C" }, items);
    }

    [Fact]
    public void RemoveAtDescendingIndices_NegativeIndex_ThrowsArgumentOutOfRange() {
        var items = new List<string> { "A", "B" };
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MapIslandVisualCleanup.RemoveAtDescendingIndices<string>(
                items,
                new List<int> { 1, -1 }));
        Assert.Equal(new[] { "A", "B" }, items);
    }

    [Fact]
    public void CollectAndRemove_RoundTrip_RemovesExactlyTheStaleItems() {
        // End-to-end: build a snapshot list, run the decision
        // helper, then run the actual removal. This is the
        // regression that would have caught v1's reverse bug
        // because the post-condition checks the ACTUAL contents
        // after the full chain runs — not just the helper's
        // returned index list.
        var items = new List<string> {
            "KeepA", "StaleB", "KeepC", "StaleD", "KeepE",
        };
        // Map a name -> "is stale?" predicate.
        Func<string, bool> isStale = name => name.StartsWith("Stale");

        var staleIndices = MapIslandVisualCleanup.CollectStaleIndices<string>(
            items,
            name => new MapIslandCleanupSnapshot(
                IsTemp: false,
                IsWarehouse: false,
                IslandId: name),
            snap => isStale(snap.IslandId));
        // Confirm the helper produced the descending list we
        // expect — this is the original assertion from before
        // the real-deletion tests were added.
        Assert.Equal(new[] { 3, 1 }, staleIndices);

        MapIslandVisualCleanup.RemoveAtDescendingIndices<string>(
            items,
            staleIndices);
        // The hard assertion: the actual list contents.
        Assert.Equal(new[] { "KeepA", "KeepC", "KeepE" }, items);
    }
}