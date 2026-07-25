namespace iBarter.Mapping;

using System.Text;
using System.Text.RegularExpressions;

public enum MapDisplayRegion {
    Main,
    RightInset,
    Hidden,
}

/// <summary>
/// Mode used to place an island on the HD map. Recorded per-island
/// in <see cref="IslandCoordinateResult.Resolution"/> and emitted
/// by <see cref="BdfIslandCoordinateCatalog.Build"/> so a stale
/// affine fallback can never silently displace a real port.
/// </summary>
public enum IslandResolutionMode {
    /// <summary>Anchor lookup failed; no coordinate emitted.</summary>
    Missing,
    /// <summary>The BDF anchor matched by exact
    /// <c>IBarterIslandName</c>; no alias or normalized-name
    /// fallback needed.</summary>
    DirectBdfMatch,
    /// <summary>The BDF anchor matched via the
    /// <see cref="BdfMapMetadata.Aliases"/> table; the alias was
    /// required because no exact match existed.</summary>
    DirectBdfAliasMatch,
    /// <summary>The BDF anchor matched by normalized-name
    /// uniqueness scoring (the last-resort direct match used when
    /// no exact or alias hit was available).</summary>
    DirectBdfNormalizedMatch,
    /// <summary>No direct anchor was found and the trusted
    /// affine calibration fit placed the island. The fit's
    /// self-residual is reported in
    /// <see cref="IslandCoordinateResult.AffineResidual"/>.</summary>
    TrustedAffineFallback,
}

/// <summary>
/// Per-island coordinate resolution outcome emitted by
/// <see cref="BdfIslandCoordinateCatalog.Build"/> so the caller
/// can run diagnostics, sanity checks, and regression tests
/// without re-deriving the path from a single coordinate.
/// </summary>
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

/// <summary>
/// Trusted affine calibration pair: a (worldX, worldY) → normalized
/// map coordinate pair. Top-level (not nested) so
/// <see cref="BdfIslandCoordinateCatalog"/> can build a
/// <see cref="IReadOnlyList{T}"/> of these without
/// `AffineMercatorTransform.CalibrationPair` qualification noise
/// in the public surface.
/// </summary>
public readonly record struct CalibrationPair(
    double X,
    double Y,
    NormalizedMercatorPoint Target);

public static class BdfIslandCoordinateCatalog {

    /// <summary>
    /// Source prefixes whose anchors are eligible for the trusted
    /// affine calibration pool. Anything else (e.g.
    /// <c>bdocodex-calibrated</c>, <c>main-map-calibrated</c>,
    /// <c>bdocodex-barterer-*</c>, or route-only NPC destinations)
    /// is excluded so we never blend two coordinate semantics into
    /// one least-squares fit.
    /// </summary>
    private static readonly string[] TrustedSourcePrefixes = {
        "bdo-world-direct",
        "bdo-world-wharf",
        "bdo-world",
    };

    /// <summary>
    /// Anchor <see cref="BdfMapAnchor.SourceUrl"/> substrings that
    /// mark a source as using the same world-coordinate system as
    /// bdo-world and therefore safe for the affine pool. Only
    /// <c>connect.js</c> and <c>village.js</c> qualify today; a
    /// new endpoint must be explicitly added here before the affine
    /// pool starts using it.
    /// </summary>
    private static readonly string[] TrustedSourceUrlSubstrings = {
        "/connect.js",
        "/village.js",
    };

    public static IReadOnlyDictionary<string, GeoCoordinate> Build(
        IReadOnlyList<MapIslandCoordinateInput> islands,
        IReadOnlyList<BdfMapAnchor> anchors,
        IReadOnlyDictionary<string, string> aliases) {
        return BuildDetailed(islands, anchors, aliases).Coordinates;
    }

