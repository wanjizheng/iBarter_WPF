namespace iBarter.Mapping;

using System.Text;
using System.Text.RegularExpressions;

public enum MapDisplayRegion {
    Main,
    RightInset,
    Hidden,
}

public enum IslandResolutionMode {
    Missing,
    DirectBdfMatch,
    DirectBdfAliasMatch,
    DirectBdfNormalizedMatch,
    TrustedAffineFallback,
}

public sealed record IslandCoordinateResult(
    string IslandId,
    string RouteDestinationX,
    string RouteDestinationY,
    string RouteDestinationSource,
    string MapAnchorX,
    string MapAnchorY,
    string MapAnchorSource,
    string MatchedBdfSourceName,
    string MatchedIBarterIslandName,
    IslandResolutionMode Resolution,
    GeoCoordinate? Coordinate,
    NormalizedMercatorPoint? Normalized,
    double AffineResidual);

public sealed record MapIslandCoordinateInput(
    string IslandId,
    double NavigationX,
    double NavigationY,
    string NavigationSource,
    MapDisplayRegion DisplayRegion,
    bool PreferNavigationCalibration = false,
    double? CalibrationX = null,
    double? CalibrationY = null,
    string MapAnchorSource = "");

public sealed record BdfMapAnchor(
    string SourceName,
    string? IBarterIslandName,
    double Latitude,
    double Longitude,
    string SourceUrl);

public sealed record BdfIslandCoordinateCatalogResult(
    IReadOnlyDictionary<string, GeoCoordinate> Coordinates,
    IReadOnlyDictionary<string, IslandCoordinateResult> Diagnostics);

public readonly record struct CalibrationPair(
    double X,
    double Y,
    NormalizedMercatorPoint Target);

public static class BdfIslandCoordinateCatalog {
    private static readonly string[] TrustedSourcePrefixes = {
        "bdo-world-direct",
        "bdo-world-wharf",
        "bdo-world",
    };

    private sealed record AnchorMatch(
        BdfMapAnchor Anchor,
        IslandResolutionMode Mode);

    public static IReadOnlyDictionary<string, GeoCoordinate> Build(
        IReadOnlyList<MapIslandCoordinateInput> islands,
        IReadOnlyList<BdfMapAnchor> anchors,
        IReadOnlyDictionary<string, string> aliases) =>
        BuildDetailed(islands, anchors, aliases).Coordinates;

    public static BdfIslandCoordinateCatalogResult BuildDetailed(
        IReadOnlyList<MapIslandCoordinateInput> islands,
        IReadOnlyList<BdfMapAnchor> anchors,
        IReadOnlyDictionary<string, string> aliases) {
        var diagnostics = new Dictionary<string, IslandCoordinateResult>(
            StringComparer.Ordinal);
        var direct = new Dictionary<string, GeoCoordinate>(StringComparer.Ordinal);

        foreach (var island in islands.Where(x => x.DisplayRegion != MapDisplayRegion.Hidden)) {
            AnchorMatch? match = FindAnchor(island.IslandId, anchors, aliases);
            BdfMapAnchor? anchor = match?.Anchor;
            if (anchor is null
                || !double.IsFinite(anchor.Latitude)
                || !double.IsFinite(anchor.Longitude)) {
                diagnostics[island.IslandId] = ResultFor(
                    island, anchor, IslandResolutionMode.Missing,
                    coordinate: null, normalized: null, residual: double.NaN);
                continue;
            }

            var coordinate = new GeoCoordinate(anchor.Latitude, anchor.Longitude);
            direct[island.IslandId] = coordinate;
            diagnostics[island.IslandId] = ResultFor(
                island, anchor, match!.Mode,
                coordinate,
                WebMercatorProjection.ToNormalized(coordinate),
                residual: 0.0);
        }

        IReadOnlyList<CalibrationPair> calibrationPairs =
            BuildTrustedCalibrationPairs(islands, direct);
        AffineMercatorTransform? affine =
            AffineMercatorTransform.TryFit(calibrationPairs);
        double affineResidual = affine?.ComputeSelfResidual(calibrationPairs)
            ?? double.NaN;

        var result = new Dictionary<string, GeoCoordinate>(direct, StringComparer.Ordinal);
        if (affine is not null) {
            foreach (var island in islands.Where(x =>
                x.DisplayRegion == MapDisplayRegion.Main
                && !result.ContainsKey(x.IslandId))) {
                if (!TryGetMapWorldPoint(island, out double mapX, out double mapY)) {
                    continue;
                }

                NormalizedMercatorPoint normalized = affine.Transform(mapX, mapY);
                if (normalized.X is >= 0 and <= 1
                    && normalized.Y is >= 0 and <= 1) {
                    GeoCoordinate coordinate =
                        WebMercatorProjection.FromNormalized(normalized);
                    result[island.IslandId] = coordinate;
                    diagnostics[island.IslandId] = ResultFor(
                        island, anchor: null,
                        IslandResolutionMode.TrustedAffineFallback,
                        coordinate, normalized, affineResidual);
                }
                else {
                    diagnostics[island.IslandId] = ResultFor(
                        island, anchor: null,
                        IslandResolutionMode.TrustedAffineFallback,
                        coordinate: null, normalized: null, residual: affineResidual);
                }
            }
        }

        return new BdfIslandCoordinateCatalogResult(result, diagnostics);
    }

