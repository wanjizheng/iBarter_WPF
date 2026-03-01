using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace iBarter {
    public sealed class StringSimilarityMatcher {
    private readonly List<string> _candidates = new List<string>();
    private readonly bool _ignoreCase;
    private readonly bool _removeDiacritics;

    public StringSimilarityMatcher(ArrayList items, bool ignoreCase = true, bool removeDiacritics = true) {
        _ignoreCase = ignoreCase;
        _removeDiacritics = removeDiacritics;
        if (items == null) return;
        foreach (var it in items) {
            if (it is string s && !string.IsNullOrWhiteSpace(s))
                _candidates.Add(Normalize(s));
        }
    }

    public void Add(string item) {
        if (!string.IsNullOrWhiteSpace(item))
            _candidates.Add(Normalize(item));
    }

    /// <summary>
    /// 返回最相似的候选项；如果没有候选，返回 null。score 为 0-100（越大越相似）
    /// </summary>
    public string FindBest(string input, out int score) {
        score = 0;
        if (string.IsNullOrWhiteSpace(input) || _candidates.Count == 0)
            return null;

        string norm = Normalize(input);
        string best = null;
        int bestScore = -1;

        foreach (var cand in _candidates) {
            int s = SimilarityScore(norm, cand); // 0-100
            // 分数高者优先；同分时选择更短编辑距离（更接近）
            if (s > bestScore) {
                bestScore = s;
                best = cand;
            }
        }

        score = bestScore < 0 ? 0 : bestScore;
        return best;
    }

        /// <summary>获取前 N 个最相似结果（已按分数降序）</summary>
        public List<(string item, int score)> TopN(string input, int n = 5) {
            var list = new List<(string item, int score)>();
            if (string.IsNullOrWhiteSpace(input) || _candidates.Count == 0 || n <= 0)
                return list;

            string norm = Normalize(input);
            foreach (var cand in _candidates)
                list.Add((cand, SimilarityScore(norm, cand)));

            list.Sort((a, b) => b.score.CompareTo(a.score)); // OK：已命名
            if (list.Count > n) list.RemoveRange(n, list.Count - n);
            return list;
        }

        // --- 内部实现 ---

        private string Normalize(string s) {
        var text = s.Trim();
        if (_ignoreCase) text = text.ToLowerInvariant();
        if (_removeDiacritics) {
            var formD = text.Normalize(NormalizationForm.FormD);
            var span = formD.AsSpan();
            var array = new char[span.Length];
            int idx = 0;
            for (int i = 0; i < span.Length; i++) {
                var uc = CharUnicodeInfo.GetUnicodeCategory(span[i]);
                if (uc != UnicodeCategory.NonSpacingMark)
                    array[idx++] = span[i];
            }
            text = new string(array, 0, idx).Normalize(NormalizationForm.FormC);
        }
        return text;
    }

    // 归一化 Levenshtein 相似度：score = (1 - dist / maxLen) * 100
    private static int SimilarityScore(string a, string b) {
        if (a == b) return 100;
        int maxLen = Math.Max(a.Length, b.Length);
        if (maxLen == 0) return 100;

        int dist = LevenshteinDistance(a, b);
        double sim = 1.0 - (double)dist / maxLen;
        int score = (int)Math.Round(sim * 100, MidpointRounding.AwayFromZero);
        if (score < 0) score = 0;
        if (score > 100) score = 100;
        return score;
    }

    // O(mn) 时间，O(min(m,n)) 空间的两行 DP 实现
    private static int LevenshteinDistance(string s, string t) {
        int n = s.Length, m = t.Length;
        if (n == 0) return m;
        if (m == 0) return n;

        // 始终让 t 为较长串，节省空间
        if (n > m) { var tmpS = s; s = t; t = tmpS; n = s.Length; m = t.Length; }

        var prev = new int[n + 1];
        var curr = new int[n + 1];

        for (int i = 0; i <= n; i++) prev[i] = i;

        for (int j = 1; j <= m; j++) {
            curr[0] = j;
            char tj = t[j - 1];
            for (int i = 1; i <= n; i++) {
                int cost = s[i - 1] == tj ? 0 : 1;
                int del = prev[i] + 1;
                int ins = curr[i - 1] + 1;
                int sub = prev[i - 1] + cost;
                int val = Math.Min(Math.Min(del, ins), sub);
                curr[i] = val;
            }
            // 交换 prev/curr
            var tmp = prev; prev = curr; curr = tmp;
        }
        return prev[n];
    }
}
}