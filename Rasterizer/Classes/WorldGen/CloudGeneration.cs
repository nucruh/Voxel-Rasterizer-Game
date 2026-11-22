using System.Diagnostics;
using OpenTK.Mathematics;
using System.Collections.Concurrent;

public class Clouds
{
    public List<float> Vertices = new();
    public Vector3 Position = new Vector3(0, 0f, 0);
    public Mesh? mesh;
    public int Seed;
    public Clouds(int seed, Vector3 position)
    {
        Seed = seed;
        Position = position;
    }

    public void Generate(Vector3 chunkPosition)
    {
        CloudGeneration.GenerateClouds(Vertices, chunkPosition, Seed);
    }

    public void ApplyMesh()
    {
        mesh?.Dispose();

        float[] array = Vertices.ToArray();
        mesh = new Mesh(array, false, Position);
        if (array.Length == 0)
        {
            mesh.Active = false;
        }

        Vertices.Clear();
        Vertices.TrimExcess();
    }
}

public class CloudGeneration
{
    const float Range = 1024f;        // half-extent of the cloud field
    const float Step = 24f;           // spacing of the noise sample grid
    const float BaseY = 300f;

    const float CoverScale = 0.006f;  // lower = bigger cloud masses
    const float Threshold = 0.0f;     // raise = fewer clouds
    const int MaxBoxesPerCell = 5;

    public static void GenerateClouds(List<float> vertices, Vector3 chunkPosition, int seed)
    {
        Random random = new(seed);
        float ox = random.Next(0, 10000);
        float oz = random.Next(0, 10000);

        int steps = (int)(Range * 2f / Step);

        for (int ix = 0; ix < steps; ix++)
            for (int iz = 0; iz < steps; iz++)
            {
                float wx = -Range + ix * Step;
                float wz = -Range + iz * Step;

                float cover = PerlinNoise.FBM(
                    (wx + ox) * CoverScale, (wz + oz) * CoverScale,
                    octaves: 4, persistence: 0.5f, lacunarity: 2.0f, seed: seed);

                if (cover <= Threshold) continue;

                // 0..1: how deep inside a cloud mass this cell is
                float strength = Math.Clamp((cover - Threshold) / 0.35f, 0f, 1f);

                // denser spots get more boxes
                int count = 1 + (int)(strength * (MaxBoxesPerCell - 1) * random.NextSingle() + random.NextSingle());

                for (int i = 0; i < count; i++)
                {
                    // jitter by more than a cell so neighbors overlap heavily
                    float jx = (random.NextSingle() - 0.5f) * Step * 2.5f;
                    float jz = (random.NextSingle() - 0.5f) * Step * 2.5f;

                    // widely varying footprints, bigger in cloud cores
                    float sx = Lerp(14f, 40f, random.NextSingle()) * Lerp(0.8f, 2.2f, strength * random.NextSingle());
                    float sz = Lerp(14f, 40f, random.NextSingle()) * Lerp(0.8f, 2.2f, strength * random.NextSingle());

                    // heights: mostly flat, occasional tall puffs in cores
                    float h = Lerp(6f, 18f, random.NextSingle());
                    if (random.NextSingle() < 0.25f * strength)
                        h += Lerp(10f, 36f, random.NextSingle());

                    // bottoms sit near a shared base with a little variation,
                    // and cores get stacked boxes on top
                    float bottom = BaseY + random.Next(0, 3) * 5f + (i > 0 ? random.NextSingle() * 14f * strength : 0f);

                    Vector3 center = new(
                        wx + jx + chunkPosition.X,
                        bottom + h * 0.5f,
                        wz + jz + chunkPosition.Z);

                    MeshUtility.AddCube(vertices, center, new Vector3(sx, h, sz));
                }
            }
    }

    static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
