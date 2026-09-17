using iBarter.Routing;
using Xunit;
namespace AutomaticRoutePlanningTests;
public class TaggedSearchFeedbackTests {
    [Fact] public void ToolbarConvertsMetresAndTracksOnlyStrictImprovements() {
        var feedback = new TaggedSearchFeedback();
        feedback.Update(new(2, 600, 10, 161000, 3));
        Assert.Equal(16100000, feedback.Snapshot.BestObjective!.Value.TotalDistance);
        feedback.Update(new(8, 600, 20, 161000, 4));
        Assert.Equal(TimeSpan.FromSeconds(2), feedback.Snapshot.LastImprovementElapsed);
        feedback.Update(new(12, 600, 30, 160000, 4));
        Assert.Equal(TimeSpan.FromSeconds(12), feedback.Snapshot.LastImprovementElapsed);
        feedback.Finish(new(null, "cancelled", Search: new(15, 30, ExtremeSearchTerminationReason.UserCancelled)), TimeSpan.FromSeconds(15));
        Assert.True(feedback.Snapshot.IsTerminal);
        Assert.Equal(ExtremeSearchTerminationReason.UserCancelled, feedback.Snapshot.TerminationReason);
        Assert.Equal(16000000, feedback.Snapshot.BestObjective!.Value.TotalDistance);
    }
    [Fact] public void FewerTripsDoesNotOverrideShorterDistance() {
        Assert.True(new RoutePlanObjective(4, 16000000, 0, 0, "").CompareTo(new(3, 16100000, 0, 0, "")) < 0);
    }
    [Fact] public void OldUnlabelledNumericTranslationStillDisplaysKilometres() {
        Assert.Contains("161.00 km", string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "distance {0:N1}", RouteDistanceDisplay.Ordinary(16100000)));
    }
}
