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
}