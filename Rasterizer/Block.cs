using OpenTK.Mathematics;

public static class LoadedBlocks
{
    public static BlockDefinition[] Definitions { get; } = new BlockDefinition[65536];
}

public class Block
{
    public BlockDefinition Definition;
    public Model? BlockModel;


    public Block(ushort id)
    {
        Definition = LoadedBlocks.Definitions[id];

    }
}

public class BlockState
{
    public Vector3i Chunk;
    public Vector3i Position;
    public ushort BlockId;
}