using iBarter.Navigation;
using Xunit;

namespace IslandNavigationTests;

public sealed class IslandNavigationGeometryTests {
    [Fact]
    public void Special_display_groups_are_exact_and_disjoint() {
        Assert.Equal(14, IslandNavigationGeometry.LeftInsetNames.Count);
        Assert.Equal(5, IslandNavigationGeometry.RightInsetNames.Count);
        Assert.Equal(2, IslandNavigationGeometry.BottomEdgeNames.Count);
        Assert.Equal(21, IslandNavigationGeometry.LeftInsetNames
            .Concat(IslandNavigationGeometry.RightInsetNames)
            .Concat(IslandNavigationGeometry.BottomEdgeNames).Distinct().Count());
        Assert.Equal(SpecialDisplayGroup.LeftInset, IslandNavigationGeometry.GetDisplayGroup("Rickun"));
        Assert.Equal(SpecialDisplayGroup.RightInset, IslandNavigationGeometry.GetDisplayGroup("Hakoven"));
        Assert.Equal(SpecialDisplayGroup.BottomEdge, IslandNavigationGeometry.GetDisplayGroup("Midnight"));
        Assert.Equal(SpecialDisplayGroup.MainMap, IslandNavigationGeometry.GetDisplayGroup("Iliya"));
    }
    [Fact]
    public void Distance_uses_world_coordinates() {
        var midnight = new NavigationPoint(-321_664, -598_912);
        var cox = new NavigationPoint(-747_393, 504_292);
        var rickun = new NavigationPoint(-816_036, 669_629);

        Assert.True(IslandNavigationGeometry.Distance(midnight, cox)
                    < IslandNavigationGeometry.Distance(midnight, rickun));
    }

    [Fact]
    public void ProjectToInset_preserves_one_scale_and_direction() {
        var points = new Dictionary<string, NavigationPoint> {
            ["northWest"] = new(0, 100),
            ["southEast"] = new(50, 0),
        };

        var projected = IslandNavigationGeometry.ProjectToInset(
            points, new NormalizedBounds(0, 0, 0.4, 0.3), 0.02);

        Assert.True(projected["northWest"].X < projected["southEast"].X);
        Assert.True(projected["northWest"].Y < projected["southEast"].Y);
        double sx = (projected["southEast"].X - projected["northWest"].X) / 50;
        double sy = (projected["southEast"].Y - projected["northWest"].Y) / 100;
        Assert.Equal(sx, sy, 10);
    }
}
