using OpenTK.Mathematics;
using static Chunk;

namespace Generations;

public static class GroundPebbleGeneration
{
    public static void PebbleGen(Chunk chunk, int x, int y, int z)
    {
        Vector3 _BP = new(chunk.Position.X + x, chunk.Position.Y + y, chunk.Position.Z + z);

        MeshUtility.AddModel(chunk.vertices, "andesite_pebble_1", _BP, 90f);
    }
}