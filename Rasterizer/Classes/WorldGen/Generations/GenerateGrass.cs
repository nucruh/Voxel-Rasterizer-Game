using OpenTK.Mathematics;
using static Chunk;

namespace Generations;

public static class GrassGeneration
{
    public static void GrassCardGen(Chunk chunk, int x, int y, int z)
    {
        Vector3 _BP = new Vector3(chunk.Position.X + x, chunk.Position.Y + y, chunk.Position.Z + z);
        chunk.foliageCount++;
        float randomOffset = (float)(Random.Shared.NextDouble() * 0.3f - 0.15f);
        Vector3 ranOffsetV = new Vector3(randomOffset, 0, randomOffset);

        int hash = HashPosition(Game._worldSeed, x, y, z);

        var rng = new Random(hash);
        float value = rng.NextSingle();

        ushort target = value < 0.5f ? (ushort)64511 : (ushort)64513; // grass_card or short_grass_card
        target = value < 0.1f ? (ushort)64514 : target; // dandelion

        float size_mod = 0.0f;

        if (target == 64511)
        {
            size_mod = 0.414f;
        }

        for (int j = 0; j < 2; j++)
        {
            float angle = j * MathHelper.PiOver2 + 45f;

            Vector3 look = Vector3.TransformNormal(
                new Vector3(1, 0, 0),
                Matrix4.CreateRotationY(angle));

            float offsetX = (float)(Random.Shared.NextDouble() * 0.04f - 0.02f);
            float offsetZ = (float)(Random.Shared.NextDouble() * 0.04f - 0.02f);

            Vector3 offset = _BP - ranOffsetV + new Vector3(offsetX, size_mod / 2.0f, offsetZ);

            MeshUtility.AddQuad(
                chunk.foliageVertices,
                look,
                offset,
                target,
                skipRecenter: true,
                heightPaint: true,
                upNormal: true,
                minWeight: 0.0f,
                maxWeight: 1.0f,
                size: 1.0f + size_mod,
                biomeData: chunk.GetBiomeUV(x, z));
        }
    }
}
