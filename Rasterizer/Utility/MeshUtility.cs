using OpenTK.Mathematics;


public static class MeshUtility
{
    static readonly float[] CubeVertices =
    {
        -1,-1,-1,  1,-1,-1,  1, 1,-1,  -1,-1,-1,  1, 1,-1,  -1, 1,-1,
        -1,-1, 1,  1, 1, 1,  1,-1, 1,  -1,-1, 1, -1, 1, 1,   1, 1, 1,
        -1,-1,-1, -1, 1,-1, -1, 1, 1,  -1,-1,-1, -1, 1, 1,  -1,-1, 1,
         1,-1,-1,  1,-1, 1,  1, 1, 1,   1,-1,-1,  1, 1, 1,   1, 1,-1,
        -1, 1,-1,  1, 1,-1,  1, 1, 1,  -1, 1,-1,  1, 1, 1,  -1, 1, 1,
        -1,-1,-1,  1,-1, 1,  1,-1,-1,  -1,-1,-1, -1,-1, 1,   1,-1, 1,
    };

    public static void AddVertex(List<float> v, Vector3 p, Vector2 uv)
    {
        v.AddRange(new[] { p.X, p.Y, p.Z, 1f, 1f, 1f, uv.X, uv.Y });
    }

    // Upright, unmirrored UV (0..1) for a cube face seen from outside.
    public static Vector2 FaceUV(int face, Vector3 p) => face switch
    {
        0 => new Vector2((1 - p.X) / 2, (p.Y + 1) / 2), // back
        1 => new Vector2((p.X + 1) / 2, (p.Y + 1) / 2), // front
        2 => new Vector2((p.Z + 1) / 2, (p.Y + 1) / 2), // left
        3 => new Vector2((1 - p.Z) / 2, (p.Y + 1) / 2), // right
        4 => new Vector2((p.X + 1) / 2, (1 - p.Z) / 2), // top
        _ => new Vector2((p.X + 1) / 2, (p.Z + 1) / 2), // bottom
    };

    public static void AddCube(List<float> v, Vector3 offset, int[] faceTextures, TextureAtlas _atlas)
    {
        for (int i = 0; i < 36; i++)
        {
            int face = i / 6;
            Vector3 p = new(CubeVertices[i * 3], CubeVertices[i * 3 + 1], CubeVertices[i * 3 + 2]);
            AddVertex(v, p + offset, _atlas.Map(faceTextures[face], FaceUV(face, p)));
        }
    }

    public static void AddQuad(List<float> v, Vector3 look, Vector3 offset, int texture, TextureAtlas _atlas)
    {
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, look));
        Vector3 up = Vector3.Normalize(Vector3.Cross(look, right));

        Vector3[] pos = { -right - up, -right + up, right + up, -right - up, right + up, right - up };
        Vector2[] uv = { new(0, 0), new(0, 1), new(1, 1), new(0, 0), new(1, 1), new(1, 0) };

        for (int i = 0; i < 6; i++)
            AddVertex(v, pos[i] + offset, _atlas.Map(texture, uv[i]));
    }
}