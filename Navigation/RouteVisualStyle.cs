namespace iBarter.Navigation;

public readonly record struct RouteLineVisualStyle(
    double StrokeThickness,
    double Opacity,
    bool ShowSelectionOverlay);

public static class RouteVisualStyle {
    public static RouteLineVisualStyle For(bool routeSelected, bool focused, bool pulse) {
        if (focused)
            return new RouteLineVisualStyle(3.2, pulse ? 0.42 : 1, false);
        return routeSelected
            ? new RouteLineVisualStyle(2.4, 1, true)
            : new RouteLineVisualStyle(1.25, 0.20, false);
    }
}
