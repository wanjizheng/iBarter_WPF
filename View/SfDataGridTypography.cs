using Syncfusion.UI.Xaml.Grid;
using System.Windows;
using System.Windows.Media;

namespace iBarter.View {
    /// <summary>
    /// Applies application typography at the grid and column levels.
    /// Syncfusion themes and SfDataGrid.Deserialize can replace values that
    /// would otherwise be inherited from the containing Window/UserControl.
    /// </summary>
    internal static class SfDataGridTypography {
        internal const double GridFontSize = 14;
        internal const double GridRowHeight = 30;
        internal const double GridHeaderRowHeight = 32;

        internal static void Apply(SfDataGrid? grid) {
            if (grid is null || Application.Current?.Resources["AppFontFamily"] is not FontFamily fontFamily) {
                return;
            }

            Style? cellStyle = Application.Current.TryFindResource(
                "ReadableSfDataGridCellStyle") as Style;
            Style? headerStyle = Application.Current.TryFindResource(
                "ReadableSfDataGridHeaderStyle") as Style;

            // Local values take precedence over Syncfusion theme setters.
            grid.FontFamily = fontFamily;
            grid.FontSize = GridFontSize;
            grid.FontWeight = FontWeights.Normal;
            if (Application.Current.Resources["AppTextFormattingMode"] is TextFormattingMode formattingMode)
                TextOptions.SetTextFormattingMode(grid, formattingMode);
            if (Application.Current.Resources["AppTextRenderingMode"] is TextRenderingMode renderingMode)
                TextOptions.SetTextRenderingMode(grid, renderingMode);
            if (Application.Current.Resources["AppTextHintingMode"] is TextHintingMode hintingMode)
                TextOptions.SetTextHintingMode(grid, hintingMode);
            grid.RowHeight = GridRowHeight;
            grid.HeaderRowHeight = GridHeaderRowHeight;
            if (cellStyle != null) grid.CellStyle = cellStyle;
            if (headerStyle != null) grid.HeaderStyle = headerStyle;

            foreach (GridColumn column in grid.Columns) {
                ApplyColumnStyles(column, cellStyle, headerStyle);

                // Multi-column dropdowns create a second internal SfDataGrid.
                // Styling only the outer Planner column leaves that popup on
                // the Syncfusion theme font, so reinforce all of its columns.
                if (column is GridMultiColumnDropDownList dropdown && dropdown.Columns != null) {
                    foreach (GridColumn innerColumn in dropdown.Columns) {
                        ApplyColumnStyles(innerColumn, cellStyle, headerStyle);
                    }
                }
            }

            grid.InvalidateVisual();
        }

        private static void ApplyColumnStyles(
            GridColumn column,
            Style? cellStyle,
            Style? headerStyle) {
            if (cellStyle != null) column.CellStyle = cellStyle;
            if (headerStyle != null) column.HeaderStyle = headerStyle;
        }
    }
}
