using iBarter.Navigation;
using Xunit;

namespace IslandNavigationTests;

public sealed class ShippingCorridorGraphTests {
    private static readonly NavigationPoint Halmad = new(558_999, 333_684);
    private static readonly NavigationPoint Hakoven = new(1_252_450, 547_567);
    private static readonly NavigationPoint Pilava = new(248_520, 198_926);
    private static readonly NavigationPoint Midnight = new(-321_664, -598_912);
    private static readonly NavigationPoint Epheria = new(-355_616, 32_650);
    private static readonly NavigationPoint Kuit = new(-348_843, 375_542);
    private static readonly NavigationPoint Teyamal = new(-524_725, 66_122);
    private static readonly NavigationPoint Grandiha = new(-559_743, -476_904);

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
    public void Sausan_to_arehaza_draws_direct_but_costs_the_right_corridor() {
        var sausan = new NavigationPoint(255_291, 142_486);
        var arehaza = new NavigationPoint(1_267_170, 177_948);

        var distancePath = ShippingCorridorGraph.BuildPath(
            "Sausan", sausan, "Arehaza", arehaza);
        var displayPath = RouteDisplayGeometry.BuildDirectLeg(sausan, arehaza);

        Assert.Equal([sausan, arehaza], displayPath);
        Assert.True(distancePath.Count > 10);
        Assert.Contains(distancePath, point => IslandNavigationGeometry.Distance(point, Halmad) < 1);
        Assert.True(ShippingCorridorGraph.PathDistance(distancePath)
                    > IslandNavigationGeometry.Distance(sausan, arehaza));
    }

    [Fact]
    public void Grandiha_midnight_trip_uses_southern_corridor_in_both_directions() {
        var outbound = ShippingCorridorGraph.BuildPath("Grandiha", Grandiha, "Midnight", Midnight);
        var inbound = ShippingCorridorGraph.BuildPath("Midnight", Midnight, "Grandiha", Grandiha);

        Assert.True(outbound.Count > 3);
        Assert.Equal(outbound.Reverse(), inbound);
        Assert.True(ShippingCorridorGraph.PathDistance(outbound)
                    > IslandNavigationGeometry.Distance(Grandiha, Midnight));
    }

    [Fact]
    public void Pilava_midnight_trip_uses_the_west_coast_in_both_directions() {
        var outbound = ShippingCorridorGraph.BuildPath("Pilava", Pilava, "Midnight", Midnight);
        var inbound = ShippingCorridorGraph.BuildPath("Midnight", Midnight, "Pilava", Pilava);

        Assert.Equal(outbound.Reverse(), inbound);
        Assert.Contains(Kuit, outbound);
        Assert.Contains(Teyamal, outbound);
        Assert.Contains(Grandiha, outbound);
        Assert.True(ShippingCorridorGraph.PathDistance(outbound)
                    > IslandNavigationGeometry.Distance(Pilava, Midnight) * 1.5);
    }

    [Fact]
    public void Midnight_epheria_trip_returns_along_the_west_coast() {
        var path = ShippingCorridorGraph.BuildPath("Midnight", Midnight, "Epheria", Epheria);

        Assert.Contains(Grandiha, path);
        Assert.Contains(Teyamal, path);
        Assert.True(ShippingCorridorGraph.PathDistance(path)
                    > IslandNavigationGeometry.Distance(Midnight, Epheria));
    }
}
