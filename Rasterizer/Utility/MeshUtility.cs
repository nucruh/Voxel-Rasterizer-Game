using OpenTK.Mathematics;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public struct Vertex   // 17 floats, same layout the shader already expects
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 UV;
    public Vector4 Tangent;
    public float Weight;
    public Vector4 TileRect;
}


public static class MeshUtility
{
    public const int FloatsPerVertex = 17;


    // pos(3) normal(3) uv(2) tangent(4) weight(1) tileRect(4)
    // tileRect = (atlasMinU, atlasMinV, cellWidth, cellHeight). Zero width = no tiling (foliage).
    public static void AddVertex(List<float> v, Vector3 p, Vector3 normal, Vector2 uv,
                                 Vector4 tangent, float weight, Vector4 tileRect = default)
    {
        v.Add(p.X); v.Add(p.Y); v.Add(p.Z);
        v.Add(normal.X); v.Add(normal.Y); v.Add(normal.Z);
        v.Add(uv.X); v.Add(uv.Y);
        v.Add(tangent.X); v.Add(tangent.Y); v.Add(tangent.Z); v.Add(tangent.W);
        v.Add(weight);
        v.Add(tileRect.X); v.Add(tileRect.Y); v.Add(tileRect.Z); v.Add(tileRect.W);
    }

    private static readonly Vector2[] QuadUVs =
    {
        new(0, 0),
        new(0, 1),
        new(1, 1),
        new(0, 0),
        new(1, 1),
        new(1, 0)
    };

    public static void AddQuad(
        List<float> v,
        Vector3 look,
        Vector3 offset,
        string texture,
        bool skipRecenter = false,
        bool heightPaint = false,
        bool upNormal = false,
        float minWeight = 0.0f,
        float maxWeight = 0.0f,
        float size = 1.0f
        )
    {
        look = Vector3.Normalize(look);

        Vector3 reference = MathF.Abs(look.Y) > 0.999f ? Vector3.UnitZ : Vector3.UnitY;

        Vector3 rightDir = Vector3.Normalize(Vector3.Cross(reference, look));
        Vector3 upDir = Vector3.Normalize(Vector3.Cross(look, rightDir));

        Vector3 right = rightDir * 0.5f * size;
        Vector3 up = upDir * 0.5f * size;

        Vector3[] pos = { -right - up, -right + up, right + up, -right - up, right + up, right - up };

        Vector3 center = offset;

        if (!skipRecenter)
            center += look * 0.5f;

        // tangent/handedness use the unscaled directions
        Vector3 tangent = rightDir;
        float handedness = Vector3.Dot(Vector3.Cross(look, tangent), upDir) < 0 ? -1 : 1;
        Vector4 tangentData = new(tangent, handedness);

        for (int i = 0; i < 6; i++)
        {
            Vector3 normal = upNormal ? Vector3.UnitY : look;

            // pos[i].Y is now in [-0.5*size, 0.5*size], so normalize it for the weight lerp
            float weight = heightPaint ? MathHelper.Lerp(minWeight, maxWeight, pos[i].Y / size + 0.5f) : 0.0f;

            AddVertex(v, pos[i] + center, normal, Game._atlas.Map(texture, QuadUVs[i], look), tangentData, weight);
        }
    }

    public static void AddQuad(List<float> v, Vector3 look, Vector3 offset, int texture, bool skipRecenter = false, bool heightPaint = false, bool upNormal = false, float minWeight = 0.0f, float maxWeight = 0.0f, float size = 1.0f)
    {
        AddQuad(v, look, offset, TextureNames.Blocks[texture].Name, skipRecenter, heightPaint, upNormal, minWeight, maxWeight, size);
    }

    public static int GetTotalTriangles(Mesh[] meshes)
    {
        return meshes.Sum(m => m.TriangleCount);
    }
}