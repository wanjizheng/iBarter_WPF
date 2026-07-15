namespace iBarter.Routing;

public static class RoutePlannerRowIdentity {
    public static string Create(int index, string islandId, string item1Id, string item2Id) =>
        $"{index}:{islandId}:{item1Id}:{item2Id}";
}
