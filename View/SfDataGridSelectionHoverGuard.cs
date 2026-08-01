using Syncfusion.Data;
using Syncfusion.UI.Xaml.Grid;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace iBarter.View {
    /// <summary>
    /// Keeps Syncfusion's row-hover foreground from overriding the foreground
    /// of an already selected row.
    /// </summary>
    public static class SfDataGridSelectionHoverGuard {
        private sealed class HoverState {
            public VirtualizingCellsControl? HoveredRow { get; set; }
            public int HoveredRowIndex { get; set; } = -1;
            public HashSet<GridCell> ForcedForegroundCells { get; } = new();
            public bool RenderRefreshPending { get; set; }
        }

        private static readonly ConditionalWeakTable<SfDataGrid, HoverState> HoverStates = new();

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(SfDataGridSelectionHoverGuard),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static void SetIsEnabled(DependencyObject element, bool value) {
            element.SetValue(IsEnabledProperty, value);
        }

        public static bool GetIsEnabled(DependencyObject element) {
            return (bool)element.GetValue(IsEnabledProperty);
        }

        private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e) {
            if (element is not SfDataGrid grid) {
                return;
            }

            if ((bool)e.NewValue) {
                // handledEventsToo is important here: Syncfusion handles the
                // routed mouse event while applying its own hover foreground.
                // Running on the bubbling event lets this guard be the final
                // writer for an already-selected row.
                grid.AddHandler(Mouse.MouseMoveEvent, new MouseEventHandler(Grid_MouseMove), true);
                grid.MouseLeave += Grid_MouseLeave;
                return;
            }

            grid.RemoveHandler(Mouse.MouseMoveEvent, new MouseEventHandler(Grid_MouseMove));
            grid.MouseLeave -= Grid_MouseLeave;
            ClearHoveredRow(grid);
            HoverStates.Remove(grid);
        }

        private static void Grid_MouseMove(object sender, MouseEventArgs e) {
            if (sender is not SfDataGrid grid) {
                return;
            }

            var state = HoverStates.GetOrCreateValue(grid);
            var row = FindVisualParent<VirtualizingCellsControl>(e.OriginalSource as DependencyObject);
            if (!ReferenceEquals(state.HoveredRow, row)) {
                state.HoveredRow?.ClearValue(VirtualizingCellsControl.RowHoverForegroundProperty);
                ClearCellForegroundOverrides(state);
                state.HoveredRow = row;
                state.HoveredRowIndex = ResolveRowIndex(row, e.OriginalSource as DependencyObject);
            }

            if (row == null || !IsSelectableDataRowIndex(state.HoveredRowIndex)) {
                return;
            }

            if (IsSelectedRow(grid, row, state.HoveredRowIndex)) {
                row.SetCurrentValue(
                    VirtualizingCellsControl.RowHoverForegroundProperty,
                    grid.SelectionForegroundBrush);
                ForceVisibleCellForegrounds(row, grid.SelectionForegroundBrush, state);
                SchedulePostHoverRefresh(grid, row, state);
            }
            else {
                row.ClearValue(VirtualizingCellsControl.RowHoverForegroundProperty);
                ClearCellForegroundOverrides(state);
            }
        }

        private static bool IsSelectedRow(
            SfDataGrid grid,
            VirtualizingCellsControl row,
            int rowIndex) {
            // Prefer Syncfusion's own selected-row collection.  It remains
            // authoritative even when virtualization wraps DataContext in a
            // RecordEntry or reuses the visible row container.
            var selectedRows = grid.SelectionController?.SelectedRows;
            if (IsSelectableDataRowIndex(rowIndex)
                && selectedRows?.Contains(rowIndex) == true) {
                return true;
            }

            var rowData = row.DataContext is RecordEntry record ? record.Data : row.DataContext;
            return selectedRows?.ContainsObject(rowData) == true
                || ReferenceEquals(rowData, grid.SelectedItem)
                || grid.SelectedItems.Contains(rowData);
        }

        private static bool IsSelectableDataRowIndex(int rowIndex) {
            // Syncfusion's GridSelectedRowsCollection.Find(int) rejects zero
            // and negative indexes. Header/filter/popup mouse routes can resolve
            // to row zero, so never pass a non-data index into Contains(int).
            return rowIndex > 0;
        }

        private static int ResolveRowIndex(
            VirtualizingCellsControl? row,
            DependencyObject? originalSource) {
            if (row == null) {
                return -1;
            }

            var sourceCell = FindVisualParent<GridCell>(originalSource);
            if (sourceCell?.ColumnBase != null) {
                return sourceCell.ColumnBase.RowIndex;
            }

            foreach (var cell in FindVisualDescendants<GridCell>(row)) {
                if (cell.ColumnBase != null) {
                    return cell.ColumnBase.RowIndex;
                }
            }

            return -1;
        }

        private static void ForceVisibleCellForegrounds(
            VirtualizingCellsControl row,
            Brush selectionForeground,
            HoverState state) {
            foreach (var cell in FindVisualDescendants<GridCell>(row)) {
                // Foreground is what both the normal TextBlock template and
                // Syncfusion's lightweight drawing renderer finally consume.
                // A local value outranks the hover trigger that turns it black.
                cell.SetCurrentValue(Control.ForegroundProperty, selectionForeground);
                cell.SetCurrentValue(GridCell.SelectionForegroundBrushProperty, selectionForeground);
                cell.InvalidateVisual();
                state.ForcedForegroundCells.Add(cell);
            }

            row.InvalidateVisual();
        }

        private static void SchedulePostHoverRefresh(
            SfDataGrid grid,
            VirtualizingCellsControl row,
            HoverState state) {
            if (state.RenderRefreshPending) {
                return;
            }

            state.RenderRefreshPending = true;
            grid.Dispatcher.BeginInvoke(
                DispatcherPriority.Render,
                new Action(() => {
                    state.RenderRefreshPending = false;
                    if (!ReferenceEquals(state.HoveredRow, row)
                        || !row.IsMouseOver
                        || !IsSelectedRow(grid, row, state.HoveredRowIndex)) {
                        return;
                    }

                    // Syncfusion performs one more foreground update during
                    // its deferred hover render.  Re-apply immediately before
                    // WPF paints so the selected row cannot regress to black.
                    row.SetCurrentValue(
                        VirtualizingCellsControl.RowHoverForegroundProperty,
                        grid.SelectionForegroundBrush);
                    ForceVisibleCellForegrounds(row, grid.SelectionForegroundBrush, state);
                }));
        }

        private static void ClearCellForegroundOverrides(HoverState state) {
            foreach (var cell in state.ForcedForegroundCells) {
                cell.ClearValue(Control.ForegroundProperty);
                cell.ClearValue(GridCell.SelectionForegroundBrushProperty);
                cell.InvalidateVisual();
            }

            state.ForcedForegroundCells.Clear();
        }

        private static void Grid_MouseLeave(object sender, MouseEventArgs e) {
            if (sender is SfDataGrid grid) {
                ClearHoveredRow(grid);
            }
        }

        private static void ClearHoveredRow(SfDataGrid grid) {
            if (!HoverStates.TryGetValue(grid, out var state)) {
                return;
            }

            state.HoveredRow?.ClearValue(VirtualizingCellsControl.RowHoverForegroundProperty);
            ClearCellForegroundOverrides(state);
            state.HoveredRow = null;
            state.HoveredRowIndex = -1;
        }

        private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root)
            where T : DependencyObject {
            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < childCount; index++) {
                var child = VisualTreeHelper.GetChild(root, index);
                if (child is T match) {
                    yield return match;
                }

                foreach (var descendant in FindVisualDescendants<T>(child)) {
                    yield return descendant;
                }
            }
        }

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject {
            while (child != null) {
                if (child is T parent) {
                    return parent;
                }

                child = child switch {
                    Visual or Visual3D => VisualTreeHelper.GetParent(child),
                    FrameworkContentElement content => content.Parent,
                    ContentElement content => ContentOperations.GetParent(content),
                    _ => null,
                };
            }

            return null;
        }
    }
}
