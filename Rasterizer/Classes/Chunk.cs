using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;

public class Chunk
{
    public Vector3 Position;
    public const int Size = 32;
    public bool IsEmpty;
    public Mesh? mesh;
    public Mesh? foliageMesh;
    public ushort[] blockData = new ushort[Size * Size * Size];

    public Chunk(Vector3 position)
    {
        Position = position;
        IsEmpty = false;
    }

    public void Generate()
    {
        for(int x = 0; x < Size; x++)
            for(int z = 0; z < Size; z++)
            {
                float height = (float)Math.Round(PerlinNoise.FBM((Position.X + x) * 0.1f, (Position.Z + z) * 0.1f, 2, 0.65f, 2.1042f) * 10, 0);
                for (int y = 0; y < Size; y++)
                {
                    if (y + (int)Position.Y <= height)
                    {
                        blockData[GetLocalIdInChunk(x, y, z)] = 1;
                        if (y + (int)Position.Y == height)
                            blockData[GetLocalIdInChunk(x, y, z)] = 2; // grass
                    }
                    else
                        blockData[GetLocalIdInChunk(x, y, z)] = 0; // air
                }
                if ((int)height + 1 < 0 || (int)height + 1 > Size)
                    continue;

                blockData[GetLocalIdInChunk(x, (int)height + 1, z)] = 64511; // grass
            }
    }

    public int GetLocalIdInChunk(int x, int y, int z)
    {
        return x + y * Size + z * Size * Size;
    }

    private Vector3[] NeighborOffsets = new Vector3[]
    {
        new Vector3(-1, 0, 0), // left
        new Vector3(1, 0, 0),  // right
        new Vector3(0, -1, 0), // down
        new Vector3(0, 1, 0),  // up
        new Vector3(0, 0, -1), // back
        new Vector3(0, 0, 1)   // front
    };

    public void CreateMesh(TextureAtlas textureAtlas, TextureAtlas normalAtlas, Chunk[,,] chunks)
    {
        Stopwatch sw = Stopwatch.StartNew();
        List<float> vertices = new List<float>();

        int foliageCount = 0;
        List<float> foliageVertices = new List<float>();

        int _x = (int)Position.X;
        int _y = (int)Position.Y;
        int _z = (int)Position.Z;

        Chunk[] neighborChunks =
        {
            chunks[_x + 1, _y, _z], // +X
            chunks[_x - 1, _y, _z], // -X
            chunks[_x, _y + 1, _z], // +Y
            chunks[_x, _y - 1, _z], // -Y
            chunks[_x, _y, _z + 1], // +Z
            chunks[_x, _y, _z - 1]  // -Z
        };

        for (int x = 0; x < Size; x++)
            for(int y = 0; y < Size; y++)
                for (int z = 0; z < Size; z++)
                {
                    ushort blockType = blockData[x + y * Size + z * Size * Size];

                    // special cases
                    if(blockType > 64510)
                    {
                        if(blockType == 64511) // grass card
                        {
                            Vector3 _BP = new Vector3(Position.X + x, Position.Y + y, Position.Z + z);
                            foliageCount++;
                            float randomOffset = (float)(new Random().NextDouble() * 0.3f - 0.15f);
                            Vector3 ranOffsetV = new Vector3(randomOffset, 0, randomOffset);

                            for (int j = 0; j < 4; j++)
                            {
                                float angle = j * MathHelper.PiOver4;

                                Vector3 look = Vector3.TransformNormal(
                                    new Vector3(1, 0, 1),
                                    Matrix4.CreateRotationY(angle)
                                );

                                float offsetX = (float)(Random.Shared.NextDouble() * 0.16f - 0.08f);
                                float offsetZ = (float)(Random.Shared.NextDouble() * 0.16f - 0.08f);

                                Vector3 offset = _BP - ranOffsetV + new Vector3(offsetX, 0, offsetZ);

                                MeshUtility.AddQuad(
                                    foliageVertices,
                                    look,
                                    offset,
                                    "grass_card",
                                    textureAtlas,
                                    true,
                                    normalAtlas,
                                    true,
                                    true
                                );
                            }

                        }

                        continue;
                    }

                    if (blockType == 0)
                        continue; // skip air blocks

                    // check neighbors
                    byte neighborCount = 0;
                    bool[] freeFaces = new bool[6];

                    byte i = 0;

                    foreach(Vector3 offset in NeighborOffsets)
                    {
                        ushort neighborBlockType = GetBlock(x + (int)offset.X, y + (int)offset.Y, z + (int)offset.Z);
                        // 1024 reserved for non full blocks
                        if (neighborBlockType != 0 && (neighborBlockType < 64511))
                        {
                            neighborCount++;
                            freeFaces[i] = false;
                        }
                        else
                        {
                            freeFaces[i] = true;
                        }
                        i++;
                    }

                    if(neighborCount == 6)
                    {
                        // all neighbors are solid, skip this block
                        continue;
                    }

                    // If the block is not air, add it to the mesh
                    i = 0;
                    Vector3 blockPosition = new Vector3(Position.X + x, Position.Y + y, Position.Z + z);
                    foreach(bool free in freeFaces)
                    {
                        if(free)
                            MeshUtility.AddQuad(vertices, NeighborOffsets[i], blockPosition, blockType, textureAtlas, false, normalAtlas);
                        i++;
                    }

                    //MeshUtility.AddCube(vertices, blockPosition, new string[] { "stone", "stone", "stone", "stone", "stone", "stone" }, textureAtlas);
                }

        mesh = new Mesh(vertices.ToArray());

        if(foliageCount != 0)
            foliageMesh = new Mesh(foliageVertices.ToArray(), true);

        sw.Stop();
        Console.WriteLine($"Chunk {Position} generated in {sw.Elapsed.TotalMicroseconds} us");
    }

    private ushort GetBlock(int x, int y, int z, Chunk[] neighborChunks)
    {
        if (x < 0 || y < 0 || z < 0 || x >= Size || y >= Size || z >= Size)
            return 0;
        return blockData[x + y * Size + z * Size * Size];
    }
}