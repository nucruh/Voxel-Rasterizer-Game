using OpenTK.Mathematics;

public struct RaycastHit
{
    public Vector3i BlockPosition;
    public Vector3i Normal;
    public Vector3 Position;
    public ushort BlockId;
    public float Distance;
}

public static class VoxelRaycaster
{
    public static bool Raycast(
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        bool ignoreNonSolid,
        out RaycastHit hit)
    {
        hit = default;

        direction = Vector3.Normalize(direction);

        Vector3i block = new Vector3i(
            (int)MathF.Floor(origin.X),
            (int)MathF.Floor(origin.Y),
            (int)MathF.Floor(origin.Z)
        );

        Vector3i step = new Vector3i(
            Math.Sign(direction.X),
            Math.Sign(direction.Y),
            Math.Sign(direction.Z)
        );

        Vector3 delta = new Vector3(
            direction.X != 0 ? MathF.Abs(1f / direction.X) : float.PositiveInfinity,
            direction.Y != 0 ? MathF.Abs(1f / direction.Y) : float.PositiveInfinity,
            direction.Z != 0 ? MathF.Abs(1f / direction.Z) : float.PositiveInfinity
        );

        Vector3 side = new Vector3(
            direction.X > 0 ? (block.X + 1 - origin.X) * delta.X :
            direction.X < 0 ? (origin.X - block.X) * delta.X :
            float.PositiveInfinity,

            direction.Y > 0 ? (block.Y + 1 - origin.Y) * delta.Y :
            direction.Y < 0 ? (origin.Y - block.Y) * delta.Y :
            float.PositiveInfinity,

            direction.Z > 0 ? (block.Z + 1 - origin.Z) * delta.Z :
            direction.Z < 0 ? (origin.Z - block.Z) * delta.Z :
            float.PositiveInfinity
        );

        Vector3i normal = Vector3i.Zero;
        float distance = 0;

        while (distance <= maxDistance)
        {
            ushort id = GetBlock(block.X, block.Y, block.Z);

            bool ignored = ignoreNonSolid && id != 0 && !BlockRegistry.Blocks[id].Solid;

            if (id != 0 && !ignored)
            {
                hit = new RaycastHit
                {
                    BlockPosition = block,
                    Normal = normal,
                    Position = origin + direction * distance,
                    BlockId = id,
                    Distance = distance
                };

                return true;
            }

            if (side.X < side.Y && side.X < side.Z)
            {
                distance = side.X;
                side.X += delta.X;

                block.X += step.X;
                normal = new Vector3i(-step.X, 0, 0);
            }
            else if (side.Y < side.Z)
            {
                distance = side.Y;
                side.Y += delta.Y;

                block.Y += step.Y;
                normal = new Vector3i(0, -step.Y, 0);
            }
            else
            {
                distance = side.Z;
                side.Z += delta.Z;

                block.Z += step.Z;
                normal = new Vector3i(0, 0, -step.Z);
            }
        }

        return false;
    }

    private static ushort GetBlock(int x, int y, int z)
    {
        Vector3i chunkPos = new Vector3i(
            (int)MathF.Floor(x / (float)Chunk.Size),
            (int)MathF.Floor(y / (float)Chunk.Size),
            (int)MathF.Floor(z / (float)Chunk.Size)
        );

        if (!Game._chunks.TryGetValue(chunkPos, out Chunk? chunk))
            return 0;

        int localX = x - chunkPos.X * Chunk.Size;
        int localY = y - chunkPos.Y * Chunk.Size;
        int localZ = z - chunkPos.Z * Chunk.Size;

        return chunk.blockData[
            chunk.GetLocalIdInChunk(localX, localY, localZ)
        ];
    }
}