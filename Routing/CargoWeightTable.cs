namespace iBarter.Routing;

public static class CargoWeightTable {
    // Verified against BDO Codex item pages; see docs/land-material-weights-2026-09-17.json.
    private static readonly IReadOnlyDictionary<string, double> LandWeights = new Dictionary<string, double> {
        ["7347"] = 0.1, // Aloe
        ["9213"] = 0.1, // Beer
        ["4661"] = 0.5, // Birch Plywood
        ["5005"] = 0.1, // Bloody Tree Knot
        ["4066"] = 0.3, // Brass Ingot
        ["4067"] = 0.3, // Bronze Ingot
        ["4612"] = 0.5, // Cactus Rind
        ["4683"] = 0.5, // Cactus Thorn
        ["4725"] = 0.5, // Caphras Tree Plywood
        ["4667"] = 0.5, // Cedar Plywood
        ["7921"] = 0.03, // Chicken Meat
        ["7348"] = 0.1, // Cinnamon
        ["5301"] = 0.1, // Clear Liquid Reagent
        ["6353"] = 0.1, // Clown's Blood
        ["7026"] = 0.1, // Coconut
        ["7702"] = 0.1, // Cooking Honey
        ["4058"] = 0.3, // Copper Ingot
        ["5855"] = 0.1, // Cotton Fabric
        ["7016"] = 0.1, // Date Palm
        ["4674"] = 0.5, // Elder Tree Plywood
        ["9057"] = 0.01, // Essence of Liquor
        ["6158"] = 0.1, // Fancy Feather
        ["7018"] = 0.1, // Fig
        ["6166"] = 0.1, // Fine Fancy Feather
        ["6161"] = 0.1, // Fine Hard Hide
        ["6165"] = 0.1, // Fine Lightweight Plume
        ["6159"] = 0.1, // Fine Soft Hide
        ["6163"] = 0.1, // Fine Thick Fur
        ["6160"] = 0.1, // Fine Tough Hide
        ["4664"] = 0.5, // Fir Plywood
        ["6185"] = 0.1, // Fire Horn
        ["5856"] = 0.1, // Flax Fabric
        ["5852"] = 0.1, // Flax Thread
        ["5804"] = 0.1, // Fleece
        ["7021"] = 0.1, // Freekeh
        ["9492"] = 0.1, // Grilled Bird Meat
        ["7958"] = 0.03, // Ground Bird Meat
        ["6153"] = 0.1, // Hard Hide
        ["4052"] = 0.3, // Iron Ingot
        ["5854"] = 0.1, // Knitting Yarn
        ["4055"] = 0.3, // Lead Ingot
        ["6351"] = 0.1, // Legendary Beast's Blood
        ["6157"] = 0.1, // Lightweight Plume
        ["4698"] = 0.5, // Loopy Tree Plywood
        ["4655"] = 0.5, // Maple Plywood
        ["4695"] = 0.5, // Moss Tree Plywood
        ["820138"] = 0.1, // Mysterious Powder
        ["4086"] = 0.3, // Noc Ingot
        ["7020"] = 0.1, // Nutmeg
        ["5008"] = 0.1, // Old Tree Bark
        ["4671"] = 0.5, // Palm Plywood
        ["4658"] = 0.5, // Pine Plywood
        ["7017"] = 0.1, // Pistachio
        ["4801"] = 0.1, // Powder of Darkness
        ["4804"] = 0.1, // Powder of Earth
        ["4802"] = 0.1, // Powder of Flame
        ["4803"] = 0.1, // Powder of Rifts
        ["4805"] = 0.1, // Powder of Time
        ["4070"] = 0.3, // Processed Coal
        ["7306"] = 0.1, // Pumpkin
        ["5302"] = 0.1, // Pure Powder Reagent
        ["5011"] = 0.1, // Red Tree Lump
        ["4494"] = 0.3, // Resplendent Jade
        ["4273"] = 0.3, // Resplendent Obsidian
        ["4414"] = 0.3, // Rough Jade
        ["9733"] = 0.1, // Shining Powder
        ["5857"] = 0.1, // Silk
        ["5853"] = 0.1, // Silk Thread
        ["6354"] = 0.1, // Sinner's Blood
        ["4722"] = 0.5, // Snowfield Cedar Plywood
        ["6151"] = 0.1, // Soft Hide
        ["5006"] = 0.1, // Spirit's Leaf
        ["7019"] = 0.1, // Star Anise
        ["820158"] = 0.1, // Supreme Bear Hide
        ["820157"] = 0.1, // Supreme Boar Hide
        ["6155"] = 0.1, // Thick Fur
        ["4711"] = 0.5, // Thornwood Plywood
        ["4702"] = 0.5, // Thuja Plywood
        ["7959"] = 0.03, // Tiger Meat
        ["4061"] = 0.3, // Tin Ingot
        ["6152"] = 0.1, // Tough Hide
        ["9066"] = 0.01, // Vinegar
        ["4677"] = 0.5, // White Cedar Plywood
        ["6355"] = 0.1, // Wise Man's Blood
        ["5858"] = 0.1, // Wool
        ["4064"] = 0.3, // Zinc Ingot
    };

    public static double GetWeight(string itemId, int level) =>
        level == 0 ? LandWeights.GetValueOrDefault(itemId, 0.1) : GetWeightForLevel(level);

    public static int GetWeightForLevel(int level) => level switch {
        1 => 100,
        2 => 400,
        3 => 900,
        4 or 5 => 1000,
        6 or 7 => 2000,
        _ => 0,
    };
}