    public static BdfIslandCoordinateCatalogResult BuildDetailed(
        IReadOnlyList<MapIslandCoordinateInput> islands,
        IReadOnlyList<BdfMapAnchor> anchors,
        IReadOnlyDictionary<string, string> aliases) {
        var diagnostics = new Dictionary<string, IslandCoordinateResult>(
            StringComparer.Ordinal);
        var direct = new Dictionary<string, GeoCoordinate>(
            StringComparer.Ordinal);

        foreach (var island in islands.Where(x => x.DisplayRegion != MapDisplayRegion.Hidden)) {
            var anchor = FindAnchor(island.IslandId, anchors, aliases);
            if (anchor is null || !double.IsFinite(anchor.Latitude)
                || !double.IsFinite(anchor.Longitude)) {
                diagnostics[island.IslandId] = ResultFor(
                    island, anchor, IslandResolutionMode.Missing,
                    coordinate: null, normalized: null, residual: double.NaN);
                continue;
            }

            var coordinate = new GeoCoordinate(anchor.Latitude, anchor.Longitude);

            // Direct BDF anchor ALWAYS wins when found. The legacy
            // display-group (LeftInset / RightInset) is a static-
            // fallback concern; it must never block direct placement
            // of a real port like Dallae Pier.
            direct[island.IslandId] = coordinate;

            var mode = ResolutionForMatch(island, anchor);
            diagnostics[island.IslandId] = ResultFor(
                island, anchor, mode,
                coordinate: coordinate,
                normalized: WebMercatorProjection.ToNormalized(coordinate),
                residual: 0.0);
        }

        var calibrationPairs = BuildTrustedCalibrationPairs(islands, direct);
        var affine = AffineMercatorTransform.TryFit(calibrationPairs);

        var result = new Dictionary<string, GeoCoordinate>(direct, StringComparer.Ordinal);
        if (affine is not null) {
            foreach (var island in islands.Where(x =>
                x.DisplayRegion == MapDisplayRegion.Main
                && !result.ContainsKey(x.IslandId)
                && double.IsFinite(x.NavigationX)
                && double.IsFinite(x.NavigationY))) {
                var normalized = affine.Transform(island.NavigationX, island.NavigationY);
                if (normalized.X is >= 0 and <= 1 && normalized.Y is >= 0 and <= 1) {
                    var coordinate = WebMercatorProjection.FromNormalized(normalized);
                    result[island.IslandId] = coordinate;

                    // The fit's self-residual surfaces any
                    // catastrophic mis-fit that would silently
                    // displace a port like Dallae Pier.
                    double residual = affine.ComputeSelfResidual(calibrationPairs);
                    diagnostics[island.IslandId] = ResultFor(
                        island,
                        anchor: null,
                        IslandResolutionMode.TrustedAffineFallback,
                        coordinate: coordinate,
                        normalized: normalized,
                        residual: residual);
                }
                else {
                    diagnostics[island.IslandId] = ResultFor(
                        island, anchor: null,
                        IslandResolutionMode.TrustedAffineFallback,
                        coordinate: null, normalized: null, residual: double.NaN);
                }
            }
        }

        return new BdfIslandCoordinateCatalogResult(result, diagnostics);
    }

    /// <summary>
    /// Build the affine calibration pool using ONLY trusted sources
    /// (BDF world-direct / world-wharf). Barterer destinations,
    /// main-map-calibrated entries, and bdocodex-calibrated entries
    /// are excluded — they represent different coordinate semantics
    /// and must not participate in the same fit.
    /// </summary>
    private static IReadOnlyList<CalibrationPair> BuildTrustedCalibrationPairs(
        IReadOnlyList<MapIslandCoordinateInput> islands,
        IReadOnlyDictionary<string, GeoCoordinate> directCoordinates) {
        var pairs = new List<CalibrationPair>();
        foreach (var island in islands) {
            if (!directCoordinates.TryGetValue(island.IslandId, out var coordinate)) {
                continue;
            }
            if (!IsTrustedSource(island.MapAnchorSource)) {
                continue;
            }
            if (!double.IsFinite(island.NavigationX)
                || !double.IsFinite(island.NavigationY)) {
                continue;
            }
            pairs.Add(new CalibrationPair(
                island.NavigationX,
                island.NavigationY,
                WebMercatorProjection.ToNormalized(coordinate)));
        }
        return pairs;
    }

