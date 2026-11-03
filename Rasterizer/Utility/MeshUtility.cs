using OpenTK.Mathematics;


public static class MeshUtility
{
    static readonly float[] CubeVertices =
    {
        -0.5f,-0.5f,-0.5f,  0.5f,-0.5f,-0.5f,  0.5f, 0.5f,-0.5f,  -0.5f,-0.5f,-0.5f,  0.5f, 0.5f,-0.5f,  -0.5f, 0.5f,-0.5f,
        -0.5f,-0.5f, 0.5f,  0.5f, 0.5f, 0.5f,  0.5f,-0.5f, 0.5f,  -0.5f,-0.5f, 0.5f, -0.5f, 0.5f, 0.5f,   0.5f, 0.5f, 0.5f,
        -0.5f,-0.5f,-0.5f, -0.5f, 0.5f,-0.5f, -0.5f, 0.5f, 0.5f,  -0.5f,-0.5f,-0.5f, -0.5f, 0.5f, 0.5f,  -0.5f,-0.5f, 0.5f,
         0.5f,-0.5f,-0.5f,  0.5f,-0.5f, 0.5f,  0.5f, 0.5f, 0.5f,   0.5f,-0.5f,-0.5f,  0.5f, 0.5f, 0.5f,   0.5f, 0.5f,-0.5f,
        -0.5f, 0.5f,-0.5f,  0.5f, 0.5f,-0.5f,  0.5f, 0.5f, 0.5f,  -0.5f, 0.5f,-0.5f,  0.5f, 0.5f, 0.5f,  -0.5f, 0.5f, 0.5f,
        -0.5f,-0.5f,-0.5f,  0.5f,-0.5f, 0.5f,  0.5f,-0.5f,-0.5f,  -0.5f,-0.5f,-0.5f, -0.5f,-0.5f, 0.5f,   0.5f,-0.5f, 0.5f,
    };

    public static void AddVertex(List<float> v, Vector3 p, Vector3 normal, Vector2 uv)
    {
        v.AddRange(new[] { p.X, p.Y, p.Z, normal.X, normal.Y, normal.Z, uv.X, uv.Y });
    }

    // Upright, unmirrored UV (0..1) for a cube face seen from outside.
    public static Vector2 FaceUV(int face, Vector3 p) => face switch
    {
        0 => new Vector2(0.5f - p.X, p.Y + 0.5f), // back
        1 => new Vector2(p.X + 0.5f, p.Y + 0.5f), // front
        2 => new Vector2(p.Z + 0.5f, p.Y + 0.5f), // left
        3 => new Vector2(0.5f - p.Z, p.Y + 0.5f), // right
        4 => new Vector2(p.X + 0.5f, 0.5f - p.Z), // top
        _ => new Vector2(p.X + 0.5f, p.Z + 0.5f), // bottom
    };

    static readonly Vector3[] CubeNormals =
    {
        -Vector3.UnitZ, // back
         Vector3.UnitZ, // front
        -Vector3.UnitX, // left
         Vector3.UnitX, // right
         Vector3.UnitY, // top
        -Vector3.UnitY  // bottom
    };

    public static void AddCube(List<float> v, Vector3 offset, string[] faceTextures, TextureAtlas _atlas)
    {
        for (int i = 0; i < 36; i++)
        {
            int face = i / 6;
            Vector3 p = new(CubeVertices[i * 3], CubeVertices[i * 3 + 1], CubeVertices[i * 3 + 2]);
            //AddVertex(v, p + offset, CubeNormals[face], _atlas.Map(_atlas.Id(faceTextures[face]), FaceUV(face, p)));
        }
    }

    public static void AddQuad(List<float> v, Vector3 look, Vector3 offset, string texture, TextureAtlas _atlas, bool skipRecenter = false)
    {
        look = Vector3.Normalize(look);

        Vector3 reference = MathF.Abs(look.Y) > 0.999f ? Vector3.UnitZ : Vector3.UnitY;

        Vector3 right = Vector3.Normalize(Vector3.Cross(reference, look)) * 0.5f;
        Vector3 up = Vector3.Normalize(Vector3.Cross(look, right)) * 0.5f;

        Vector3[] pos = { -right - up, -right + up, right + up, -right - up, right + up, right - up };
        Vector2[] uv = { new(0, 0), new(0, 1), new(1, 1), new(0, 0), new(1, 1), new(1, 0) };

        Vector3 center = offset;

        if(!skipRecenter)
        {
            center += look * 0.5f;
        }
        

        //int id = _atlas.Id(texture);
        for (int i = 0; i < 6; i++)
            AddVertex(v, pos[i] + center, look, _atlas.Map(texture, uv[i], look));
    }

    public static int GetTotalTriangles(Mesh[] meshes)
    {
        return meshes.Sum(m => m.TriangleCount);
    }
}