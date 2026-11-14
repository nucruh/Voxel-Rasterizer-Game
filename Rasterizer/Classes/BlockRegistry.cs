using System.Text.Json;

public static class BlockRegistry
{
    public static readonly Dictionary<ushort, BlockDefinition> Blocks = [];
    public static readonly Dictionary<string, ushort> NameToId = [];

    public static void Load(string path)
    {
        string json = File.ReadAllText(path);

        var definitions = JsonSerializer.Deserialize<List<BlockJson>>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }
        );

        foreach (var definition in definitions!)
        {
            var block = new BlockDefinition(definition);

            Blocks.Add(block.Id, block);
            NameToId.Add(block.Name, block.Id);
        }

        Console.WriteLine($"Loaded {Blocks.Count} blocks.");
    }
}