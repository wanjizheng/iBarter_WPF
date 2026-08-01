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

            // Hoist the query normalize out of the per-item loop. The
            // previous version re-ran ChineseTextNormalizer.NormalizeForMatching
            // on the query inside Matches() once per (item x candidate) pair
            // -- with 275 items x 3 candidates = 825 redundant normalizations
            // per keystroke. IME pinyin TextChanged fires ~20/sec, so the
            // LCMapStringEx P/Invoke storms were stalling the UI thread.
            //
            // Now: normalize the query ONCE per keystroke, share it across
            // all 275 candidates, and skip the normalized path entirely
            // when the query has no CJK (the fast path in
            // NormalizeForMatching returns the OCR-variant map only,
            // which is already applied directly via MatchesCore).
            string normalizedQuery = ChineseTextNormalizer.NormalizeForMatching(query);
            bool queryHasCjk = ChineseTextNormalizer.ContainsCjk(normalizedQuery);

            if (item is Items barterItem) {
                return MatchesAny(query, normalizedQuery, queryHasCjk,
                    barterItem.ItemNameDisplay,
                    barterItem.ItemNameZhTw,
                    barterItem.ItemName);
            }

            if (item is Islands island) {
                return MatchesAny(query, normalizedQuery, queryHasCjk,
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

            if (string.IsNullOrEmpty(filterText)) {
                return false;
            }

            // Same hoist as FilterRecord: normalize filterText once and
            // share across all items instead of re-normalizing per row.
            string normalizedFilter = ChineseTextNormalizer.NormalizeForMatching(filterText);
            bool filterHasCjk = ChineseTextNormalizer.ContainsCjk(normalizedFilter);

            if (item is Items barterItem) {
                return MatchesAnyAutoComplete(filterText, normalizedFilter, filterHasCjk,
                    barterItem.ItemNameDisplay,
                    barterItem.ItemNameZhTw,
                    barterItem.ItemName);
            }

            if (item is Islands island) {
                return MatchesAnyAutoComplete(filterText, normalizedFilter, filterHasCjk,
                    island.IslandsNameDisplay,
                    island.IslandsNameZhTw,
                    island.IslandsName);
            }

            return false;
        }

        private bool MatchesAny(string query, string normalizedQuery, bool queryHasCjk, params string[] candidates) {
            var comparison = AllowCaseSensitiveFiltering
                ? StringComparison.CurrentCulture
                : StringComparison.CurrentCultureIgnoreCase;
            foreach (var candidate in candidates) {
                if (Matches(candidate, query, normalizedQuery, queryHasCjk, comparison)) {
                    return true;
                }
            }
            return false;
        }

        private bool Matches(string candidate, string query, string normalizedQuery, bool queryHasCjk, StringComparison comparison) {
            if (string.IsNullOrEmpty(candidate)) {
                return false;
            }

            // Cheap path first: raw candidate vs raw query.
            if (MatchesCore(candidate, query, comparison)) {
                return true;
            }

            // Skip the normalized path entirely when the query has no
            // CJK -- the OCR-variant map is already applied to ASCII
            // inputs by NormalizeForMatching's fast path, so a second
            // pass on the candidate cannot find anything the direct
            // IndexOf just missed.
            if (!queryHasCjk) {
                return false;
            }

            string normalizedCandidate = ChineseTextNormalizer.NormalizeForMatching(candidate);
            if (string.IsNullOrEmpty(normalizedCandidate) || string.IsNullOrEmpty(normalizedQuery)) {
                return false;
            }

            return MatchesCore(normalizedCandidate, normalizedQuery, comparison);
        }

        private bool MatchesAnyAutoComplete(string filterText, string normalizedFilter, bool filterHasCjk, params string[] candidates) {
            var comparison = AllowCaseSensitiveFiltering
                ? StringComparison.CurrentCulture
                : StringComparison.CurrentCultureIgnoreCase;
            foreach (var candidate in candidates) {
                if (MatchesAutoComplete(candidate, filterText, normalizedFilter, filterHasCjk, comparison)) {
                    return true;
                }
            }
            return false;
        }

        private bool MatchesAutoComplete(string candidate, string filterText, string normalizedFilter, bool filterHasCjk, StringComparison comparison) {
            if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(filterText)) {
                return false;
            }

            // Keep autocomplete aligned with incremental filtering. Item
            // columns in Planner and Scanner use SearchCondition.Contains,
            // so a distinctive middle or trailing fragment (for example
            // "纱帽" in "最高级纱帽箱子") must be eligible for selection too.
            if (MatchesBySearchCondition(
                    candidate, filterText, comparison, SearchCondition)) {
                return true;
            }

            // Same skip-the-normalized-path logic as Matches: ASCII
            // filters cannot benefit from the OCR-variant pass.
            if (!filterHasCjk || string.IsNullOrEmpty(normalizedFilter)) {
                return false;
            }

            string normalizedCandidate = ChineseTextNormalizer.NormalizeForMatching(candidate);
            return !string.IsNullOrEmpty(normalizedCandidate)
                   && MatchesBySearchCondition(
                       normalizedCandidate, normalizedFilter, comparison, SearchCondition);
        }

        private bool MatchesCore(string candidate, string query, StringComparison comparison) {
            return MatchesBySearchCondition(
                candidate, query, comparison, SearchCondition);
        }

        internal static bool MatchesBySearchCondition(
                string candidate,
                string query,
                StringComparison comparison,
                SearchCondition searchCondition) {
            return searchCondition switch {
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
