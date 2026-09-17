using System.Text;

namespace iBarter.Routing;

public readonly record struct RouteTaskCompletionDecision(
    bool IsValid,
    bool CompletesPlannerRow,
    int RemainingExchangeQuantity);

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

    public static bool IsSegmentId(string taskRowId) =>
        !string.IsNullOrEmpty(taskRowId)
        && taskRowId.StartsWith(SplitPrefix, StringComparison.Ordinal)
        && !StringComparer.Ordinal.Equals(PlannerRowId(taskRowId), taskRowId);

    public static bool IsCompleted(string taskRowId, IReadOnlySet<string> completedPlannerRowIds) =>
        completedPlannerRowIds.Contains(taskRowId)
        || completedPlannerRowIds.Contains(PlannerRowId(taskRowId));

    public static bool AreAllTasksCompleted(
        string plannerRowId,
        IEnumerable<string> taskRowIds,
        IReadOnlySet<string> completedTaskRowIds) {
        var siblings = taskRowIds
            .Where(taskRowId => StringComparer.Ordinal.Equals(
                PlannerRowId(taskRowId), plannerRowId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return siblings.Length > 0
            && siblings.All(completedTaskRowIds.Contains);
    }

    public static bool TryGetExchangeCount(
        BarterStep step,
        int item1PerExchange,
        int item2PerExchange,
        out int exchangeCount) {
        exchangeCount = 0;
        if (step is null || item1PerExchange <= 0 || item2PerExchange <= 0
            || step.Consumed.Quantity <= 0 || step.Produced.Quantity <= 0
            || step.Consumed.Quantity % item1PerExchange != 0
            || step.Produced.Quantity % item2PerExchange != 0)
            return false;

        int consumedCount = step.Consumed.Quantity / item1PerExchange;
        int producedCount = step.Produced.Quantity / item2PerExchange;
        if (consumedCount <= 0 || consumedCount != producedCount) return false;
        exchangeCount = consumedCount;
        return true;
    }

    public static RouteTaskCompletionDecision DecideCompletion(
        int plannerExchangeQuantity,
        int taskExchangeCount) {
        if (plannerExchangeQuantity <= 0 || taskExchangeCount <= 0
            || taskExchangeCount > plannerExchangeQuantity)
            return new RouteTaskCompletionDecision(false, false, plannerExchangeQuantity);
        return taskExchangeCount == plannerExchangeQuantity
            ? new RouteTaskCompletionDecision(true, true, plannerExchangeQuantity)
            : new RouteTaskCompletionDecision(
                true, false, plannerExchangeQuantity - taskExchangeCount);
    }
}
