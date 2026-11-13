using OpenTK.Mathematics;
using System.Diagnostics;

public static class WorldPregeneration
{
    public static void Pregenerate()
    {
        Stopwatch sw = Stopwatch.StartNew();

        // firs paralell pass; gen chunks
        Parallel.For(0, Game.WorldChunks, x =>
        {
            for (int z = 0; z < Game.WorldChunks; z++)
                for (int y = 0; y < 3; y++)
                {
                    Chunk c = new Chunk(new Vector3i(x * Chunk.Size, y * Chunk.Size, z * Chunk.Size));
                    c.Generate();
                    Game._chunks[new Vector3i(x, y, z)] = c;
                }
        });

        // second parallel pass; overflow
        Parallel.ForEach(Game._chunks.Values, c =>
        {
            // overflow blocks generated in first pass
            c.ApplyOverflow(Game._chunks);
        });

        // third parallel pass; mesh gen
        Stopwatch swmesh = Stopwatch.StartNew();
        Parallel.ForEach(Game._chunks.Values, c =>
        {
            c.CreateMesh(Game._chunks);
        });


        // fourth pass; upload meshes to gpu, add to scene
        foreach (var c in Game._chunks.Values)
        {
            c.ApplyMesh();
            Game._sceneObjects.Add(c.mesh!);

            if (c.foliageMesh != null)
                Game._sceneObjects.Add(c.foliageMesh);
        }
        swmesh.Stop();

        Game.totalGenerationTime = sw.Elapsed.TotalMilliseconds;
        sw.Stop();

        Console.WriteLine(swmesh.Elapsed.TotalMilliseconds);
    }
}