namespace iBarter.Navigation;

using System.Collections.Frozen;

public readonly record struct NavigationPoint(double X, double Y);
public readonly record struct NormalizedPoint(double X, double Y);
public readonly record struct NormalizedBounds(double Left, double Top, double Right, double Bottom);
public enum SpecialDisplayGroup { MainMap, LeftInset, RightInset, BottomEdge }

public static class IslandNavigationGeometry {
    public static readonly FrozenSet<string> LeftInsetNames = new[] {
        "Dallae", "Haemo", "Unfinished", "Pakio", "Lantinia", "Carrack", "Wandering",
        "Haran", "Crow", "Cholace", "Ancient", "Rickun", "Marine", "Cox_Pirate",
    }.ToFrozenSet(StringComparer.Ordinal);

    public static readonly FrozenSet<string> RightInsetNames = new[] {
        "Hakoven", "Derko", "Arehaza", "Kashuma", "Halmad",
    }.ToFrozenSet(StringComparer.Ordinal);

    public static readonly FrozenSet<string> BottomEdgeNames = new[] {
        "Grandiha", "Midnight",
    }.ToFrozenSet(StringComparer.Ordinal);

    public static SpecialDisplayGroup GetDisplayGroup(string islandName) {
        if (LeftInsetNames.Contains(islandName)) return SpecialDisplayGroup.LeftInset;
        if (RightInsetNames.Contains(islandName)) return SpecialDisplayGroup.RightInset;
        if (BottomEdgeNames.Contains(islandName)) return SpecialDisplayGroup.BottomEdge;
        return SpecialDisplayGroup.MainMap;
    }
    public static double Distance(NavigationPoint a, NavigationPoint b) {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static IReadOnlyDictionary<string, NormalizedPoint> ProjectToInset(
        IReadOnlyDictionary<string, NavigationPoint> points,
        NormalizedBounds bounds,
        double padding) {
        if (points.Count == 0) {
            return new Dictionary<string, NormalizedPoint>();
        }

        return points.ToDictionary(
            pair => pair.Key,
            pair => ProjectPointToInset(points.Values, bounds, padding, pair.Value));
    }

    public static NormalizedPoint ProjectPointToInset(
        IEnumerable<NavigationPoint> referencePoints,
        NormalizedBounds bounds,
        double padding,
        NavigationPoint point) {
        var points = referencePoints as NavigationPoint[] ?? referencePoints.ToArray();
        if (points.Length == 0)
            throw new ArgumentException("At least one reference point is required.", nameof(referencePoints));

        double minX = points.Min(p => p.X);
        double maxX = points.Max(p => p.X);
        double minY = points.Min(p => p.Y);
        double maxY = points.Max(p => p.Y);
        double usableWidth = bounds.Right - bounds.Left - 2 * padding;
        double usableHeight = bounds.Bottom - bounds.Top - 2 * padding;
        double spanX = Math.Max(maxX - minX, 1);
        double spanY = Math.Max(maxY - minY, 1);
        double scale = Math.Min(usableWidth / spanX, usableHeight / spanY);
        double usedWidth = spanX * scale;
        double usedHeight = spanY * scale;
        double originX = bounds.Left + (bounds.Right - bounds.Left - usedWidth) / 2;
        double originY = bounds.Top + (bounds.Bottom - bounds.Top - usedHeight) / 2;

        return new NormalizedPoint(
            originX + (point.X - minX) * scale,
            originY + (maxY - point.Y) * scale);
    }
}
