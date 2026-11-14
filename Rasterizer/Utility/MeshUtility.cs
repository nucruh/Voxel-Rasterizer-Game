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

    public static void AddQuadAnchored(
    List<float> v,
    Vector3 look,
    Vector3 bottom,
    string texture,
    float width = 1.0f,
    float height = 1.0f,
    bool heightPaint = false,
    bool upNormal = false,
    float minWeight = 0.0f,
    float maxWeight = 0.0f)
    {
        look = Vector3.Normalize(look);

        Vector3 reference = MathF.Abs(look.Y) > 0.999f ? Vector3.UnitZ : Vector3.UnitY;

        Vector3 rightDir = Vector3.Normalize(Vector3.Cross(reference, look));
        Vector3 upDir = Vector3.Normalize(Vector3.Cross(look, rightDir));

        Vector3 right = rightDir * width * 0.5f;
        Vector3 up = upDir * height;

        Vector3[] pos = {-right, -right + up, right + up, -right, right + up, right};

        Vector4 tangentData = new(rightDir,
            Vector3.Dot(Vector3.Cross(look, rightDir), upDir) < 0 ? -1 : 1);

        for (int i = 0; i < 6; i++)
        {
            Vector3 normal = upNormal ? Vector3.UnitY : look;

            float weight = heightPaint ? MathHelper.Lerp(minWeight, maxWeight, Vector3.Dot(pos[i], upDir) / height) : 0.0f;

            AddVertex(v, pos[i] + bottom, normal, Game._atlas.Map(texture, QuadUVs[i], look), tangentData, weight);
        }
    }

    public static void AddQuad(List<float> v, Vector3 look, Vector3 offset, ushort texture, bool skipRecenter = false, bool heightPaint = false, bool upNormal = false, float minWeight = 0.0f, float maxWeight = 0.0f, float size = 1.0f)
    {
        AddQuad(v, look, offset, BlockRegistry.Blocks[texture].Textures[0], skipRecenter, heightPaint, upNormal, minWeight, maxWeight, size);
    }

    public static void AddModel(
    List<float> v,
    string modelName,
    Vector3 position,
    Quaternion? rotation = null,
    Vector3? scale = null,
    bool heightPaint = false,
    float minWeight = 0.0f,
    float maxWeight = 0.0f)
    {
        AddModel(v, Models.Get(modelName), position, rotation, scale, heightPaint, minWeight, maxWeight);
    }

    public static void AddModel(
        List<float> v,
        Model model,
        Vector3 position,
        Quaternion? rotation = null,
        Vector3? scale = null,
        bool heightPaint = false,
        float minWeight = 0.0f,
        float maxWeight = 0.0f)
    {
        Quaternion rot = rotation ?? Quaternion.Identity;
        Vector3 s = scale ?? Vector3.One;

        // a mirrored scale (odd number of negatives) flips the winding and the tangent handedness
        bool mirrored = s.X * s.Y * s.Z < 0;

        float[] src = model.Vertices;
        int vertexCount = src.Length / FloatsPerVertex;

        v.EnsureCapacity(v.Count + src.Length);

        // reads one vertex at index i
        Vertex Read(int i)
        {
            int o = i * FloatsPerVertex;
            return new Vertex
            {
                Position = new Vector3(src[o], src[o + 1], src[o + 2]),
                Normal = new Vector3(src[o + 3], src[o + 4], src[o + 5]),
                UV = new Vector2(src[o + 6], src[o + 7]),
                Tangent = new Vector4(src[o + 8], src[o + 9], src[o + 10], src[o + 11]),
                Weight = src[o + 12],
                TileRect = new Vector4(src[o + 13], src[o + 14], src[o + 15], src[o + 16])
            };
        }

        void Emit(in Vertex vert)
        {
            // position: scale, rotate, translate
            Vector3 p = rot * (vert.Position * s) + position;

            // normal: inverse-transpose of the scale keeps it correct for non-uniform scale
            Vector3 n = rot * (vert.Normal / s);
            n = n.LengthSquared > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitY;

            // tangent lies in the surface, so it transforms like a position direction
            Vector3 t = rot * (vert.Tangent.Xyz * s);
            t = t.LengthSquared > 1e-12f ? Vector3.Normalize(t) : Vector3.UnitX;
            float w = mirrored ? -vert.Tangent.W : vert.Tangent.W;

            float weight = heightPaint
                ? MathHelper.Lerp(minWeight, maxWeight, MathHelper.Clamp(vert.Position.Y, 0f, 1f))
                : vert.Weight;

            AddVertex(v, p, n, vert.UV, new Vector4(t, w), weight, vert.TileRect);
        }

        for (int i = 0; i < vertexCount; i += 3)
        {
            Vertex a = Read(i), b = Read(i + 1), c = Read(i + 2);

            if (mirrored)
            {
                Emit(a); Emit(c); Emit(b);   // swap to keep the winding order
            }
            else
            {
                Emit(a); Emit(b); Emit(c);
            }
        }
    }

    // convenience: yaw in degrees, uniform scale (the common case for placed props)
    public static void AddModel(List<float> v, string modelName, Vector3 position,
                                float yawDegrees, float scale = 1.0f)
    {
        AddModel(v, modelName, position,
            Quaternion.FromAxisAngle(Vector3.UnitY, MathHelper.DegreesToRadians(yawDegrees)),
            new Vector3(scale));
    }

    public static int GetTotalTriangles(Mesh[] meshes)
    {
        return meshes.Sum(m => m.TriangleCount);
    }
}