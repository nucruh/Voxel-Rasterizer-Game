using OpenTK.Mathematics;
using static Chunk;

namespace Generations;

public static class MushroomGeneration
{
    public static void PorciniGen(Chunk chunk, int x, int y, int z)
    {
        Vector3 _BP = new(chunk.Position.X + x, chunk.Position.Y + y, chunk.Position.Z + z);

        Random rng = new(HashCode.Combine((int)_BP.X, (int)_BP.Y, (int)_BP.Z));

        const float PivotHeight = 0.5f; // "origin" Y from the model json (pivot height above the base)

        int count = rng.Next(1, 4);

        for (int i = 0; i < count; i++)
        {
            float scale = 0.6f + rng.NextSingle() * 0.6f; // 0.6x .. 1.2x
            float yaw = rng.NextSingle() * 360f;

            Vector3 offset = new(
                (rng.NextSingle() - 0.5f) * 0.8f,
                PivotHeight * (scale - 1f), // keeps the base at the same height for any scale
                (rng.NextSingle() - 0.5f) * 0.8f);

            MeshUtility.AddModel(chunk.vertices, "mushroom_porcini_1", _BP + offset, yaw, scale);
        }
    }
}
