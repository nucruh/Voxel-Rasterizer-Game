public enum ItemSize
{
    Tiny, // 64
    Small, // 32
    Normal, // 16
    Medium, // 8
    Large, // 4
    Huge // 1
}

public class BlockDefinition
{
    public ushort Id;
    public string Name;
    public string DisplayName;
    public string Description;
    public ItemSize ItemSize;

    public string[] Textures;
    public string[] NormalMaps;

    public bool Solid;
    public bool Foliage;
    public bool Transparent;

    public byte HardnessLevel;
    public string[] ModelNames;

    public HashSet<string> Tags = [];

    public BlockDefinition(BlockJson json)
    {
        Id = json.Id;
        Name = json.Name;
        DisplayName = json.DisplayName;
        Description = json.Description;
        ItemSize = Enum.Parse<ItemSize>(json.ItemSize, true);

        Textures = ResolveFaces(json.Albedo);
        NormalMaps = ResolveFaces(json.Normal);

        Solid = json.Solid;
        Foliage = json.Foliage;
        Transparent = json.Transparent;

        HardnessLevel = json.HardnessLevel;
        ModelNames = json.Model ?? [];

        Tags = json.Tags.ToHashSet();
    }

    private static string[] ResolveFaces(Dictionary<string, string> textures)
    {
        string all = textures.GetValueOrDefault("all", "normal");
        string side = textures.GetValueOrDefault("side", all);

        return
        [
            textures.GetValueOrDefault("east", side),
            textures.GetValueOrDefault("west", side),
            textures.GetValueOrDefault("top", all),
            textures.GetValueOrDefault("bottom", all),
            textures.GetValueOrDefault("south", side),
            textures.GetValueOrDefault("north", side)
        ];
    }
}

public class BlockJson
{
    public ushort Id { get; set; }
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string ItemSize { get; set; } = "normal";

    public Dictionary<string, string> Albedo { get; set; } = [];
    public Dictionary<string, string> Normal { get; set; } = [];

    public bool Solid { get; set; }
    public bool Foliage { get; set; }
    public bool Transparent { get; set; }

    public byte HardnessLevel { get; set; }
    public string[]? Model { get; set; }

    public string[] Tags { get; set; } = [];
}