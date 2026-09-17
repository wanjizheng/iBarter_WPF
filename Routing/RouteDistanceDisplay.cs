namespace iBarter.Routing;

/// <summary>Ordinary objectives use world centimetres; TAG objectives use metres.</summary>
public static class RouteDistanceDisplay {
    public static double OrdinaryKilometres(double worldDistance) => worldDistance / 100_000.0;
    public static double TaggedKilometres(double metres) => metres / 1_000.0;
    public static string Tagged(double metres) => $"{TaggedKilometres(metres):N2} km";
    public static string Ordinary(double worldDistance) => $"{OrdinaryKilometres(worldDistance):N2} km";
}
