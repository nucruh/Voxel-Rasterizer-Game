public class TextureNames
{
    // Index in this list = texture id. Loads Textures/<name>.png (32x32).
    // Index = lineNum - 6
    public static readonly string[] Names = {
        "andesite_stone",
        "dirt_loam",
        "grass_side_loam",
        "grass_top_loam",
        "grass_card",
        "oak_log",
        "oak_leaf"
    };

    // actual blocks here
    public static readonly Dictionary<string, List<string>> NameToNames = new()
    {
        ["grass_card"] = new List<string> { "grass_card", "grass_card", "grass_card", "grass_card", "grass_card", "grass_card" },
        ["oak_leaf"] = new List<string> { "oak_leaf", "oak_leaf", "oak_leaf", "oak_leaf", "oak_leaf", "oak_leaf" },
        ["oak_log"] = new List<string> { "oak_log", "oak_log", "oak_log", "oak_log", "oak_log", "oak_log" },
        ["andesite_stone"] = new List<string> { "andesite_stone", "andesite_stone", "andesite_stone", "andesite_stone", "andesite_stone", "andesite_stone" },
        ["dirt_loam"] = new List<string> { "dirt_loam", "dirt_loam", "dirt_loam", "dirt_loam", "dirt_loam", "dirt_loam" },
        ["grass_loam"] = new List<string> {"grass_side_loam", "grass_side_loam", "grass_top_loam", "dirt_loam", "grass_side_loam", "grass_side_loam"}
    };


    // normal maps
    public static readonly Dictionary<string, List<string>> NameToNames2 = new()
    {
        ["grass_card"] = new List<string> { "normal", "normal", "normal", "normal", "normal", "normal" },
        ["andesite_stone"] = new List<string> { "normal", "normal", "normal", "normal", "normal", "normal" },
        ["dirt_loam"] = new List<string> { "normal", "normal", "normal", "normal", "normal", "normal" },
        ["grass_loam"] = new List<string> { "normal", "normal", "normal", "normal", "normal", "normal" },
        ["oak_log"] = new List<string> { "normal", "normal", "normal", "normal", "normal", "normal" },
        ["oak_leaf"] = new List<string> { "normal", "normal", "normal", "normal", "normal", "normal" }
    };

    public static readonly Dictionary<string, int> NameToId = new()
    {
        ["andesite_stone"] = 3,
        ["dirt_loam"] = 1,
        ["grass_loam"] = 2,
        ["oak_log"] = 4,
        ["grass_card"] = 64511,
        ["oak_leaf"] = 64512
    };

    public static readonly Dictionary<int, string> IdToName = NameToId.ToDictionary(x => x.Value, x => x.Key);
}