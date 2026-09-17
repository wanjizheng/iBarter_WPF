namespace iBarter.Routing;

/// <summary>Adapts TAG metres to the existing toolbar's world-centimetre objective.</summary>
public sealed class TaggedSearchFeedback {
    private double? best;
    private TimeSpan? improved;
    public ExtremeSearchProgressSnapshot Snapshot { get; private set; } = new(TimeSpan.Zero, null, null, null, ExtremeSearchTerminationReason.None);
    public void Update(TaggedSearchProgress progress) {
        var elapsed = TimeSpan.FromSeconds(progress.ElapsedSeconds);
        if (progress.BestDistance is double distance && (best is null || distance < best)) { best = distance; improved = elapsed; }
        Snapshot = new(elapsed, best is double b ? new RoutePlanObjective(progress.BestRoutes ?? 0, b * 100, 0, 0, "") : null,
            improved, improved is TimeSpan last ? elapsed - last : null, ExtremeSearchTerminationReason.None);
    }
    public void Finish(TaggedTransportResult? result, TimeSpan elapsed) {
        if (result?.Plan is { } plan) Update(new(result.Search?.ElapsedSeconds ?? elapsed.TotalSeconds, 0,
            result.Search?.Candidates ?? 0, plan.Distance, result.PlannedRequest is { } r ? TaggedTransportRoutes.Build(r, plan).Length : 0));
        Snapshot = Snapshot with { Elapsed = TimeSpan.FromSeconds(result?.Search?.ElapsedSeconds ?? elapsed.TotalSeconds),
            TerminationReason = result?.Search?.Termination ?? ExtremeSearchTerminationReason.Error };
    }
}
