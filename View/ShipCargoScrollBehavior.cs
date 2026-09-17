namespace iBarter.View;

public static class ShipCargoScrollBehavior {
    public static int GetLogicalItemDelta(int wheelDelta) => wheelDelta switch {
        < 0 => 1,
        > 0 => -1,
        _ => 0,
    };
}
