using OpenTK.Mathematics;
using static Chunk;

namespace Generations;

public static class OakTreeGeneration
{
    public static void LeafBlob(Chunk chunk, int cx, int cy, int cz, int radius, Random rng)
    {
        double ry = radius * 0.7; // flattened vertically, like a real oak crown

        for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -(int)Math.Ceiling(ry); dy <= (int)Math.Ceiling(ry); dy++)
                for (int dz = -radius; dz <= radius; dz++)
                {
                    double d = (dx * dx) / (double)(radius * radius)
                             + (dy * dy) / (ry * ry)
                             + (dz * dz) / (double)(radius * radius);

                    if (d > 1.0) continue;

                    // Ragged edges: randomly drop outer voxels
                    if (d > 0.65 && rng.NextDouble() < 0.4) continue;

                    chunk.SetBlock(cx + dx, cy + dy, cz + dz, LeafId, true);
                }
    }

    public static void GenerateOak(Chunk chunk, int x, int y, int z)
    {
        var rng = new Random(HashPosition(Game._worldSeed, x, y, z));

        int trunkHeight = rng.Next(7, 11);

        // Trunk
        for (int i = 0; i < trunkHeight; i++)
            chunk.SetBlock(x, y + i, z, LogId);

        // Root flare at the base
        foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            if (rng.NextDouble() < 0.6)
            {
                chunk.SetBlock(x + dx, y, z + dz, LogId);
                if (rng.NextDouble() < 0.3)
                    chunk.SetBlock(x + dx, y + 1, z + dz, LogId);
            }

        // Branches: start in the upper half of the trunk, spread outward and rise
        int branchCount = rng.Next(4, 7);
        double angleOffset = rng.NextDouble() * Math.PI * 2;

        for (int b = 0; b < branchCount; b++)
        {
            // Evenly spread angles with some jitter, so branches don't clump
            double angle = angleOffset + b * (Math.PI * 2 / branchCount)
                           + (rng.NextDouble() - 0.5) * 0.6;

            int startY = y + (int)(trunkHeight * (0.5 + rng.NextDouble() * 0.4));
            int length = rng.Next(3, 6);
            double rise = 0.4 + rng.NextDouble() * 0.4; // vertical blocks per horizontal block

            double dirX = Math.Cos(angle);
            double dirZ = Math.Sin(angle);

            int bx = x, by = startY, bz = z;
            double fy = startY;

            for (int s = 1; s <= length; s++)
            {
                bx = x + (int)Math.Round(dirX * s);
                bz = z + (int)Math.Round(dirZ * s);
                fy += rise;
                by = (int)Math.Round(fy);

                chunk.SetBlock(bx, by, bz, LogId);

                // Fill diagonal gaps so the branch stays connected
                chunk.SetBlock(bx, by - 1, bz, LogId);
            }

            // Leaf cluster at the branch tip
            LeafBlob(chunk, bx, by + 1, bz, rng.Next(3, 6), rng);
        }

        // Main crown on top of the trunk
        LeafBlob(chunk, x, y + trunkHeight, z, 3, rng);
    }


    public static void LeafBlockGen(Chunk chunk, int x, int y, int z)
    {
        Vector3 _BP = new(chunk.Position.X + x, chunk.Position.Y + y, chunk.Position.Z + z);
        chunk.foliageCount++;

        for (int i = 0; i < 3; i++)
        {
            float pitch = Random.Shared.NextSingle() * MathF.PI;
            float yaw = Random.Shared.NextSingle() * MathF.Tau;
            float roll = Random.Shared.NextSingle() * MathF.Tau;

            Quaternion rotation =
                Quaternion.FromAxisAngle(Vector3.UnitX, pitch) *
                Quaternion.FromAxisAngle(Vector3.UnitY, yaw) *
                Quaternion.FromAxisAngle(Vector3.UnitZ, roll);

            Vector3 look = Vector3.Transform(Vector3.UnitZ, rotation);

            float scale = 1.1f + Random.Shared.NextSingle() * 0.4f;

            MeshUtility.AddQuad(
                chunk.foliageVertices,
                look,
                _BP,
                "oak_leaf",
                true, true, true,
                0.2f, 0.2f, scale,
                chunk.GetBiomeUV(x, z)
                );
        }
    }
}