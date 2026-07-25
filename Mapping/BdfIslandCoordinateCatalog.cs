namespace iBarter.Mapping;

using System.Text;
using System.Text.RegularExpressions;

public enum MapDisplayRegion {
    Main,
    RightInset,
    Hidden,
}

public sealed record MapIslandCoordinateInput(
    string IslandId,
    double NavigationX,
    double NavigationY,
    string NavigationSource,
    MapDisplayRegion DisplayRegion,
    bool PreferNavigationCalibration = false,
    double? CalibrationX = null,
    double? CalibrationY = null);

public sealed record BdfMapAnchor(
    string SourceName,
    string? IBarterIslandName,
    double Latitude,
    double Longitude,
    string SourceUrl);

public static class BdfIslandCoordinateCatalog {
    public static IReadOnlyDictionary<string, GeoCoordinate> Build(
        IReadOnlyList<MapIslandCoordinateInput> islands,
        IReadOnlyList<BdfMapAnchor> anchors,
        IReadOnlyDictionary<string, string> aliases) {
        var anchorCoordinates =
            new Dictionary<string, GeoCoordinate>(StringComparer.Ordinal);
        var direct = new Dictionary<string, GeoCoordinate>(StringComparer.Ordinal);
        foreach (var island in islands.Where(x => x.DisplayRegion != MapDisplayRegion.Hidden)) {
            var anchor = FindAnchor(island.IslandId, anchors, aliases);
            if (anchor is not null
                && double.IsFinite(anchor.Latitude)
                && double.IsFinite(anchor.Longitude)) {
                var coordinate = new GeoCoordinate(
                    anchor.Latitude, anchor.Longitude);
                anchorCoordinates[island.IslandId] = coordinate;
                if (!island.PreferNavigationCalibration)
                    direct[island.IslandId] = coordinate;
            }
        }

        var calibrationPairs = islands
            .Where(island => island.DisplayRegion != MapDisplayRegion.Hidden
                && double.IsFinite(island.CalibrationX ?? island.NavigationX)
                && double.IsFinite(island.CalibrationY ?? island.NavigationY)
                && anchorCoordinates.ContainsKey(island.IslandId))
            .Select(island => new CalibrationPair(
                island.CalibrationX ?? island.NavigationX,
                island.CalibrationY ?? island.NavigationY,
                WebMercatorProjection.ToNormalized(
                    anchorCoordinates[island.IslandId])))
            .ToArray();
        AffineMercatorTransform? affine = AffineMercatorTransform.TryFit(calibrationPairs);

        var result = new Dictionary<string, GeoCoordinate>(direct, StringComparer.Ordinal);
        if (affine is not null) {
            foreach (var island in islands.Where(x =>
                x.DisplayRegion == MapDisplayRegion.Main
                && !result.ContainsKey(x.IslandId)
                && double.IsFinite(x.NavigationX)
                && double.IsFinite(x.NavigationY))) {
                var normalized = affine.Transform(island.NavigationX, island.NavigationY);
                if (normalized.X is >= 0 and <= 1 && normalized.Y is >= 0 and <= 1)
                    result[island.IslandId] = WebMercatorProjection.FromNormalized(normalized);
            }
        }
        return result;
    }

