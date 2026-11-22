using OpenTK.Mathematics;
using System.Collections.Concurrent;
using System.Diagnostics;

public static class DynamicChunks
{
    private const int LOAD_RADIUS = 6;
    private const int UNLOAD_RADIUS = LOAD_RADIUS + 2;

    private sealed class ChunkBuildResult
    {
        public required List<Chunk> ChunksToMesh { get; init; }
        public required double ElapsedMilliseconds { get; init; }
        public required int GeneratedChunkCount { get; init; }
    }

    private static Task? _generationTask;
    private static readonly ConcurrentQueue<ChunkBuildResult> _completedBuilds = new();

    public static void HandleChunks(bool uploadCompleted, bool scheduleGeneration)
    {
        // Applying meshes must happen on the OpenGL thread. The expensive generation
        // and CPU mesh building are performed by GenerateChunksAsync below.
        if (_generationTask is { IsCompleted: true })
        {
            // Keep the completed task held until its result is uploaded. This prevents
            // another generation batch from starting while a result is waiting for
            // the OpenGL thread.
            if (!uploadCompleted)
                return;
            _generationTask = null;
        }

        if (uploadCompleted)
        {
            while (_completedBuilds.TryDequeue(out ChunkBuildResult? result))
            {
                ApplyCompletedBuild(result);
            }
        }

        if (!scheduleGeneration)
            return;

        Vector3i posChunk = new Vector3i(
            (int)MathF.Floor(Game._camera.Position.X / Chunk.Size),
            (int)MathF.Floor(Game._camera.Position.Y / Chunk.Size),
            (int)MathF.Floor(Game._camera.Position.Z / Chunk.Size)
        );

        // A completed worker can still be handing results back to this thread.
        // Cleanup is therefore performed only after completed results have been
        // applied and before starting another worker.
        if (_generationTask == null)
            UnloadDistantChunks(posChunk);

        // Queue surrounding chunks
        int loadRadiusSquared = LOAD_RADIUS * LOAD_RADIUS;
        for (int x = posChunk.X - LOAD_RADIUS; x <= posChunk.X + LOAD_RADIUS; x++)
        {
            for (int z = posChunk.Z - LOAD_RADIUS; z <= posChunk.Z + LOAD_RADIUS; z++)
            {
                int dx = x - posChunk.X;
                int dz = z - posChunk.Z;

                // Keep loading circular so corner chunks cannot be immediately
                // removed by the circular unload radius below.
                if (dx * dx + dz * dz > loadRadiusSquared)
                    continue;

                for (int y = 0; y < 3; y++)
                {
                    Vector3i targetPos = new Vector3i(x, y, z);

                    if (Game._chunks.ContainsKey(targetPos))
                        continue;

                    Game._chunkGenerateQueue.TryAdd(targetPos, 0);
                }
            }
        }

        // Keep one build in flight. This prevents two workers from rebuilding the
        // same neighbors and keeps the handoff to the render thread deterministic.
        if (_generationTask != null)
            return;

        // Select complete vertical columns. Keeping the three Y chunks together
        // prevents a column from being temporarily exposed with missing layers.
        List<Vector3i> chunksToGenerate = new();
        var columns = Game._chunkGenerateQueue.Keys
            .Where(pos => !Game._chunks.ContainsKey(pos))
            .GroupBy(pos => (pos.X, pos.Z))
            .OrderBy(column =>
            {
                int dx = column.Key.X - posChunk.X;
                int dz = column.Key.Z - posChunk.Z;
                return dx * dx + dz * dz;
            });

        foreach (var column in columns)
        {
            List<Vector3i> columnChunks = column
                .OrderBy(pos => pos.Y)
                .ToList();

            if (columnChunks.Count == 0)
                continue;

            // Do not split columns across batches. The first column is allowed
            // to exceed the budget so a valid batch is always possible.
            if (chunksToGenerate.Count > 0 &&
                chunksToGenerate.Count + columnChunks.Count > EngineSettings.MAX_CHUNKS_PER_INTERVAL)
                break;

            chunksToGenerate.AddRange(columnChunks);
        }

        foreach (Vector3i pos in chunksToGenerate)
            Game._chunkGenerateQueue.TryRemove(pos, out _);

        if (chunksToGenerate.Count != 0)
        {
            string batchPositions = string.Join(", ", chunksToGenerate.Select(position => position.ToString()));
            //Console.WriteLine($"[Chunks] Starting generation: count={chunksToGenerate.Count}, queuedRemaining={Game._chunkGenerateQueue.Count}, center={posChunk}, positions=[{batchPositions}], thread={Environment.CurrentManagedThreadId}.");
            _generationTask = GenerateChunksAsync(chunksToGenerate.ToArray());
        }
    }