    /// <summary>
    /// True if the source tag is one we trust to feed the affine
    /// pool. Conservative by design — a new source must be
    /// explicitly added to <see cref="TrustedSourcePrefixes"/>
    /// or <see cref="TrustedSourceUrlSubstrings"/> before it can
    /// influence the fit.
    /// </summary>
    private static bool IsTrustedSource(string source) {
        if (string.IsNullOrEmpty(source)) return false;
        foreach (var prefix in TrustedSourcePrefixes) {
            if (source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        foreach (var sub in TrustedSourceUrlSubstrings) {
            if (source.Contains(sub, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        return false;
    }

    private static IslandResolutionMode ResolutionForMatch(
        MapIslandCoordinateInput island, BdfMapAnchor anchor) {
        if (!string.IsNullOrEmpty(anchor.IBarterIslandName)
            && StringComparer.Ordinal.Equals(anchor.IBarterIslandName, island.IslandId)) {
            return IslandResolutionMode.DirectBdfMatch;
        }
        return IslandResolutionMode.DirectBdfAliasMatch;
    }

    private static IslandCoordinateResult ResultFor(
        MapIslandCoordinateInput island,
        BdfMapAnchor? anchor,
        IslandResolutionMode mode,
        GeoCoordinate? coordinate,
        NormalizedMercatorPoint? normalized,
        double residual) {
        string anchorName = anchor?.SourceName ?? string.Empty;
        string anchorIBarter = anchor?.IBarterIslandName ?? string.Empty;
        return new IslandCoordinateResult(
            IslandId: island.IslandId,
            RouteDestinationX: FormatCoord(island.NavigationX),
            RouteDestinationY: FormatCoord(island.NavigationY),
            RouteDestinationSource: island.NavigationSource ?? string.Empty,
            MapAnchorX: FormatCoord(island.CalibrationX),
            MapAnchorY: FormatCoord(island.CalibrationY),
            MapAnchorSource: island.MapAnchorSource ?? string.Empty,
            MatchedBdfSourceName: anchorName,
            MatchedIBarterIslandName: anchorIBarter,
            Resolution: mode,
            Coordinate: coordinate,
            Normalized: normalized,
            AffineResidual: residual);
    }

    private static string FormatCoord(double? value) =>
        value.HasValue && double.IsFinite(value.Value)
            ? value.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
            : "-";

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
            || sourceUrl.Contains("trade.js", StringComparison.OrdinalIgnoreCase)
            ? 0
            : 1;

    private static string Normalize(string value) {
        string normalized = value.Replace('_', ' ').ToLowerInvariant().Normalize(
            NormalizationForm.FormD);
        normalized = Regex.Replace(normalized, @"\s+-\s+.*$", string.Empty);
        normalized = normalized.Replace("'", string.Empty).Replace("'", string.Empty);
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

        /// <summary>
        /// Self-residual: RMS pixel error of the transform against
        /// the calibration pairs it was fit on. Surfaced as
        /// <see cref="IslandCoordinateResult.AffineResidual"/> so
        /// diagnostics can flag a mis-fit that would silently
        /// displace a real port.
        /// </summary>
        public double ComputeSelfResidual(IReadOnlyList<CalibrationPair> pairs) {
            if (pairs.Count == 0) return double.NaN;
            double sumSq = 0.0;
            foreach (var pair in pairs) {
                var projected = Transform(pair.X, pair.Y);
                double dx = projected.X - pair.Target.X;
                double dy = projected.Y - pair.Target.Y;
                sumSq += dx * dx + dy * dy;
            }
            return Math.Sqrt(sumSq / pairs.Count);
        }

        public static AffineMercatorTransform? TryFit(IReadOnlyList<CalibrationPair> pairs) {
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