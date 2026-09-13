namespace iBarter.Routing;

public static class CargoWeightTable {
    public static int GetWeightForLevel(int level) => level switch {
        1 => 100,
        2 => 400,
        3 => 900,
        4 or 5 => 1000,
        6 or 7 => 2000,
        _ => 0,
    };
}
