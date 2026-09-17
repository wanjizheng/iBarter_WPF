using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace iBarter.Routing;

public sealed record TaggedCarrier {
    public string Id { get; init; } = "main";
    public string Name { get; set; } = "Main";
    public double LimitLT { get; set; } = 1500;
    public double OccupiedLT { get; set; }
    public int Slots { get; set; } = 30;
}

public sealed record TaggedPort {
    public string IslandId { get; set; } = "";
    public string Npc { get; set; } = "";
    public bool Enabled { get; set; }
    public string WarehouseId { get; set; } = "";
    public string Evidence { get; set; } = "";
}

public sealed record TaggedCargoEntry(string Container, string ItemId, int Quantity);

public sealed record TaggedTransportSettings {
    public bool Enabled { get; set; }
    public string StartIsland { get; set; } = "Velia";
    public string ActiveCharacter { get; set; } = "main";
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? HomeWarehouseId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool StartFromSelectedLocation { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public RouteOptimizationProfile? SearchProfile { get; set; }
    public TaggedCarrier[] Carriers { get; set; } = [
        new() { Id = "main", Name = "Main" }, new() { Id = "alt", Name = "TAG" },
        new() { Id = "main-elephant", Name = "Main elephant", LimitLT = 1200, Slots = 16 },
        new() { Id = "alt-elephant", Name = "TAG elephant", LimitLT = 1200, Slots = 16 }];
    public TaggedPort[] Ports { get; set; } = [
        new() { IslandId = "Velia", Npc = "Croix", Enabled = true, WarehouseId = "Velia", Evidence = "Warehouse base" },
        new() { IslandId = "Iliya", Npc = "Dario", Enabled = true, WarehouseId = "Iliya", Evidence = "Warehouse base" },
        new() { IslandId = "Epheria", Npc = "Bartholomeo", Enabled = true, WarehouseId = "Epheria", Evidence = "Warehouse base" },
        new() { IslandId = "Kuit", Npc = "Gafur", Enabled = true, Evidence = "Player transfer reports; https://www.reddit.com/r/blackdesertonline/comments/kypdnx/" },
        new() { IslandId = "Lema", Npc = "Bolhi", Enabled = true, Evidence = "Wharf menu; no direct warehouse assumed" },
        new() { IslandId = "Arehaza", Npc = "Luicy", Evidence = "NPC found; cargo menu unverified" },
        new() { IslandId = "Grandiha", Npc = "Derensha", Evidence = "NPC found; cargo menu unverified" },
        new() { IslandId = "Midnight", Npc = "Neltia", Evidence = "NPC found; cargo menu unverified" },
        new() { IslandId = "Dallae", Npc = "Sungoo", Evidence = "NPC found; cargo menu unverified" },
        new() { IslandId = "Haemo", Npc = "Gurong", Evidence = "NPC found; cargo menu unverified" },
        new() { IslandId = "Sausan", Evidence = "Cargo-menu exception reported; no Ancado storage link" }];
    public TaggedCargoEntry[] InitialCargo { get; set; } = [];
    // Coordinates in the existing navigation catalog are game world centimeters.
    public double NormalMetersPerSecond { get; set; } = 12;
    public double OverloadedMetersPerSecond { get; set; } = 3;
    public double DockSeconds { get; set; } = 15;
    public double SwitchSeconds { get; set; } = 15;
    public double ElephantSummonSeconds { get; set; } = 10;
    public double TransferSeconds { get; set; } = 5;
    public double BarterSeconds { get; set; } = 3;
    public bool AllowOverloadedSailing { get; set; } = true;
    public double BarterOutputRatio { get; set; } = 1.7;
    public double CharacterReceiveRatio { get; set; } = 1.7;
    public int ShipSlots { get; set; } = 20;
    public int SearchSeconds { get; set; } = 15;
    public int MaxStates { get; set; } = 50000;
}

public sealed record TaggedTrade(string RowId, string IslandId, string InputId, int InputPerExchange,
    string OutputId, int OutputPerExchange, int Exchanges);

public sealed record TaggedTransportRequest {
    public TaggedTransportSettings Settings { get; init; } = new();
    public Dictionary<string, RouteItem> Items { get; init; } = new();
    public Dictionary<string, RoutePoint> Points { get; init; } = new();
    public Dictionary<string, Dictionary<string, int>> Warehouses { get; init; } = new();
    public TaggedTrade[] Trades { get; init; } = [];
    public double ShipLimitLT { get; init; }
    public double ShipOccupiedLT { get; init; }
    public string Fingerprint() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(this))));

    public static TaggedTransportRequest Build(PlannerRouteSnapshot[] rows, StorageItemSnapshot[] storage,
        IslandRouteSnapshot[] islands, double limit, double occupied, TaggedTransportSettings settings) {
        var metadata = storage.ToDictionary(x => x.ItemId,
            x => new RouteItem(x.ItemId, x.ItemId, x.Level, CargoWeightTable.GetWeightForLevel(x.Level)));
        foreach (var row in rows) {
            metadata[row.Item1Id] = new(row.Item1Id, row.Item1DisplayName, row.Item1Level, CargoWeightTable.GetWeightForLevel(row.Item1Level));
            metadata[row.Item2Id] = new(row.Item2Id, row.Item2DisplayName, row.Item2Level, CargoWeightTable.GetWeightForLevel(row.Item2Level));
        }
        var warehouses = new Dictionary<string, Dictionary<string, int>> {
            ["Velia"] = storage.ToDictionary(x => x.ItemId, x => x.Velia),
            ["Iliya"] = storage.ToDictionary(x => x.ItemId, x => x.Iliya),
            ["Epheria"] = storage.ToDictionary(x => x.ItemId, x => x.Epheria),
            ["Ancado"] = storage.ToDictionary(x => x.ItemId, x => x.Ancado),
        };
        // Match the existing planner's user-supplied land-material contract.
        foreach (var group in rows.Where(x => !x.ExchangeDone && x.Item1Level == 0 && x.Item2Level == 1)
                     .GroupBy(x => x.Item1Id))
            foreach (var warehouse in warehouses.Values)
                warehouse[group.Key] = checked(group.Sum(x => x.ExchangeQuantity * x.Item1Number));
        return new() {
            Settings = JsonSerializer.Deserialize<TaggedTransportSettings>(JsonSerializer.Serialize(settings))!,
            Items = metadata,
            Points = islands.GroupBy(x => x.IslandId).ToDictionary(x => x.Key, x => x.First().Point),
            Warehouses = warehouses, ShipLimitLT = limit, ShipOccupiedLT = occupied,
            Trades = rows.Where(x => !x.ExchangeDone && x.ExchangeQuantity > 0).Select(x => new TaggedTrade(
                x.RowId, x.IslandId, x.Item1Id, x.Item1Number, x.Item2Id, x.Item2Number, x.ExchangeQuantity)).ToArray()
        };
    }
}

