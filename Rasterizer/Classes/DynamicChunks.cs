using OpenTK.Mathematics;
using System.Collections.Concurrent;

public static class DynamicChunks
{
    public static void HandleChunks()
    {


        Vector3i posChunk = new Vector3i(
            (int)MathF.Floor(Game._camera.Position.X / Chunk.Size),
            (int)MathF.Floor(Game._camera.Position.Y / Chunk.Size),
            (int)MathF.Floor(Game._camera.Position.Z / Chunk.Size)
        );

        // Queue surrounding chunks
        for (int x = posChunk.X - 3; x <= posChunk.X + 3; x++)
        {
            for (int z = posChunk.Z - 3; z <= posChunk.Z + 3; z++)
            {
                for (int y = 0; y < 3; y++)
                {
                    Vector3i targetPos = new Vector3i(x, y, z);

                    if (Game._chunks.ContainsKey(targetPos))
                        continue;

                    Game._chunkGenerateQueue[targetPos] = true;
                }
            }
        }

        // Select chunks to generate
        Vector3i[] chunksToGenerate = Game._chunkGenerateQueue.Keys
            .Where(pos => !Game._chunks.ContainsKey(pos))
            .Take(EngineSettings.MAX_CHUNKS_PER_INTERVAL)
            .ToArray();

        ConcurrentBag<Chunk> generatedChunks = new();

        // 1st pass: Generate chunk data
        Parallel.ForEach(chunksToGenerate, pos =>
        {
            Chunk c = new Chunk(pos * Chunk.Size);
            c.Generate();

            generatedChunks.Add(c);
        });

        List<Chunk> newChunks = generatedChunks.ToList();

        // Add generated chunks to dictionary (main thread)
        foreach (var c in newChunks)
        {
            Game._chunks[c.Position / Chunk.Size] = c;
            Game._chunkGenerateQueue.Remove(c.Position / Chunk.Size);
        }

        // 2nd pass: Apply overflow and collect affected chunks
        HashSet<Chunk> chunksToMesh = new(newChunks);

        foreach (var c in newChunks)
        {
            c.ApplyOverflow(Game._chunks);

            int x = c.Position.X / Chunk.Size;
            int y = c.Position.Y / Chunk.Size;
            int z = c.Position.Z / Chunk.Size;

            Vector3i[] neighborPositions =
            {
                    new Vector3i(x + 1, y, z),
                    new Vector3i(x - 1, y, z),
                    new Vector3i(x, y + 1, z),
                    new Vector3i(x, y - 1, z),
                    new Vector3i(x, y, z + 1),
                    new Vector3i(x, y, z - 1)
                };

            foreach (var pos in neighborPositions)
            {
                if (!Game._chunks.TryGetValue(pos, out Chunk? neighbor))
                    continue;

                neighbor.ApplyOverflow(Game._chunks);
                neighbor.wasRecalculated = true;

                chunksToMesh.Add(neighbor);
            }
        }

        // 3rd pass: Dispose old meshes (main thread)
        foreach (var c in chunksToMesh)
        {
            if (c.mesh != null)
            {
                Game._sceneObjects.Remove(c.mesh);
                c.mesh.Dispose();
                c.mesh = null;
            }

            if (c.foliageMesh != null)
            {
                Game._sceneObjects.Remove(c.foliageMesh);
                c.foliageMesh.Dispose();
                c.foliageMesh = null;
            }
        }

        // 4th pass: Create meshes
        Parallel.ForEach(chunksToMesh, c =>
        {
            c.CreateMesh(Game._chunks);
        });

        // 5th pass: Apply meshes (main thread)
        foreach (var c in chunksToMesh)
        {
            c.ApplyMesh();

            if (c.mesh != null)
                Game._sceneObjects.Add(c.mesh);

            if (c.foliageMesh != null)
                Game._sceneObjects.Add(c.foliageMesh);
        }
    }
}