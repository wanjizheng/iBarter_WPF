namespace iBarter.Planning;

public enum AutoPlanningStrategy { CrowCoinFirst, ProfitFirst, RestockFirst }

public sealed record AutoPlanningRoute(
    string RowId, int Group,
    string Item1Id, int Item1Level, int Item1Number,
    string Item2Id, int Item2Level, int Item2Number,
    bool ProducesCrowCoin, int Parley, int Remaining);

public sealed record AutoPlanningRequest(
    IReadOnlyList<AutoPlanningRoute> Routes,
    IReadOnlyDictionary<string, int> CurrentInventory,
    AutoPlanningStrategy Strategy,
    int Lv5Target, int Lv6Target,
    int ParleyBudget = 1_000_000);

public sealed record AutoPlanningDiagnostic(string Code, string RowId = "");

public sealed record AutoPlanningResult(
    bool Success,
    IReadOnlyDictionary<string, int> Multipliers,
    IReadOnlyDictionary<string, int> ProjectedInventory,
    int UsedParley,
    IReadOnlyList<AutoPlanningDiagnostic> Diagnostics);