    private static IReadOnlyList<CalibrationPair> BuildTrustedCalibrationPairs(
        IReadOnlyList<MapIslandCoordinateInput> islands,
        IReadOnlyDictionary<string, GeoCoordinate> directCoordinates) {
        var pairs = new List<CalibrationPair>();
        foreach (MapIslandCoordinateInput island in islands) {
            if (!directCoordinates.TryGetValue(island.IslandId, out GeoCoordinate coordinate)
                || !IsTrustedSource(island.MapAnchorSource)
                || !TryGetMapWorldPoint(island, out double mapX, out double mapY)) {
                continue;
            }

            pairs.Add(new CalibrationPair(
                mapX,
                mapY,
                WebMercatorProjection.ToNormalized(coordinate)));
        }
        return pairs;
    }

    private static bool TryGetMapWorldPoint(
        MapIslandCoordinateInput island,
        out double x,
        out double y) {
        x = island.CalibrationX ?? island.NavigationX;
        y = island.CalibrationY ?? island.NavigationY;
        return double.IsFinite(x) && double.IsFinite(y);
    }

    private static bool IsTrustedSource(string source) {
        if (string.IsNullOrWhiteSpace(source)) return false;
        return TrustedSourcePrefixes.Any(prefix =>
            source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static IslandCoordinateResult ResultFor(
        MapIslandCoordinateInput island,
        BdfMapAnchor? anchor,
        IslandResolutionMode mode,
        GeoCoordinate? coordinate,
        NormalizedMercatorPoint? normalized,
        double residual) =>
        new(
            IslandId: island.IslandId,
            RouteDestinationX: FormatCoord(island.NavigationX),
            RouteDestinationY: FormatCoord(island.NavigationY),
            RouteDestinationSource: island.NavigationSource ?? string.Empty,
            MapAnchorX: FormatCoord(island.CalibrationX),
            MapAnchorY: FormatCoord(island.CalibrationY),
            MapAnchorSource: island.MapAnchorSource ?? string.Empty,
            MatchedBdfSourceName: anchor?.SourceName ?? string.Empty,
            MatchedIBarterIslandName: anchor?.IBarterIslandName ?? string.Empty,
            Resolution: mode,
            Coordinate: coordinate,
            Normalized: normalized,
            AffineResidual: residual);

    private static string FormatCoord(double? value) =>
        value.HasValue && double.IsFinite(value.Value)
            ? value.Value.ToString(
                "0.###", System.Globalization.CultureInfo.InvariantCulture)
            : "-";

    private static AnchorMatch? FindAnchor(
        string islandId,
        IReadOnlyList<BdfMapAnchor> anchors,
        IReadOnlyDictionary<string, string> aliases) {
        BdfMapAnchor? explicitMatch = anchors.FirstOrDefault(anchor =>
            StringComparer.OrdinalIgnoreCase.Equals(
                anchor.IBarterIslandName, islandId));
        if (explicitMatch is not null) {
            return new AnchorMatch(
                explicitMatch, IslandResolutionMode.DirectBdfMatch);
        }

        if (aliases.TryGetValue(islandId, out string? sourceName)) {
            BdfMapAnchor? alias = anchors.FirstOrDefault(anchor =>
                StringComparer.OrdinalIgnoreCase.Equals(
                    anchor.SourceName, sourceName));
            if (alias is not null) {
                return new AnchorMatch(
                    alias, IslandResolutionMode.DirectBdfAliasMatch);
            }
        }

        string normalized = Normalize(islandId);
        BdfMapAnchor[] normalizedCandidates = anchors
            .Where(anchor => Normalize(anchor.SourceName) == normalized)
            .ToArray();
        return normalizedCandidates.Length == 1
            ? new AnchorMatch(
                normalizedCandidates[0],
                IslandResolutionMode.DirectBdfNormalizedMatch)
            : null;
    }

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

    private sealed record AffineMercatorTransform(
        double X0,
        double Xx,
        double Xy,
        double Y0,
        double Yx,
        double Yy) {
        public NormalizedMercatorPoint Transform(double x, double y) =>
            new(X0 + Xx * x + Xy * y, Y0 + Yx * x + Yy * y);

        public double ComputeSelfResidual(IReadOnlyList<CalibrationPair> pairs) {
            if (pairs.Count == 0) return double.NaN;
            double sumSq = 0.0;
            foreach (CalibrationPair pair in pairs) {
                NormalizedMercatorPoint projected = Transform(pair.X, pair.Y);
                double dx = projected.X - pair.Target.X;
                double dy = projected.Y - pair.Target.Y;
                sumSq += dx * dx + dy * dy;
            }
            return Math.Sqrt(sumSq / pairs.Count);
        }

        public static AffineMercatorTransform? TryFit(
            IReadOnlyList<CalibrationPair> pairs) {
            if (pairs.Count < 3) return null;
            double[,] normal = new double[3, 3];
            double[] targetX = new double[3];
            double[] targetY = new double[3];
            foreach (CalibrationPair pair in pairs) {
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
