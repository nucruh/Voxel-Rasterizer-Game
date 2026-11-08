public static class TextureNames
{
    public const string TextureRoot = "Textures";
    public static readonly string DiffuseFolder = ResolveFolder("Diffuse");
    public static readonly string NormalFolder = ResolveFolder("Normal");
    public static readonly string RoughnessFolder = Path.Combine(TextureRoot, "Roughness");

    private static string ResolveFolder(string category)
    {
        string folder = Path.Combine(TextureRoot, category);
        return Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*.png").Any()
            ? folder
            : TextureRoot;
    }

    public static readonly Dictionary<int, BlockDefinition> Blocks = new()
    {
        [1] = new(1, "dirt_loam", "dirt_loam"),
        [2] = new(2, "grass_loam", new[]
        {
            "grass_side_loam",
            "grass_side_loam",
            "grass_top_loam",
            "dirt_loam",
            "grass_side_loam",
            "grass_side_loam"
        }),
        [3] = new(3, "andesite_stone", "andesite_stone"),
        [4] = new(4, "oak_log", "oak_log"),

        [64511] = new(64511, "grass_card", "grass_card", isTransparent: true, isFoliage: true),

        [64512] = new(64512, "oak_leaf", "oak_leaf", isTransparent: true, isFoliage: true),

        [64513] = new(64513, "short_grass_card", "short_grass_card", isTransparent: true, isFoliage: true),

        [64514] = new(64514, "dandelion", "dandelion", isTransparent: true, isFoliage: true)
    };

    public static readonly string[] Names = Directory
        .GetFiles(DiffuseFolder, "*.png")
        .Select(Path.GetFileNameWithoutExtension)
        .Where(x => !x.Contains("normal", StringComparison.OrdinalIgnoreCase))
        .OrderBy(x => x)
        .ToArray()!;

    // One source filename per diffuse atlas slot. Do not remove duplicates:
    // the normal atlas must have the same slot layout as the diffuse atlas.
    public static readonly string[] NormalNames = Names
        .Select(x => File.Exists(Path.Combine(NormalFolder, x + ".png"))
            ? x
            : File.Exists(Path.Combine(NormalFolder, x + "_normal.png"))
                ? x + "_normal"
                : "normal")
        .ToArray();

    public static readonly Dictionary<string, int> NameToId = Blocks.Values.ToDictionary(x => x.Name, x => x.Id);
}