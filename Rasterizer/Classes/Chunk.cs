using System.Drawing;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;

public class Chunk
{
    public Vector3 Position;
    public const int Size = 32;
    public bool IsEmpty;
    public Mesh? mesh;
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
                float height = (float)Math.Round(PerlinNoise.Noise((Position.X + x) * 0.1f, (Position.Z + z) * 0.1f) * 10, 0);
                for (int y = 0; y < Size; y++)
                {
                    if (y <= height)
                        blockData[x + y * Size + z * Size * Size] = 1; // stone
                    else
                        blockData[x + y * Size + z * Size * Size] = 0; // air
                }
            }
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

    public void CreateMesh(TextureAtlas textureAtlas)
    {
        List<float> vertices = new List<float>();

        for(int x = 0; x < Size; x++)
            for(int y = 0; y < Size; y++)
                for (int z = 0; z < Size; z++)
                {
                    ushort blockType = blockData[x + y * Size + z * Size * Size];

                    // check neighbors
                    byte neighborCount = 0;
                    bool[] freeFaces = new bool[6];

                    byte i = 0;

                    foreach(Vector3 offset in NeighborOffsets)
                    {
                        if (GetBlock(x + (int)offset.X, y + (int)offset.Y, z + (int)offset.Z) != 0)
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

                    if(neighborCount == 6 || blockType == 0)
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
                            MeshUtility.AddQuad(vertices, NeighborOffsets[i], blockPosition, "stone", textureAtlas);
                        i++;
                    }

                    //MeshUtility.AddCube(vertices, blockPosition, new string[] { "stone", "stone", "stone", "stone", "stone", "stone" }, textureAtlas);
                }

        mesh = new Mesh(vertices.ToArray());
    }

    private ushort GetBlock(int x, int y, int z)
    {
        if (x < 0 || y < 0 || z < 0 || x >= Size || y >= Size || z >= Size)
            return 0;
        return blockData[x + y * Size + z * Size * Size];
    }
}