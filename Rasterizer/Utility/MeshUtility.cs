using OpenTK.Mathematics;

public static class MeshUtility
{
    public const int FloatsPerVertex = 17;

    // pos(3) normal(3) uv(2) tangent(4) weight(1) tileRect(4)
    // tileRect = (atlasMinU, atlasMinV, cellWidth, cellHeight). Zero width = no tiling (foliage).
    public static void AddVertex(List<float> v, Vector3 p, Vector3 normal, Vector2 uv,
                                 Vector4 tangent, float weight, Vector4 tileRect = default)
    {
        v.AddRange(new[]
        {
            p.X, p.Y, p.Z,
            normal.X, normal.Y, normal.Z,
            uv.X, uv.Y,
            tangent.X, tangent.Y, tangent.Z, tangent.W,
            weight,
            tileRect.X, tileRect.Y, tileRect.Z, tileRect.W
        });
    }

    public static void AddQuad(List<float> v, Vector3 look, Vector3 offset, string texture, TextureAtlas _atlas, bool skipRecenter = false, TextureAtlas _normalAtlas = null!, bool heightPaint = false, bool upNormal = false)
    {
        look = Vector3.Normalize(look);

        Vector3 reference = MathF.Abs(look.Y) > 0.999f ? Vector3.UnitZ : Vector3.UnitY;

        Vector3 right = Vector3.Normalize(Vector3.Cross(reference, look)) * 0.5f;
        Vector3 up = Vector3.Normalize(Vector3.Cross(look, right)) * 0.5f;

        Vector3[] pos = { -right - up, -right + up, right + up, -right - up, right + up, right - up };
        Vector2[] uv = { new(0, 0), new(0, 1), new(1, 1), new(0, 0), new(1, 1), new(1, 0) };

        Vector3 center = offset;

        if (!skipRecenter)
            center += look * 0.5f;

        Vector3 tangent = Vector3.Normalize(right);
        float handedness = Vector3.Dot(Vector3.Cross(look, tangent), up) < 0 ? -1 : 1;

        Vector4 tangentData = new(tangent, handedness);

        float weightMod = heightPaint ? 1.0f : 0.0f;

        for (int i = 0; i < 6; i++)
        {
            Vector3 normal = upNormal ? Vector3.UnitY : look;
            AddVertex(v, pos[i] + center, normal, _atlas.Map(texture, uv[i], look), tangentData, (pos[i].Y + 0.5f) * weightMod);
        }
    }

    public static void AddQuad(List<float> v, Vector3 look, Vector3 offset, int texture, TextureAtlas _atlas, bool skipRecenter = false, TextureAtlas _normalAtlas = null!, bool heightPaint = false, bool upNormal = false)
    {
        AddQuad(v, look, offset, TextureNames.IdToName[texture], _atlas, skipRecenter, _normalAtlas, heightPaint, upNormal);
    }

    public static int GetTotalTriangles(Mesh[] meshes)
    {
        return meshes.Sum(m => m.TriangleCount);
    }
}