    private static async Task GenerateChunksAsync(Vector3i[] chunksToGenerate)
    {
        try
        {
            //Console.WriteLine($"[Chunks] Generation scheduled: count={chunksToGenerate.Length}, requestThread={Environment.CurrentManagedThreadId}.");
            ChunkBuildResult result = await Task.Run(() => BuildChunks(chunksToGenerate));
            //Console.WriteLine($"[Chunks] Worker finished: generated={result.GeneratedChunkCount}, meshes={result.ChunksToMesh.Count}, buildTime={result.ElapsedMilliseconds:F1}ms, thread={Environment.CurrentManagedThreadId}.");
            _completedBuilds.Enqueue(result);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[Chunks] Generation failed: {exception}");
            foreach (Vector3i position in chunksToGenerate)
            {
                if (!Game._chunks.ContainsKey(position))
                    Game._chunkGenerateQueue.TryAdd(position, 0);
            }

            Console.WriteLine($"Chunk generation failed: {exception}");
        }
    }

    private static ChunkBuildResult BuildChunks(Vector3i[] chunksToGenerate)
    {
        //Console.WriteLine($"[Chunks] BuildChunks executing: count={chunksToGenerate.Length}, workerThread={Environment.CurrentManagedThreadId}.");
        Stopwatch stopwatch = Stopwatch.StartNew();
        ConcurrentBag<Chunk> generatedChunks = new();

        Parallel.ForEach(chunksToGenerate, pos =>
        {
            Chunk chunk = new Chunk(pos * Chunk.Size);
            chunk.Generate();
            generatedChunks.Add(chunk);
        });

        List<Chunk> newChunks = generatedChunks.ToList();
        foreach (Chunk chunk in newChunks)
            Game._chunks[chunk.Position / Chunk.Size] = chunk;

        HashSet<Chunk> chunksToMesh = new(newChunks);
        foreach (Chunk chunk in newChunks)
        {
            chunk.ApplyOverflow(Game._chunks);

            int x = chunk.Position.X / Chunk.Size;
            int y = chunk.Position.Y / Chunk.Size;
            int z = chunk.Position.Z / Chunk.Size;
            Vector3i[] neighborPositions =
            {
                new(x + 1, y, z), new(x - 1, y, z),
                new(x, y + 1, z), new(x, y - 1, z),
                new(x, y, z + 1), new(x, y, z - 1)
            };

            foreach (Vector3i position in neighborPositions)
            {
                if (!Game._chunks.TryGetValue(position, out Chunk? neighbor))
                    continue;

                neighbor.ApplyOverflow(Game._chunks);
                neighbor.wasRecalculated = true;
                chunksToMesh.Add(neighbor);
            }
        }

        // CreateMesh only builds CPU-side vertex data. OpenGL objects are created
        // later by ApplyMesh on the main thread.
        Parallel.ForEach(chunksToMesh, chunk => chunk.CreateMesh(Game._chunks));

        stopwatch.Stop();
        return new ChunkBuildResult
        {
            ChunksToMesh = chunksToMesh.ToList(),
            ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
            GeneratedChunkCount = newChunks.Count
        };
    }

    private static void ApplyCompletedBuild(ChunkBuildResult result)
    {
        EngineSettings.UpdateChunkBuildBudget(
            result.ElapsedMilliseconds,
            result.GeneratedChunkCount);

        foreach (Chunk chunk in result.ChunksToMesh)
            DisposeChunkMeshes(chunk);

        foreach (Chunk chunk in result.ChunksToMesh)
        {
            chunk.ApplyMesh();
            if (chunk.mesh != null)
                Game._sceneObjects.Add(chunk.mesh);
            if (chunk.foliageMesh != null)
                Game._sceneObjects.Add(chunk.foliageMesh);
        }
    }

    private static void UnloadDistantChunks(Vector3i center)
    {
        int unloadRadiusSquared = UNLOAD_RADIUS * UNLOAD_RADIUS;

        foreach (KeyValuePair<Vector3i, Chunk> entry in Game._chunks.ToArray())
        {
            Vector3i position = entry.Key;
            int dx = position.X - center.X;
            int dz = position.Z - center.Z;

            // Chunk streaming is horizontal; retain all vertical layers in a
            // column while that column is within the unload radius.
            if (dx * dx + dz * dz <= unloadRadiusSquared)
                continue;

            if (Game._chunks.TryRemove(position, out Chunk? chunk))
                DisposeChunkMeshes(chunk);
        }

        foreach (Vector3i position in Game._chunkGenerateQueue.Keys)
        {
            int dx = position.X - center.X;
            int dz = position.Z - center.Z;

            if (dx * dx + dz * dz > unloadRadiusSquared)
                Game._chunkGenerateQueue.TryRemove(position, out _);
        }
    }

    private static void DisposeChunkMeshes(Chunk chunk)
    {
        if (chunk.mesh != null)
        {
            Game._sceneObjects.Remove(chunk.mesh);
            chunk.mesh.Dispose();
            chunk.mesh = null;
        }

        if (chunk.foliageMesh != null)
        {
            Game._sceneObjects.Remove(chunk.foliageMesh);
            chunk.foliageMesh.Dispose();
            chunk.foliageMesh = null;
        }
    }
}