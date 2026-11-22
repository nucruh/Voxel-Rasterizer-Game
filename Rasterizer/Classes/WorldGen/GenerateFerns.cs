using OpenTK.Mathematics;
using static Chunk;

namespace Generations;

public static class FernGeneration
{
    public static void FernGen(Chunk chunk, int x, int y, int z)
    {
        Vector3 _BP = new(chunk.Position.X + x, chunk.Position.Y + y, chunk.Position.Z + z);
        chunk.foliageCount++;

        for (int i = 0; i < 12; i++)
        {
            float yaw = Random.Shared.NextSingle() * MathF.Tau;
            float pitch = 0.4f + Random.Shared.NextSingle() * 0.9f;

            Quaternion rotation =
                Quaternion.FromAxisAngle(Vector3.UnitY, yaw) *
                Quaternion.FromAxisAngle(Vector3.UnitX, pitch);

            Vector3 look = Vector3.Transform(Vector3.UnitZ, rotation);
            Vector3 up = Vector3.Transform(Vector3.UnitY, rotation);

            float height = 0.65f + Random.Shared.NextSingle() * 0.5f;
            float width = 0.65f + Random.Shared.NextSingle() * 0.4f;

            MeshUtility.AddQuadAnchored(
                chunk.foliageVertices,
                look,
                _BP + Vector3.UnitY * -0.5f,
                "fern",
                width,
                height,
                true, true,
                0.0f, 0.2f,
                chunk.GetBiomeUV(x, z)
                );
        }
    }
}
