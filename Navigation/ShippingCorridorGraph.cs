namespace iBarter.Navigation;

using System.Collections.Frozen;

/// <summary>
/// Small, local navigation graph for sea passages where a straight line cuts over land.
/// The returned polyline is the common source for both route cost and map rendering.
/// </summary>
public static class ShippingCorridorGraph {
    private static readonly FrozenSet<string> RightRegion = new[] {
        "Halmad", "Kashuma", "Derko", "Hakoven", "Arehaza",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, NavigationPoint> RightNodes =
        new Dictionary<string, NavigationPoint>(StringComparer.Ordinal) {
            ["Halmad"] = new(558_999, 333_684),
            ["Kashuma"] = new(589_314, 372_650),
            ["Derko"] = new(843_205, 415_735),
            ["Hakoven"] = new(1_252_450, 547_567),
            ["Arehaza"] = new(1_267_170, 177_948),
        };

    private static readonly CorridorEdge[] RightEdges = [
        Edge("Halmad", "Kashuma", [
            (8664,5705),(8679,5690),(8679,5675),(8664,5660),(8661,5642),
            (8667,5606),(8679,5588),(8685,5582),(8691,5582),(8703,5597),
            (8703,5609),(8715,5615),(8754,5591),(8769,5576),
        ]),
        Edge("Kashuma", "Derko", [
            (8769,5576),(8796,5552),(8811,5528),(8835,5516),(9006,5459),
            (9084,5393),(9123,5390),(9153,5360),(9195,5339),(9237,5327),
            (9357,5303),(9426,5312),(9432,5318),(9435,5348),(9534,5438),
            (9549,5444),(9579,5450),(9597,5447),(9606,5441),(9609,5438),
        ]),
        Edge("Derko", "Hakoven", [
            (9609,5438),(9855,5513),(9876,5513),(9888,5489),(9885,5294),
            (9876,5282),(9858,5246),(9858,5234),(9870,5171),(9993,4985),
            (10014,4970),(10131,4937),(10170,4931),(10314,4919),(10434,4928),
            (10458,4940),(10905,5054),(10986,5054),(10995,5051),(11010,5039),
            (11013,5036),
        ]),
        Edge("Hakoven", "Arehaza", [
            (11013,5036),(11073,5864),(11064,6086),(11007,6209),
        ]),
        Edge("Derko", "Arehaza", [
            (9609,5438),(9855,5513),(9876,5513),(9888,5489),(9885,5294),
            (9876,5282),(9858,5246),(9858,5234),(9870,5171),(9993,4985),
            (10014,4970),(10131,4937),(10170,4931),(10314,4919),(10353,4922),
            (10566,4967),(10788,5201),(10824,5261),(11064,5783),(11070,5822),
            (11073,5870),(11061,6092),(11007,6209),
        ]),
    ];

    private static readonly CorridorEdge SouthernEdge = new(
        "Grandiha", "Midnight",
        JoinEndpoints(
            new NavigationPoint(-559_743, -476_904),
            [
                (4952,8387),(4958,8411),(4919,8579),(4919,8597),
                (5033,8702),(5543,8705),(5732,8792),
            ],
            new NavigationPoint(-321_664, -598_912)));

    public static IReadOnlyList<NavigationPoint> BuildPath(
        string fromIslandId,
        NavigationPoint from,
        string toIslandId,
        NavigationPoint to) {
        bool fromRight = RightRegion.Contains(fromIslandId);
        bool toRight = RightRegion.Contains(toIslandId);

        if (fromRight && toRight)
            return ReplaceEndpoints(BuildRightPath(fromIslandId, toIslandId), from, to);

        if (!fromRight && toRight) {
            var inside = BuildRightPath("Halmad", toIslandId);
            return Collapse([from, .. inside, to]);
        }

        if (fromRight && !toRight) {
            var inside = BuildRightPath(fromIslandId, "Halmad");
            return Collapse([from, .. inside, to]);
        }

        if (Matches(SouthernEdge, fromIslandId, toIslandId)) {
            var path = Oriented(SouthernEdge, fromIslandId);
            return ReplaceEndpoints(path, from, to);
        }

        return Collapse([from, to]);
    }

    public static double Distance(
        string fromIslandId,
        NavigationPoint from,
        string toIslandId,
        NavigationPoint to) =>
        PathDistance(BuildPath(fromIslandId, from, toIslandId, to));

    public static double PathDistance(IReadOnlyList<NavigationPoint> path) {
        double result = 0;
        for (int i = 1; i < path.Count; i++)
            result += IslandNavigationGeometry.Distance(path[i - 1], path[i]);
        return result;
    }

    private static IReadOnlyList<NavigationPoint> BuildRightPath(string fromId, string toId) {
        if (StringComparer.Ordinal.Equals(fromId, toId))
            return [RightNodes[fromId]];

        var distances = RightNodes.Keys.ToDictionary(x => x, _ => double.PositiveInfinity, StringComparer.Ordinal);
        var previous = new Dictionary<string, CorridorEdge>(StringComparer.Ordinal);
        var unvisited = new HashSet<string>(RightNodes.Keys, StringComparer.Ordinal);
        distances[fromId] = 0;

        while (unvisited.Count > 0) {
            string current = unvisited.OrderBy(x => distances[x]).ThenBy(x => x, StringComparer.Ordinal).First();
            unvisited.Remove(current);
            if (current == toId || double.IsPositiveInfinity(distances[current])) break;
            foreach (var edge in RightEdges.Where(x => x.A == current || x.B == current)) {
                string neighbour = edge.A == current ? edge.B : edge.A;
                if (!unvisited.Contains(neighbour)) continue;
                double candidate = distances[current] + PathDistance(edge.Points);
                if (candidate >= distances[neighbour]) continue;
                distances[neighbour] = candidate;
                previous[neighbour] = edge;
            }
        }

        if (!previous.ContainsKey(toId)) throw new InvalidOperationException($"No right corridor from {fromId} to {toId}.");
        var edges = new List<(CorridorEdge Edge, string From)>();
        string cursor = toId;
        while (cursor != fromId) {
            var edge = previous[cursor];
            string prior = edge.A == cursor ? edge.B : edge.A;
            edges.Add((edge, prior));
            cursor = prior;
        }
        edges.Reverse();
        var points = new List<NavigationPoint>();
        foreach (var (edge, start) in edges)
            points.AddRange(Oriented(edge, start));
        return Collapse(points);
    }

    private static CorridorEdge Edge(string a, string b, (int X, int Y)[] geoPoints) =>
        new(a, b, JoinEndpoints(RightNodes[a], geoPoints, RightNodes[b]));

    private static IReadOnlyList<NavigationPoint> JoinEndpoints(
        NavigationPoint from,
        IEnumerable<(int X, int Y)> geoPoints,
        NavigationPoint to) => Collapse([from, .. geoPoints.Select(GeoToWorld), to]);

    private static NavigationPoint GeoToWorld((int X, int Y) point) =>
        new(point.X * 301.25 - 2_048_500, 2_048_500 - point.Y * 301.25);

    private static bool Matches(CorridorEdge edge, string from, string to) =>
        edge.A == from && edge.B == to || edge.A == to && edge.B == from;

    private static IReadOnlyList<NavigationPoint> Oriented(CorridorEdge edge, string from) =>
        edge.A == from ? edge.Points : edge.Points.Reverse().ToArray();

    private static IReadOnlyList<NavigationPoint> ReplaceEndpoints(
        IReadOnlyList<NavigationPoint> source,
        NavigationPoint from,
        NavigationPoint to) {
        if (source.Count <= 1) return Collapse([from, to]);
        return Collapse([from, .. source.Skip(1).SkipLast(1), to]);
    }

    private static IReadOnlyList<NavigationPoint> Collapse(IEnumerable<NavigationPoint> source) {
        var result = new List<NavigationPoint>();
        foreach (var point in source)
            if (result.Count == 0 || result[^1] != point)
                result.Add(point);
        return result;
    }

    private sealed record CorridorEdge(string A, string B, IReadOnlyList<NavigationPoint> Points);
}