    private static BdfMapAnchor? FindAnchor(
        string islandId,
        IReadOnlyList<BdfMapAnchor> anchors,
        IReadOnlyDictionary<string, string> aliases) {
        var explicitMatch = anchors.FirstOrDefault(anchor =>
            StringComparer.OrdinalIgnoreCase.Equals(anchor.IBarterIslandName, islandId));
        if (explicitMatch is not null) return explicitMatch;

        if (aliases.TryGetValue(islandId, out string? sourceName)) {
            var alias = anchors.FirstOrDefault(anchor =>
                StringComparer.OrdinalIgnoreCase.Equals(anchor.SourceName, sourceName));
            if (alias is not null) return alias;
        }

        string normalized = Normalize(islandId);
        return anchors
            .Where(anchor => Normalize(anchor.SourceName) == normalized)
            .OrderBy(anchor => anchor.SourceName.Contains(" - ", StringComparison.Ordinal))
            .ThenBy(anchor => SourcePriority(anchor.SourceUrl))
            .ThenBy(anchor => anchor.SourceName, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static int SourcePriority(string sourceUrl) =>
        sourceUrl.Contains("connect.js", StringComparison.OrdinalIgnoreCase)
            || sourceUrl.Contains("village.js", StringComparison.OrdinalIgnoreCase)
            || sourceUrl.Contains("city.js", StringComparison.OrdinalIgnoreCase)
            || sourceUrl.Contains("trade.js", StringComparison.OrdinalIgnoreCase)
            ? 0
            : 1;

    private static string Normalize(string value) {
        string normalized = value.Replace('_', ' ').ToLowerInvariant().Normalize(
            NormalizationForm.FormD);
        normalized = Regex.Replace(normalized, @"\s+-\s+.*$", string.Empty);
        normalized = normalized.Replace("'", string.Empty).Replace("’", string.Empty);
        normalized = Regex.Replace(
            normalized,
            @"\b(island|port|pier|village|town|city|wharf)\b",
            " ");
        return Regex.Replace(normalized, @"[^a-z0-9]+", " ").Trim();
    }

    private readonly record struct CalibrationPair(
        double X,
        double Y,
        NormalizedMercatorPoint Target);

    private sealed record AffineMercatorTransform(
        double X0,
        double Xx,
        double Xy,
        double Y0,
        double Yx,
        double Yy) {
        public NormalizedMercatorPoint Transform(double x, double y) =>
            new(X0 + Xx * x + Xy * y, Y0 + Yx * x + Yy * y);

        public static AffineMercatorTransform? TryFit(
            IReadOnlyList<CalibrationPair> pairs) {
            if (pairs.Count < 3) return null;
            double[,] normal = new double[3, 3];
            double[] targetX = new double[3];
            double[] targetY = new double[3];
            foreach (var pair in pairs) {
                double[] row = [1, pair.X, pair.Y];
                for (int r = 0; r < 3; r++) {
                    targetX[r] += row[r] * pair.Target.X;
                    targetY[r] += row[r] * pair.Target.Y;
                    for (int c = 0; c < 3; c++)
                        normal[r, c] += row[r] * row[c];
                }
            }

            double[]? coefficientsX = Solve(normal, targetX);
            double[]? coefficientsY = Solve(normal, targetY);
            return coefficientsX is null || coefficientsY is null
                ? null
                : new AffineMercatorTransform(
                    coefficientsX[0], coefficientsX[1], coefficientsX[2],
                    coefficientsY[0], coefficientsY[1], coefficientsY[2]);
        }

        private static double[]? Solve(double[,] source, double[] target) {
            double[,] matrix = new double[3, 4];
            for (int row = 0; row < 3; row++) {
                for (int column = 0; column < 3; column++)
                    matrix[row, column] = source[row, column];
                matrix[row, 3] = target[row];
            }

            for (int pivot = 0; pivot < 3; pivot++) {
                int best = pivot;
                for (int row = pivot + 1; row < 3; row++)
                    if (Math.Abs(matrix[row, pivot]) > Math.Abs(matrix[best, pivot]))
                        best = row;
                if (Math.Abs(matrix[best, pivot]) < 1e-12) return null;
                if (best != pivot)
                    for (int column = pivot; column < 4; column++)
                        (matrix[pivot, column], matrix[best, column]) =
                            (matrix[best, column], matrix[pivot, column]);

                double divisor = matrix[pivot, pivot];
                for (int column = pivot; column < 4; column++)
                    matrix[pivot, column] /= divisor;
                for (int row = 0; row < 3; row++) {
                    if (row == pivot) continue;
                    double factor = matrix[row, pivot];
                    for (int column = pivot; column < 4; column++)
                        matrix[row, column] -= factor * matrix[pivot, column];
                }
            }
            return [matrix[0, 3], matrix[1, 3], matrix[2, 3]];
        }
    }
}
