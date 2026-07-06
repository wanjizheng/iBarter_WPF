using System;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Cells;

namespace iBarter.View {
    internal sealed class LocalizedMultiColumnDropDownControl : SfMultiColumnDropDownControl {
        protected override bool FilterRecord(object item) {
            string query = SearchText;
            if (string.IsNullOrEmpty(query)) {
                query = Text;
            }
            if (string.IsNullOrEmpty(query)) {
                return true;
            }

            if (item is Items barterItem) {
                return MatchesAny(query,
                    barterItem.ItemNameDisplay,
                    barterItem.ItemNameZhTw,
                    barterItem.ItemName);
            }

            if (item is Islands island) {
                return MatchesAny(query,
                    island.IslandsNameDisplay,
                    island.IslandsNameZhTw,
                    island.IslandsName);
            }

            return base.FilterRecord(item);
        }

        protected override bool ProcessAppendText(object item, string exactValue, string filterText) {
            if (base.ProcessAppendText(item, exactValue, filterText)) {
                return true;
            }

            if (MatchesAutoComplete(exactValue, filterText)) {
                return true;
            }

            if (item is Items barterItem) {
                return MatchesAnyAutoComplete(filterText,
                    barterItem.ItemNameDisplay,
                    barterItem.ItemNameZhTw,
                    barterItem.ItemName);
            }

            if (item is Islands island) {
                return MatchesAnyAutoComplete(filterText,
                    island.IslandsNameDisplay,
                    island.IslandsNameZhTw,
                    island.IslandsName);
            }

            return false;
        }

        private bool MatchesAny(string query, params string[] candidates) {
            foreach (var candidate in candidates) {
                if (Matches(candidate, query)) {
                    return true;
                }
            }
            return false;
        }

        private bool Matches(string candidate, string query) {
            if (string.IsNullOrEmpty(candidate)) {
                return false;
            }

            var comparison = AllowCaseSensitiveFiltering
                ? StringComparison.CurrentCulture
                : StringComparison.CurrentCultureIgnoreCase;

            if (MatchesCore(candidate, query, comparison)) {
                return true;
            }

            string normalizedCandidate = ChineseTextNormalizer.NormalizeForMatching(candidate);
            string normalizedQuery = ChineseTextNormalizer.NormalizeForMatching(query);
            if (string.IsNullOrEmpty(normalizedCandidate) || string.IsNullOrEmpty(normalizedQuery)) {
                return false;
            }

            return MatchesCore(normalizedCandidate, normalizedQuery, comparison);
        }

        private bool MatchesAnyAutoComplete(string query, params string[] candidates) {
            foreach (var candidate in candidates) {
                if (MatchesAutoComplete(candidate, query)) {
                    return true;
                }
            }
            return false;
        }

        private bool MatchesAutoComplete(string candidate, string query) {
            if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(query)) {
                return false;
            }

            var comparison = AllowCaseSensitiveFiltering
                ? StringComparison.CurrentCulture
                : StringComparison.CurrentCultureIgnoreCase;

            if (candidate.StartsWith(query, comparison)) {
                return true;
            }

            string normalizedCandidate = ChineseTextNormalizer.NormalizeForMatching(candidate);
            string normalizedQuery = ChineseTextNormalizer.NormalizeForMatching(query);
            return !string.IsNullOrEmpty(normalizedCandidate)
                   && !string.IsNullOrEmpty(normalizedQuery)
                   && normalizedCandidate.StartsWith(normalizedQuery, comparison);
        }

        private bool MatchesCore(string candidate, string query, StringComparison comparison) {
            return SearchCondition switch {
                SearchCondition.Equals     => string.Equals(candidate, query, comparison),
                SearchCondition.Contains   => candidate.IndexOf(query, comparison) >= 0,
                SearchCondition.StartsWith => candidate.StartsWith(query, comparison),
                _                          => candidate.StartsWith(query, comparison),
            };
        }
    }

    internal sealed class LocalizedMultiColumnDropDownRenderer : GridCellMultiColumnDropDownRenderer {
        protected override SfMultiColumnDropDownControl OnCreateEditUIElement() {
            return new LocalizedMultiColumnDropDownControl();
        }
    }
}
