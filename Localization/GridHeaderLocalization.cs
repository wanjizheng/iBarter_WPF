// SPDX-License-Identifier: MIT
//
// iBarter localization — Syncfusion SfDataGrid column-header helper.
//
// Syncfusion's GridTextColumn.HeaderText is a plain CLR property, not a
// DependencyProperty, so {DynamicResource} cannot refresh it on language
// change.  Instead each View subscribes to LanguageService.LanguageChanged
// and walks its grid's columns, replacing HeaderText from a
// MappingName -> resource key map.
//
// Phase 2 of the i18n refactor.

using System.Collections.Generic;
using Syncfusion.UI.Xaml.Grid;

namespace iBarter.Localization {

    public static class GridHeaderLocalization {

        /// <summary>
        ///     Walks <paramref name="grid"/>'s columns; for every GridTextColumn whose
        ///     <c>MappingName</c> appears in <paramref name="mappingNameToKey"/>,
        ///     sets <c>HeaderText</c> to the current
        ///     <see cref="LanguageService.Localize(string, object[])"/> value for
        ///     that key.  Other column types (e.g. GridImageColumn, GridMultiColumn
        ///     DropDownList) skip the assignment since they're not text columns,
        ///     and PropertyDescriptor-style dropdowns own their inner column list
        ///     (those get handled by the per-View helper that walks the dropdown's
        ///     Columns collection with a dedicated inner-key map).
        /// </summary>
        public static void ApplyHeaders(
            SfDataGrid grid,
            IReadOnlyDictionary<string, string> mappingNameToKey) {

            if (grid is null || mappingNameToKey is null) {
                return;
            }

            foreach (var col in grid.Columns) {
                if (col is GridTextColumn tc
                    && !string.IsNullOrEmpty(tc.MappingName)
                    && mappingNameToKey.TryGetValue(tc.MappingName, out var key)) {
                    tc.HeaderText = LanguageService.Instance.Localize(key);
                }
            }
        }
    }
}
