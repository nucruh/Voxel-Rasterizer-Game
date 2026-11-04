public class TextureNames
{
    // Index in this list = texture id. Loads Textures/<name>.png (32x32).
    // Index = lineNum - 6
    public static readonly string[] Names = {
        "stone",
        "dirt_loam",
        "grass_side_loam",
        "grass_top_loam",
        "grass_card",
    };

    // actual blocks here
    public static readonly Dictionary<string, List<string>> NameToNames = new()
    {
        ["grass_card"] = new List<string> { "grass_card", "grass_card", "grass_card", "grass_card", "grass_card", "grass_card" },
        ["stone"] = new List<string> { "stone", "stone", "stone", "stone", "stone", "stone" },
        ["dirt_loam"] = new List<string> { "dirt_loam", "dirt_loam", "dirt_loam", "dirt_loam", "dirt_loam", "dirt_loam" },
        ["grass_loam"] = new List<string> {"grass_side_loam", "grass_side_loam", "grass_top_loam", "dirt_loam", "grass_side_loam", "grass_side_loam"}
    };

    public static readonly Dictionary<string, List<string>> NameToNames2 = new()
    {
        ["grass_card"] = new List<string> { "normal", "normal", "normal", "normal", "normal", "normal" },
        ["stone"] = new List<string> { "normal", "normal", "normal", "normal", "normal", "normal" },
        ["dirt_loam"] = new List<string> { "normal", "normal", "normal", "normal", "normal", "normal" },
        ["grass_loam"] = new List<string> { "normal", "normal", "normal", "normal", "normal", "normal" },
    };

    public static readonly Dictionary<string, int> NameToId = new()
    {
        ["stone"] = 0,
        ["dirt_loam"] = 1,
        ["grass_loam"] = 2,
        ["grass_card"] = 64511
    };

    public static readonly Dictionary<int, string> IdToName = NameToId.ToDictionary(x => x.Value, x => x.Key);
}