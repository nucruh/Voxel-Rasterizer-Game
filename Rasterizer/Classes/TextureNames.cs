using System.IO;

public static class TextureNames
{
    public const string TextureRoot = "Textures";

    public static readonly string DiffuseFolder = ResolveFolder("Diffuse");
    public static readonly string NormalFolder = ResolveFolder("Normal");
    public static readonly string RoughnessFolder = Path.Combine(TextureRoot, "Roughness");

    public static readonly string[] Names = Directory
        .GetFiles(DiffuseFolder, "*.png")
        .Select(Path.GetFileNameWithoutExtension)
        .Where(x => !x!.Contains("normal", StringComparison.OrdinalIgnoreCase))
        .OrderBy(x => x)
        .ToArray()!;

    public static readonly string[] NormalNames = Names
        .Select(x => File.Exists(Path.Combine(NormalFolder, x + ".png"))
            ? x
            : File.Exists(Path.Combine(NormalFolder, x + "_normal.png"))
                ? x + "_normal"
                : "normal")
        .ToArray();

    private static string ResolveFolder(string category)
    {
        string folder = Path.Combine(TextureRoot, category);

        return Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*.png").Any()
            ? folder
            : TextureRoot;
    }
}