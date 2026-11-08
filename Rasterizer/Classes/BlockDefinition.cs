public class BlockDefinition
{
    public int Id;
    public string Name;

    // Texture atlas names, ordered by face.
    public string[] Textures;

    // Normal map names, ordered by face.
    public string[] NormalMaps;

    public bool IsTransparent;
    public bool IsFoliage;

    public BlockDefinition(
        int id,
        string name,
        string texture,
        bool isTransparent = false,
        bool isFoliage = false)
    {
        Id = id;
        Name = name;

        Textures = Enumerable.Repeat(texture, 6).ToArray();
        NormalMaps = Enumerable.Repeat("normal", 6).ToArray();

        IsTransparent = isTransparent;
        IsFoliage = isFoliage;
    }

    public BlockDefinition(
        int id,
        string name,
        string[] textures,
        string[]? normalMaps = null,
        bool isTransparent = false,
        bool isFoliage = false)
    {
        Id = id;
        Name = name;

        Textures = textures;
        NormalMaps = normalMaps ?? Enumerable.Repeat("normal", 6).ToArray();

        IsTransparent = isTransparent;
        IsFoliage = isFoliage;
    }
}