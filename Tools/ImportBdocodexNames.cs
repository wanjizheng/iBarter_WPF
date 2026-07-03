// SPDX-License-Identifier: MIT
//
// iBarter localization — author-time BDOCODEX scraper (Phase 4).
//
// Hits https://bdocodex.com/tw/item/{id}/ for every ItemID in
// Resources/Items.csv, extracts the Traditional Chinese (zh-TW) name
// from the H1 of the rendered page, and writes Resources/Items.zh-TW.csv
// as a sidecar (one row per ItemID, 3 columns: ItemID,NameZhTW,Confidence).
//
// Islands cannot be scraped the same way: bdocodex has no per-island
// page; its world map and barter listings are JavaScript-rendered so
// no static HTML carries the zh-TW island names.  Islands.zh-TW.csv
// is therefore hand-curated; this tool only produces Items.zh-TW.csv.
//
// Usage in-app:
//   Tools menu → Import Bdocodex Names (zh-TW)
// This kicks off the async work on a background thread; progress is
// surfaced via App.myCFun.Log().  The CSV is rewritten in place; an
// existing file is overwritten.  Re-runs are safe and idempotent.
//
// Not part of the steady-state runtime: the in-app menu item survives
// because users occasionally edit Items.csv and want a re-pull, but
// the importer is one-shot semantically and ships as an editor tool
// rather than a service.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace iBarter.Tools {

    /// <summary>
    ///     One-shot importer that rewrites <c>Resources/Items.zh-TW.csv</c>
    ///     (sidecar to <c>Resources/Items.csv</c>) by hitting
    ///     <c>bdocodex.com/tw/item/{id}/</c> for every parseable ItemID in
    ///     <c>App.listItems</c>.  Rate-limited to ~5 req/sec to stay polite
    ///     to bdocodex; one HttpClient instance is reused for the whole run.
    /// </summary>
    public class ImportBdocodexNames {

        private readonly HttpClient _http;
        private readonly Action<string> _log;

        // Anchor used to scrub and decode a JSON-ish / CSV-cell field. We use
        // CsvHelper elsewhere in the codebase; this importer hand-rolls the
        // quoting so it has no external NuGet deps.
        private static readonly Regex H1StripPrefix = new(
            @"^\s*[\[【]\s*\d+\s*階段\s*[\]】]\s*",
            RegexOptions.Compiled);

        private static readonly Regex H1Element = new(
            @"<h1[^>]*>\s*(?<body>[^<]+?)\s*</h1>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public ImportBdocodexNames(HttpClient http, Action<string> logger) {
            _http = http;
            _log = logger;
        }

        /// <summary>
        ///     Drives the import.  <paramref name="ct"/> is honoured so the
        ///     caller can cancel via menu / window close.  Returns the number
        ///     of rows written to Items.zh-TW.csv.
        /// </summary>
        public async Task<int> RunAsync(
            IEnumerable<Items> items,
            string outputCsvPath,
            CancellationToken ct = default) {

            var sb = new StringBuilder();
            sb.AppendLine("ItemID,NameZhTW,Confidence");
            int rows = 0;
            int failed = 0;

            using var rateLimiter = new SemaphoreSlim(1, 1);
            foreach (var item in items) {
                ct.ThrowIfCancellationRequested();

                // bdocodex Item routes are numeric only.  App.listItems mixes
                // numeric IDs ("800066") with a few pseudo-rows where ItemID
                // isn't a pure number ("Gold Bar 1,000G" has ItemID "10" but
                // other rows may not) - silently skip the non-numeric ones
                // and let the CSV stay focused on page-addressable items.
                if (item is null
                    || string.IsNullOrWhiteSpace(item.ItemID)
                    || !Regex.IsMatch(item.ItemID, @"^\d+$")) {
                    continue;
                }

                await rateLimiter.WaitAsync(ct);
                try {
                    var (name, confidence) = await FetchZhTwNameAsync(item.ItemID, ct);
                    if (string.IsNullOrWhiteSpace(name)) {
                        failed++;
                        sb.AppendLine($"{item.ItemID},,low");
                        _log($"[ImportBdocodexNames] {item.ItemID} ({item.ItemName}): empty H1 — kept empty for manual fill");
                    } else {
                        sb.AppendLine($"{item.ItemID},{QuoteCsv(name)},{confidence}");
                        rows++;
                    }
                }
                catch (Exception ex) {
                    failed++;
                    sb.AppendLine($"{item.ItemID},,low");
                    _log($"[ImportBdocodexNames] {item.ItemID} ({item.ItemName}): {ex.GetType().Name} {ex.Message}");
                }
                finally {
                    rateLimiter.Release();
                    // ~5 req/sec - respectful to bdocodex and keeps the run under 60s for 275 items.
                    await Task.Delay(220, ct);
                }
            }

            // Always emit, even if every fetch failed - the sidecar file
            // existing with whatever rows we did get is more useful than
            // missing the file entirely.
            Directory.CreateDirectory(Path.GetDirectoryName(outputCsvPath)!);
            File.WriteAllText(outputCsvPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            _log($"[ImportBdocodexNames] Done. Wrote {rows} rows to {outputCsvPath} ({failed} failures).");
            return rows;
        }

        private async Task<(string Name, string Confidence)> FetchZhTwNameAsync(
            string itemId,
            CancellationToken ct) {

            var url = $"https://bdocodex.com/tw/item/{itemId}/";
            var html = await _http.GetStringAsync(url, ct);

            var m = H1Element.Match(html);
            if (!m.Success) {
                return ("", "low");
            }

            var raw = WebUtility.HtmlDecode(m.Groups["body"].Value).Trim();
            var stripped = H1StripPrefix.Replace(raw, "").Trim();

            // The page also lists the Korean name in some headers; sanity-
            // check that the H1 isn't accidentally the Korean one by
            // requiring the stripped string to contain at least one CJK
            // Unified Ideograph.
            bool hasCjk = stripped.Any(c => c >= 0x4E00 && c <= 0x9FFF);

            return hasCjk ? (stripped, "high") : (raw, "medium");
        }

        private static string QuoteCsv(string s) {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
