namespace iBarter.Navigation;

public readonly record struct RouteMarkerOffset(double X, double Y) {
    public double DistanceFromOrigin => Math.Sqrt(X * X + Y * Y);
}

public static class RouteStepMarkerLayout {
    private const double RepeatedMarkerRadius = 13;

    public static RouteMarkerOffset OffsetFor(int occurrenceIndex, int occurrenceCount) {
        if (occurrenceCount <= 1) return new RouteMarkerOffset(0, 0);
        double angle = -Math.PI / 2 + 2 * Math.PI * occurrenceIndex / occurrenceCount;
        return new RouteMarkerOffset(
            RepeatedMarkerRadius * Math.Cos(angle),
            RepeatedMarkerRadius * Math.Sin(angle));
    }
}
