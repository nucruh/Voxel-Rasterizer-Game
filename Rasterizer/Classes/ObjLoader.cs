using System.Globalization;
using OpenTK.Mathematics;

public static class ObjLoader
{

    private const float BaseScale = 0.5f;

    public static float[] Load(string path, string texture, Vector3 scale, Vector3 origin)
    {
        if (!TextureNames.Names.Contains(texture))
            throw new Exception($"Model texture '{texture}' is not in the diffuse atlas (needed by {path}).");

        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var result = new List<float>();

        Vector3 s = scale * BaseScale;

        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            string[] t = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            switch (t[0])
            {
                case "v":
                    positions.Add(new Vector3(F(t[1]), F(t[2]), F(t[3])) * s - origin);
                    break;
                case "vn":
                    Vector3 nn = new Vector3(F(t[1]), F(t[2]), F(t[3])) / s;
                    normals.Add(nn.LengthSquared > 1e-12f ? Vector3.Normalize(nn) : Vector3.UnitY);
                    break;
                case "vt":
                    uvs.Add(new Vector2(F(t[1]), t.Length > 2 ? F(t[2]) : 0f));
                    break;
                case "f":
                    AddFace(result, t, texture, positions, normals, uvs, mirrored: s.X * s.Y * s.Z < 0);
                    break;
            }
        }

        return result.ToArray();
    }

    private static void AddFace(List<float> v, string[] t, string texture,
        List<Vector3> positions, List<Vector3> normals, List<Vector2> uvs, bool mirrored)
    {
        int count = t.Length - 1;
        if (count < 3) return;

        var p = new Vector3[count];
        var n = new Vector3?[count];
        var uv = new Vector2[count];   // raw UVs, NOT clamped

        for (int i = 0; i < count; i++)
        {
            string[] parts = t[i + 1].Split('/');

            p[i] = positions[Resolve(parts[0], positions.Count)];

            if (parts.Length > 1 && parts[1].Length > 0)
                uv[i] = uvs[Resolve(parts[1], uvs.Count)];

            if (parts.Length > 2 && parts[2].Length > 0)
                n[i] = normals[Resolve(parts[2], normals.Count)];
        }

        for (int i = 1; i < count - 1; i++)
        {
            int[] idx = mirrored ? new[] { 0, i, i + 1 } : new[] { 0, i + 1, i };
            int a = idx[0], b = idx[1], c = idx[2];

            Vector3 faceNormal = Vector3.Cross(p[b] - p[a], p[c] - p[a]);
            faceNormal = faceNormal.LengthSquared > 1e-12f
                ? Vector3.Normalize(faceNormal)
                : Vector3.UnitY;

            Vector4 tangent = ComputeTangent(p[a], p[b], p[c], uv[a], uv[b], uv[c], faceNormal);

            // shift the whole triangle into the 0..1 tile (keeps UV differences intact)
            const float eps = 1e-8f;
            float minU = MathF.Min(uv[a].X, MathF.Min(uv[b].X, uv[c].X));
            float minV = MathF.Min(uv[a].Y, MathF.Min(uv[b].Y, uv[c].Y));
            Vector2 shift = new(MathF.Floor(minU + eps), MathF.Floor(minV + eps));

            foreach (int k in idx)
            {
                Vector2 local = uv[k] - shift;
                local = new Vector2(MathHelper.Clamp(local.X, 0f, 1f),
                                    MathHelper.Clamp(local.Y, 0f, 1f));

                Vector2 atlasUv = Game._atlas.Map(texture, local);
                MeshUtility.AddVertex(v, p[k], n[k] ?? faceNormal, atlasUv, tangent, 0.0f);
            }
        }
    }

    private static Vector4 ComputeTangent(Vector3 p0, Vector3 p1, Vector3 p2,
                                          Vector2 uv0, Vector2 uv1, Vector2 uv2,
                                          Vector3 normal)
    {
        Vector3 e1 = p1 - p0, e2 = p2 - p0;
        Vector2 d1 = uv1 - uv0, d2 = uv2 - uv0;

        float det = d1.X * d2.Y - d2.X * d1.Y;

        if (MathF.Abs(det) < 1e-12f)
        {
            Vector3 fallback = MathF.Abs(normal.Y) > 0.999f ? Vector3.UnitX : Vector3.Cross(Vector3.UnitY, normal);
            return new Vector4(Vector3.Normalize(fallback), 1f);
        }

        float r = 1f / det;
        Vector3 tangent = (e1 * d2.Y - e2 * d1.Y) * r;
        Vector3 bitangent = (e2 * d1.X - e1 * d2.X) * r;

        tangent -= normal * Vector3.Dot(normal, tangent);
        tangent = tangent.LengthSquared > 1e-12f ? Vector3.Normalize(tangent) : Vector3.UnitX;

        float handedness = Vector3.Dot(Vector3.Cross(normal, tangent), bitangent) < 0 ? -1f : 1f;
        return new Vector4(tangent, handedness);
    }

    private static int Resolve(string s, int count)
    {
        int i = int.Parse(s, CultureInfo.InvariantCulture);
        return i > 0 ? i - 1 : count + i;
    }

    private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
}