public enum TaggedActionKind { Sail, Switch, Transfer, Barter, StackAtWarehouse, SummonElephant, Sell }
public sealed record TaggedAction(TaggedActionKind Kind, string Location, string From = "", string To = "",
    string ItemId = "", int Quantity = 0, int TradeIndex = -1);
public sealed record TaggedTransportStep(TaggedAction Action, double Seconds, double TotalSeconds,
    double ShipLT, double MainLT, double AltLT);
public sealed record TaggedTransportPlan(string Fingerprint, TaggedTransportStep[] Steps, double TotalSeconds,
    double SailingSeconds, double HandlingSeconds, double Distance, string Status, double? ShipOnlySeconds = null, int? ShipOnlyRouteCount = null, double? ShipOnlyDistance = null);
public sealed record TaggedSearchReport(double ElapsedSeconds, int Candidates, ExtremeSearchTerminationReason Termination, bool? OrdinaryReferenceAccepted = null);
public sealed record TaggedTransportResult(TaggedTransportPlan? Plan, string Message, TaggedTransportRequest? PlannedRequest = null, TaggedSearchReport? Search = null);
public sealed record TaggedSearchProgress(double ElapsedSeconds, double BudgetSeconds, int ExpandedStates, double? BestDistance, int? BestRoutes = null);

public sealed class TaggedTransportState {
    public string Location { get; internal set; } = "";
    public string Active { get; internal set; } = "main";
    public HashSet<string> SummonedElephants { get; internal set; } = new(StringComparer.Ordinal);
    public Dictionary<string, Dictionary<string, int>> Cargo { get; internal set; } = new();
    public int[] Remaining { get; internal set; } = [];
    public double Seconds { get; internal set; }
    public double SailingSeconds { get; internal set; }
    public double Distance { get; internal set; }
    public double OverloadedDistance { get; internal set; }
    public Dictionary<string, int> SoldItems { get; internal set; } = new();
    public List<TaggedTransportStep> Steps { get; internal set; } = [];
    // A transition owns only the inventories it changes. Other inventories remain
    // read-only and shared with its parent, avoiding full warehouse copies per edge.
    internal TaggedTransportState Copy(params string[] changedContainers) => new() {
        Location = Location, Active = Active, Remaining = (int[])Remaining.Clone(),
        SummonedElephants = new(SummonedElephants, StringComparer.Ordinal),
        SoldItems = new(SoldItems, StringComparer.Ordinal),
        Cargo = Cargo.ToDictionary(x => x.Key, x => changedContainers.Contains(x.Key) ? new Dictionary<string, int>(x.Value) : x.Value),
        Seconds = Seconds, SailingSeconds = SailingSeconds, Distance = Distance, OverloadedDistance = OverloadedDistance, Steps = new(Steps)
    };
}
