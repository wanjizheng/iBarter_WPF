using System.Text;

namespace iBarter.Routing;

/// <summary>
/// Keeps a capacity-split route task unique for the solver while preserving
/// the stable PlannerRowId used by completion, selection and UI workflows.
/// </summary>
public static class RouteTaskIdentity {
    private const string SplitPrefix = "br-split-v1:";

    public static string CreateSegmentId(
        string plannerRowId,
        int segmentIndex,
        int segmentCount) {
        if (segmentCount <= 1) return plannerRowId;
        if (segmentIndex < 0 || segmentIndex >= segmentCount)
            throw new ArgumentOutOfRangeException(nameof(segmentIndex));

        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(plannerRowId))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return $"{SplitPrefix}{segmentIndex + 1}:{segmentCount}:{encoded}";
    }

    public static string PlannerRowId(string taskRowId) {
        if (string.IsNullOrEmpty(taskRowId)
            || !taskRowId.StartsWith(SplitPrefix, StringComparison.Ordinal))
            return taskRowId;

        string[] parts = taskRowId.Split(':', 4);
        if (parts.Length != 4 || !Int32.TryParse(parts[1], out int part)
            || !Int32.TryParse(parts[2], out int count)
            || part <= 0 || count <= 1 || part > count)
            return taskRowId;

        try {
            string encoded = parts[3].Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight(encoded.Length + ((4 - encoded.Length % 4) % 4), '=');
            return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (FormatException) {
            return taskRowId;
        }
    }

    public static bool IsCompleted(string taskRowId, IReadOnlySet<string> completedPlannerRowIds) =>
        completedPlannerRowIds.Contains(PlannerRowId(taskRowId));
}
