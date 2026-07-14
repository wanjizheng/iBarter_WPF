using iBarter.Navigation;
using Xunit;

namespace IslandNavigationTests;

public sealed class ShippingCorridorGraphTests {
    private static readonly NavigationPoint Halmad = new(558_999, 333_684);
    private static readonly NavigationPoint Hakoven = new(1_252_450, 547_567);

    [Fact]
    public void Ordinary_sea_keeps_direct_segment() {
        var from = new NavigationPoint(10, 20);
        var to = new NavigationPoint(40, 60);

        var path = ShippingCorridorGraph.BuildPath("Iliya", from, "Narvo", to);

        Assert.Equal([from, to], path);
        Assert.Equal(50, ShippingCorridorGraph.Distance("Iliya", from, "Narvo", to), 8);
    }

    [Theory]
    [InlineData("Iliya", "Hakoven")]
    [InlineData("Hakoven", "Iliya")]
    public void Every_external_right_region_trip_uses_halmad_gateway(string fromId, string toId) {
        var external = new NavigationPoint(100_000, 200_000);
        NavigationPoint from = fromId == "Hakoven" ? Hakoven : external;
        NavigationPoint to = toId == "Hakoven" ? Hakoven : external;

        var path = ShippingCorridorGraph.BuildPath(fromId, from, toId, to);

        Assert.Contains(path, point => IslandNavigationGeometry.Distance(point, Halmad) < 1);
        Assert.True(path.Count > 3);
        Assert.Equal(from, path[0]);
        Assert.Equal(to, path[^1]);
    }

    [Fact]
    public void Right_region_internal_trip_uses_confirmed_corridor_polyline() {
        var derko = new NavigationPoint(843_205, 415_735);

        var path = ShippingCorridorGraph.BuildPath("Halmad", Halmad, "Derko", derko);

        Assert.True(path.Count > 5);
        Assert.True(ShippingCorridorGraph.PathDistance(path)
                    > IslandNavigationGeometry.Distance(Halmad, derko));
    }

    [Fact]
    public void Display_leg_stays_direct_while_distance_uses_right_corridor() {
        var distancePath = ShippingCorridorGraph.BuildPath("Halmad", Halmad, "Hakoven", Hakoven);

        var displayPath = RouteDisplayGeometry.BuildDirectLeg(Halmad, Hakoven);

        Assert.True(distancePath.Count > 2);
        Assert.Equal([Halmad, Hakoven], displayPath);
    }

    [Fact]
    public void Grandiha_midnight_trip_uses_southern_corridor_in_both_directions() {
        var grandiha = new NavigationPoint(-559_743, -476_904);
        var midnight = new NavigationPoint(-321_664, -598_912);

        var outbound = ShippingCorridorGraph.BuildPath("Grandiha", grandiha, "Midnight", midnight);
        var inbound = ShippingCorridorGraph.BuildPath("Midnight", midnight, "Grandiha", grandiha);

        Assert.True(outbound.Count > 3);
        Assert.Equal(outbound.Reverse(), inbound);
        Assert.True(ShippingCorridorGraph.PathDistance(outbound)
                    > IslandNavigationGeometry.Distance(grandiha, midnight));
    }
}
