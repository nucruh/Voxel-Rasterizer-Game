using System.Diagnostics;
using OpenTK.Mathematics;
using System.Collections.Concurrent;
using Generations;

public struct BlockPlacement
{
    public Vector3 Position;
    public ushort BlockId;
    public bool OnlyIfAir;

    public BlockPlacement(Vector3 position, ushort blockId, bool onlyIfAir = false)
    {
        Position = position;
        BlockId = blockId;
        OnlyIfAir = onlyIfAir;
    }
}

public class Chunk
{
    public Vector3i Position;
    public const int Size = 32;
    public bool Active = true;
    public bool wasRecalculated = false;
    public Mesh? mesh;
    public Mesh? foliageMesh;
    public ushort[] blockData = new ushort[Size * Size * Size];
    public ushort[] biomeUV = new ushort[Size * Size];
    public int foliageCount = 0;

    public List<float> vertices = new List<float>();
    public List<float> foliageVertices = new List<float>();
    public List<BlockPlacement> overflowBlocks = new List<BlockPlacement>();

    // reused between builds so we don't allocate per slice / per chunk
    [ThreadStatic] private static ushort[]? _mask;
    [ThreadStatic] private static int[]? _p;

    public Chunk(Vector3i position)
    {
        Position = position;
    }

    public static readonly ushort AIR = 0, STONE = 3, GRASS = 2, GRASS_CARD = 64511, DIRT = 1;

    // tweak these
    const float BaseHeight = 32f;
    const float MountainPower = 40f;   // max extra height from mountains
    const float RockHeight = 58f;   // above this: bare stone, no grass

