namespace iBarter.Navigation;

/// <summary>
/// Builds the simple geometry used only to visualise the order of route stops.
/// Sailing-cost geometry remains the responsibility of <see cref="ShippingCorridorGraph"/>.
/// </summary>
public static class RouteDisplayGeometry {
    public static IReadOnlyList<T> BuildDirectLeg<T>(T from, T to) =>
        EqualityComparer<T>.Default.Equals(from, to) ? [from] : [from, to];
}
