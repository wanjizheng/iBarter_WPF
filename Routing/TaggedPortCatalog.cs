namespace iBarter.Routing;

/// <summary>Personal wharf NPCs. Coordinates use the existing Codex-to-world calibration.</summary>
public static class TaggedPortCatalog {
    public sealed record Entry(string Island, string ChineseIsland, string Npc, string ChineseNpc, int NpcId, double X, double Y, string Warehouse = "", bool Enabled = false, bool TransferVerified = false, int SpawnId = 1);
    public static readonly Entry[] Additional = [
        new("Nampo", "南浦原水村", "Yooan", "尤安", 47209, -1297330, 1127450),
        new("Byukgye", "碧溪島", "Darirong", "橋龍", 47332, -1213770, 1053360),
        new("Cheongsa", "青紗島", "Gangman", "江萬", 47772, -876607.5, 1332851.875),
        new("Ancado", "安卡杜內港", "Samia", "薩米亞", 45301, 978133, 343900, "Ancado"),
        new("Oquilla", "奧基魯阿之眼", "Ravikel", "拉比凱爾", 49579, -105511, 628806),
        new("Outpost", "前線補給港", "Flanche", "佛嵐赤", 49558, -453240, 34677.2),
        new("ShakatuPier", "夏卡魯廢棄渡口", "Purio", "普利歐", 45142, 504984, 266674, Enabled: true, TransferVerified: true),
        new("RunnPier", "廢棄渡口（倫城西側）", "Torio", "特利歐", 45141, 427418, 178947, Enabled: true, TransferVerified: true),
        new("Altinova", "爾提諾巴", "Tunger", "屯葛", 44025, 372611, -26598.8),
        new("SanctuaryCoastalOutpost", "聖所海岸哨所", "Akin", "亞肯", 47670, 489848, 450451),
        new("AulisCoast", "奧利斯海岸", "Karanza", "卡藍佳", 47753, 1200460, 660099, SpawnId: 16),
        new("OlviaWharf", "奧爾比亞海岸碼頭", "Cedrio", "世德里奧", 40773, -72739.9, 133610, Enabled: true, TransferVerified: true)
    ];
    public static string ChineseNpc(string name) => name switch {
        "Croix" => "克魯瓦", "Dario" => "大立奧", "Bartholomeo" => "巴魯特魯麥奧",
        "Gafur" => "嘉僕勒", "Bolhi" => "波爾利", "Luicy" => "路易西", "Derensha" => "德倫莎",
        "Neltia" => "柰緹歐", "Sungoo" => "善九", "Gurong" => "具龍", "Anax" => "阿洛斯",
        "" => "—", _ => Additional.FirstOrDefault(e => e.Npc == name)?.ChineseNpc ?? name
    };
    public static void Merge(TaggedTransportSettings settings) {
        var known = settings.Ports.Select(p => p.IslandId).ToHashSet();
        settings.Ports = settings.Ports.Concat(Additional.Where(e => !known.Contains(e.Island)).Select(e => new TaggedPort {
            IslandId = e.Island, Npc = e.Npc, WarehouseId = e.Warehouse, Enabled = e.Enabled,
            Evidence = $"Personal wharf NPC: https://bdocodex.com/us/npc/{e.NpcId}/{e.SpawnId}/; "
                + (e.TransferVerified ? "Player cargo-transfer reports; no warehouse link assumed." : "Cargo transfer unverified; enable after in-game confirmation.")
        })).ToArray();
        if (!known.Contains("Crows_Nest")) settings.Ports = [..settings.Ports,
            new() { IslandId = "Crows_Nest", Npc = "Anax", Evidence = "https://bdocodex.com/us/npc/50810/1/" }];
    }
}
