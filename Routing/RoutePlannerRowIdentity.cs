using System.Text;

namespace iBarter.Routing;

/// <summary>
/// Builds the stable identity string the routing layer uses to match a
/// Planner row across reloads, edits, progress toggles, and reorder.
///
/// <para>Audit 5 (round 2): the v1 RowId included the planner collection
/// index, which made it unstable against reorder/filter/sort — every
/// <c>.OrderBy</c> on the grid would silently remap every CK to a
/// different BarterStep. The v2 schema uses ONLY business keys:
/// (IslandName, Item1Id, Item2Id).  Duplicate (Island, Item1, Item2)
/// triples (e.g. two "Iliya 800049 → 10" rows) get a deterministic
/// <c>#N</c> suffix based on the first-occurrence order in the supplied
/// collection; the same data always produces the same suffix.</para>
///
/// <para>This RowId is the ONLY identity that survives reload from
/// <c>automatic-route-plan.json</c>; downstream contracts that depend on
/// a stable identity (the verifier's row lookup, the CK progress
/// pipeline, the map double-click handler) all consume this string.</para>
/// </summary>
public static class RoutePlannerRowIdentity {
    /// <summary>
    /// Builds the full per-collection identity including a deterministic
    /// duplicate-disambiguator.  Order of <paramref name="rows"/> must be
    /// the canonical order the rest of the routing pipeline uses
    /// (i.e. the order they were inserted into the planner collection).
    /// </summary>
    public static string Create(
        int index, string islandId, string item1Id, string item2Id) {
        if (string.IsNullOrEmpty(islandId)) return $"INVALID:{index}";
        if (string.IsNullOrEmpty(item1Id) || string.IsNullOrEmpty(item2Id))
            return $"INVALID:{index}";
        return BuildBase(islandId, item1Id, item2Id);
    }

    /// <summary>
    /// Bulk variant: assigns each row its canonical RowId, disambiguating
    /// duplicate (Island, Item1, Item2) triples with a <c>#N</c> suffix in
    /// first-seen order.  Two callers with identical input rows always
    /// receive the same per-row identity, regardless of any prior sort.
    /// </summary>
    public static IReadOnlyList<string> CreateForRows(
        IEnumerable<(int index, string islandId, string item1Id, string item2Id)> rows) {
        var result = new List<string>();
        var occurrence = new Dictionary<string, int>(StringComparer.Ordinal);
        var baseToIndices = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        // First pass: assign base ids; track how many duplicates each base sees.
        foreach (var row in rows) {
            string baseId = BuildBase(row.islandId, row.item1Id, row.item2Id);
            if (!baseToIndices.TryGetValue(baseId, out var list)) {
                list = new List<int>();
                baseToIndices[baseId] = list;
            }
            list.Add(result.Count);
            occurrence[baseId] = 0;
            result.Add(baseId);
        }
        // Second pass: for each base that occurs more than once, assign
        // #1, #2, ... to the rows in first-seen order.
        for (int i = 0; i < result.Count; i++) {
            string baseId = result[i];
            if (baseToIndices[baseId].Count <= 1) continue;
            int nth = ++occurrence[baseId];
            result[i] = $"{baseId}#{nth}";
        }
        return result;
    }

    private static string BuildBase(string islandId, string item1Id, string item2Id) {
        var sb = new StringBuilder(64);
        sb.Append(islandId).Append(':').Append(item1Id).Append(':').Append(item2Id);
        return sb.ToString();
    }
}