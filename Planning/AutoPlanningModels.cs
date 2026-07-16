namespace iBarter.Planning;

public enum AutoPlanningStrategy {
    CrowCoinFirst,
    ProfitFirst,
    RestockFirst,
    ManualSelection,
}

public sealed record AutoPlanningRoute(
    string RowId, int Group,
    string Item1Id, int Item1Level, int Item1Number,
    string Item2Id, int Item2Level, int Item2Number,
    bool ProducesCrowCoin, int Parley, int Remaining) {

    // Locale-independent ItemID of Crow Coin. Matches Resources/Items.csv:10
    // ("Crow Coin" in en-US, "烏鴉硬幣" in zh-TW). The service uses this to
    // decide which routes participate in the Crow Coin phase, so the UI does
    // not need to set the ProducesCrowCoin flag.
    public const string CrowCoinItemId = "10";
}

public sealed record AutoPlanningRequest(
    IReadOnlyList<AutoPlanningRoute> Routes,
    IReadOnlyDictionary<string, int> CurrentInventory,
    AutoPlanningStrategy Strategy,
    int Lv5Target, int Lv6Target,
    int ParleyBudget = 1_000_000,
    IReadOnlyDictionary<string, int>? CarryOverInventory = null);

public sealed record AutoPlanningDiagnostic(string Code, string RowId = "");

public sealed record AutoPlanningResult(
    bool Success,
    IReadOnlyDictionary<string, int> Multipliers,
    IReadOnlyDictionary<string, int> ProjectedInventory,
    int UsedParley,
    IReadOnlyList<AutoPlanningDiagnostic> Diagnostics);