    static float Smooth(float a, float b, float t)
    {
        t = Math.Clamp((t - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    static float HeightAt(float wx, float wz)
    {
        // domain warp: bends everything so features aren't blobby
        float x = wx + PerlinNoise.FBM(wx * 0.01f + 100f, wz * 0.01f + 100f, 2, 0.5f, 2f) * 25f;
        float z = wz + PerlinNoise.FBM(wx * 0.01f - 200f, wz * 0.01f - 200f, 2, 0.5f, 2f) * 25f;

        float hills = PerlinNoise.FBM(x * 0.03f, z * 0.03f, 3, 0.5f, 2f) * 8f;
        float detail = PerlinNoise.FBM(x * 0.1f, z * 0.1f, 2, 0.65f, 2.1042f) * 2f;

        // where mountains are allowed (large, rare regions)
        float mask = Smooth(0.0f, 0.5f, PerlinNoise.FBM(x * 0.005f, z * 0.005f, 2, 0.5f, 2f));

        // ridged noise: sharp crests instead of round bumps
        float ridge = 1f - MathF.Abs(PerlinNoise.FBM(x * 0.015f, z * 0.015f, 3, 0.5f, 2f) * 2f);
        ridge *= ridge;

        return BaseHeight + hills + detail + mask * ridge * MountainPower;
    }

    // cheap deterministic hash, so grass placement is stable between rebuilds
    static float Hash01(int x, int z)
    {
        uint h = (uint)(x * 374761393 + z * 668265263);
        h = (h ^ (h >> 13)) * 1274126177u;
        return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
    }

    public void SetBlock(int x, int y, int z, ushort id, bool onlyIfAir = false)
    {
        if (x >= 0 && x < Size &&
            y >= 0 && y < Size &&
            z >= 0 && z < Size)
        {
            int index = GetLocalIdInChunk(x, y, z);

            if (!onlyIfAir || blockData[index] == AIR)
            {
                blockData[index] = id;
            }
                
        }
        else
        {
            overflowBlocks.Add(new BlockPlacement(
                new Vector3(Position.X + x, Position.Y + y, Position.Z + z),
                id, onlyIfAir));
        }
    }

    public void ApplyOverflow(ConcurrentDictionary<Vector3i, Chunk> chunks)
    {
        int cx = (int)(Position.X / Size);
        int cy = (int)(Position.Y / Size);
        int cz = (int)(Position.Z / Size);

        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                {
                    if (x == 0 && y == 0 && z == 0)
                        continue;

                    int nx = cx + x;
                    int ny = cy + y;
                    int nz = cz + z;

                    if (!chunks.TryGetValue(new Vector3i(nx, ny, nz), out Chunk? neighbor))
                        continue;

                    foreach (var block in neighbor.overflowBlocks)
                    {
                        int localX = (int)(block.Position.X - Position.X);
                        int localY = (int)(block.Position.Y - Position.Y);
                        int localZ = (int)(block.Position.Z - Position.Z);

                        if (localX < 0 || localX >= Size ||
                            localY < 0 || localY >= Size ||
                            localZ < 0 || localZ >= Size)
                            continue;

                        int index = GetLocalIdInChunk(localX, localY, localZ);

                        if (block.OnlyIfAir && blockData[index] != AIR)
                            continue;

                        blockData[index] = block.BlockId;

                    }
                }
    }

    private bool IsAllSame(out ushort id)
    {
        id = blockData[0];
        for (int i = 1; i < blockData.Length; i++)
            if (blockData[i] != id) return false;
        return true;
    }

    public void Generate()
    {
        int py = (int)Position.Y;

        int[,,] treeSpots = new int[Size, Size, Size];

        for (int x = 0; x < Size; x++)
            for (int z = 0; z < Size; z++)
            {
                float wx = Position.X + x, wz = Position.Z + z;

                float h = HeightAt(wx, wz);
                int height = (int)MathF.Floor(h);

                // slope from neighbours: steep = cliff
                float slope = MathF.Max(MathF.Abs(HeightAt(wx + 1, wz) - h),
                                        MathF.Abs(HeightAt(wx, wz + 1) - h));

                bool rocky = slope > 1.6f || height > RockHeight;
                ushort topBlock = rocky ? STONE : GRASS;

                var (temperature, humidity) = BiomeUtility.GetNormalizedBiomeData(wx, wz);
                biomeUV[x + z * Size] = (ushort)((int)(temperature * 255) | ((int)(humidity * 255) << 8));

                for (int y = 0; y < Size; y++)
                {
                    int wy = y + py;
                    ushort id = AIR;
                    if (wy < height - 3)
                        id = STONE;
                    else if (wy < height)
                        id = rocky ? STONE : DIRT;
                    else if (wy == height)
                        id = topBlock;
                    if(id != AIR)
                        SetBlock(x, y, z, id, true);
                }

                int grassY = height - py;
                if (grassY < 0) continue;

                float hash = Hash01((int)wx, (int)wz);

                if (!rocky && hash < 0.85f)
                    SetBlock(x, grassY + 1, z, GRASS_CARD, true);
                else if (!rocky && hash < 0.853f && grassY + 1 < Size)
                    treeSpots[x, grassY + 1, z] = 1;
                else if (!rocky && hash < 0.869f && grassY + 1 < Size)
                    SetBlock(x, grassY + 1, z, 64515, true); // fern
                else if (!rocky && hash < 0.9f && grassY + 1 < Size)
                    SetBlock(x, grassY + 1, z, 64516, true); // andsite pebble
                else if (!rocky && hash < 0.91f && grassY + 1 < Size)
                    SetBlock(x, grassY + 1, z, 64517, true); // porcini shroom
            }


        // tree gen
        for (int _x = 0; _x < Size; _x++)
            for (int _y = 0; _y < Size; _y++)
                for (int _z = 0; _z < Size; _z++)
                {
                    if (treeSpots[_x, _y, _z] != 1)
                        continue;

                    OakTreeGeneration.GenerateOak(this, _x, _y, _z);

                }
    }

    public static readonly ushort LogId = 4;
    public static readonly ushort LeafId = 64512;

    public static int HashPosition(int seed, int x, int y, int z)
    {
        unchecked
        {
            uint h = (uint)seed;
            h ^= (uint)x * 0x9E3779B1u; h = (h << 13) | (h >> 19); h *= 0x85EBCA6Bu;
            h ^= (uint)y * 0xC2B2AE35u; h = (h << 13) | (h >> 19); h *= 0x27D4EB2Fu;
            h ^= (uint)z * 0x165667B1u; h = (h << 13) | (h >> 19); h *= 0x9E3779B1u;
            h ^= h >> 16;
            return (int)h;
        }
    }

    public int GetLocalIdInChunk(int x, int y, int z)
    {
        return x + y * Size + z * Size * Size;
    }

    public ushort GetBiomeUV(int x, int z)
    {
        x = Math.Clamp(x, 0, Size - 1);
        z = Math.Clamp(z, 0, Size - 1);
        return biomeUV[x + z * Size];
    }

    public void CreateMesh(ConcurrentDictionary<Vector3i, Chunk> chunks)
    {
        Stopwatch sw = Stopwatch.StartNew();
        vertices = new List<float>();

        foliageCount = 0;
        foliageVertices = new List<float>();

        int _x = (int)(Position.X / Size);
        int _y = (int)(Position.Y / Size);
        int _z = (int)(Position.Z / Size);

        if (IsAllSame(out ushort first) && first == AIR)
            return; // truly empty chunk, nothing to draw

        // order: +X, -X, +Y, -Y, +Z, -Z (matches GetBlock)
        Chunk[] neighborChunks =
        {
            chunks.GetValueOrDefault(new Vector3i(_x + 1, _y, _z))!,
            chunks.GetValueOrDefault(new Vector3i(_x - 1, _y, _z))!,
            chunks.GetValueOrDefault(new Vector3i(_x, _y + 1, _z))!,
            chunks.GetValueOrDefault(new Vector3i(_x, _y - 1, _z))!,
            chunks.GetValueOrDefault(new Vector3i(_x, _y, _z + 1))!,
            chunks.GetValueOrDefault(new Vector3i(_x, _y, _z - 1))!
        };

        // foliage still needs a per-block pass
        for (int x = 0; x < Size; x++)
            for (int y = 0; y < Size; y++)
                for (int z = 0; z < Size; z++)
                {
                    ushort block = blockData[x + y * Size + z * Size * Size];
                    if (block < 64511)
                        continue; // solid block are handled by greedy meshin

                    if (block == 64511) // grass card
                        GrassGeneration.GrassCardGen(this, x, y, z);
                    else if (block == 64512) // oak leaf
                        OakTreeGeneration.LeafBlockGen(this, x, y, z);
                    else if (block == 64515) // fern
                        FernGeneration.FernGen(this, x, y, z);
                    else if (block == 64516) // pebble
                        GroundPebbleGeneration.PebbleGen(this, x, y, z);
                    else if (block == 64517) // porcini shroom
                        MushroomGeneration.PorciniGen(this, x, y, z);

                }

        // solid blocks: greedy meshed
        GreedyMesh(vertices, neighborChunks);

        sw.Stop();
        //Console.WriteLine($"Chunk {Position} generated in {sw.Elapsed.TotalMicroseconds} us");
    }

    public void ApplyMesh()
    {
        mesh?.Dispose();

        float[] array = vertices.ToArray();
        mesh = new Mesh(array, false, Position);
        if (array.Length == 0)
        {
            mesh.Active = false;
        }

        if (foliageCount != 0)
        {
            foliageMesh?.Dispose();
            foliageMesh = new Mesh(foliageVertices.ToArray(), true, Position);
        }


        vertices.Clear();
        vertices.TrimExcess();
        foliageVertices.Clear();
        foliageVertices.TrimExcess();
        // make some code that disposes the list of vertices and foliageVertices after the mesh is applied, to free up memory, also somehow make it so that apply mesh is run immediately after create mesh for each chunk
        // dont wait for all to generate
    }

    private ushort GetBlock(int x, int y, int z, Chunk[] neighborChunks)
    {
        if (x >= Size) return neighborChunks[0]?.GetBlock(x - Size, y, z, null!) ?? 0;
        if (x < 0) return neighborChunks[1]?.GetBlock(x + Size, y, z, null!) ?? 0;
        if (y >= Size) return neighborChunks[2]?.GetBlock(x, y - Size, z, null!) ?? 0;
        if (y < 0) return neighborChunks[3]?.GetBlock(x, y + Size, z, null!) ?? 0;
        if (z >= Size) return neighborChunks[4]?.GetBlock(x, y, z - Size, null!) ?? 0;
        if (z < 0) return neighborChunks[5]?.GetBlock(x, y, z + Size, null!) ?? 0;

        return blockData[x + y * Size + z * Size * Size];
    }

    // ---------------- Greedy meshing ----------------

    private static readonly int[] AxisU = { 2, 0, 0 };
    private static readonly int[] AxisV = { 1, 2, 1 };

    // 0 = air, >= 64511 = non full blocks (foliage etc.)
    private static bool IsSolid(ushort b) => b != 0 && b < 64511;

    private void GreedyMesh(List<float> vertices, Chunk[] neighborChunks)
    {
        ushort[] mask = _mask ??= new ushort[Size * Size];
        int[] p = _p ??= new int[3];

        for (int d = 0; d < 3; d++)
        {
            int u = AxisU[d], v = AxisV[d];

            for (int dir = -1; dir <= 1; dir += 2)
            {
                // sign of the "right" vector along axis u, matches AddQuad orientation
                int su = d == 2 ? dir : -dir;

                Vector3 look = Vector3.Zero;
                look[d] = dir;

                Vector3 tangent = Vector3.Zero;
                tangent[u] = su;
                Vector3 up = Vector3.Zero;
                up[v] = 1;
                float handedness = Vector3.Dot(Vector3.Cross(look, tangent), up) < 0 ? -1 : 1;
                Vector4 tangentData = new(tangent, handedness);

                for (int s = 0; s < Size; s++)
                {
                    // 1. build the visibility mask for this slice
                    for (int j = 0; j < Size; j++)
                        for (int i = 0; i < Size; i++)
                        {
                            p[d] = s; p[u] = i; p[v] = j;
                            ushort block = GetBlock(p[0], p[1], p[2], neighborChunks);

                            if (!IsSolid(block)) { mask[i + j * Size] = 0; continue; }

                            ushort neighbor = GetBlock(p[0] + (d == 0 ? dir : 0),
                                                       p[1] + (d == 1 ? dir : 0),
                                                       p[2] + (d == 2 ? dir : 0),
                                                       neighborChunks);

                            mask[i + j * Size] = IsSolid(neighbor) ? (ushort)0 : block;
                        }

                    // 2. greedy merge
                    for (int j = 0; j < Size; j++)
                        for (int i = 0; i < Size; i++)
                        {
                            ushort type = mask[i + j * Size];
                            if (type == 0) continue;

                            int w = 1;
                            while (i + w < Size && mask[i + w + j * Size] == type) w++;

                            int h = 1;
                            bool stop = false;
                            while (j + h < Size && !stop)
                            {
                                for (int k = 0; k < w; k++)
                                    if (mask[i + k + (j + h) * Size] != type) { stop = true; break; }
                                if (!stop) h++;
                            }

                            for (int dj = 0; dj < h; dj++)
                                for (int di = 0; di < w; di++)
                                    mask[i + di + (j + dj) * Size] = 0;

                            EmitQuad(vertices, Game._atlas, type, d, u, v, dir, su, s, i, j, w, h,
                                     look, tangentData);

                            i += w - 1;
                        }
                }
            }
        }
    }

    private static readonly (int a, int b)[] QuadCorners =
        { (0, 0), (0, 1), (1, 1), (0, 0), (1, 1), (1, 0) }; // same order as AddQuad

    private void EmitQuad(List<float> vertices, TextureAtlas atlas, ushort type,
                          int d, int u, int v, int dir, int su, int s, int i, int j,
                          int w, int h, Vector3 look, Vector4 tangentData)
    {
        string blockName = BlockRegistry.Blocks[type].Name;

        // atlas cell rect for this block face (assumes Map is linear in uv)
        Vector2 min = atlas.Map(blockName, new Vector2(0, 0), look);
        Vector2 max = atlas.Map(blockName, new Vector2(1, 1), look);
        Vector4 rect = new(min.X, min.Y, max.X - min.X, max.Y - min.Y);

        foreach (var (a, b) in QuadCorners)
        {
            Vector3 pos = Vector3.Zero;
            pos[d] = s + 0.5f * dir;
            pos[u] = su > 0 ? i - 0.5f + a * w : i + w - 0.5f - a * w;
            pos[v] = j - 0.5f + b * h;
            pos += Position;

            MeshUtility.AddVertex(
                vertices, pos, look,
                new Vector2(a * w, b * h),
                tangentData, 0f, rect,
                GetBiomeUV(
                    (int)MathF.Floor(pos.X - Position.X + 0.5f),
                    (int)MathF.Floor(pos.Z - Position.Z + 0.5f)
                )
            );
        }
    }

    public void Unload()
    {
        Active = false;
        mesh?.Active = false;
        foliageMesh?.Active = false;
        //mesh?.Dispose();
    }

    public void Load()
    {
        mesh?.Active = true;
        foliageMesh?.Active = true;
        Active = true;
    }

    public void Dispose()
    {
        vertices.Clear();
        foliageVertices.Clear();

        mesh?.Dispose();
        foliageMesh?.Dispose();

    }
}