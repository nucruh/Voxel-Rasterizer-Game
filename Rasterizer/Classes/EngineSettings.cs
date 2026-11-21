public static class EngineSettings
{
    // == Update Rates
    public const float SHADOW_MAP_RATE = 20f;
    public const float INFO_TIMER_RATE = 0.5f;
    // Chunk uploads are OpenGL/main-thread work and can be serviced frequently.
    public const float CHUNK_UPLOAD_INTERVAL = 0.01f;
    // Generation is CPU/background work and can be scheduled less frequently.
    public const float CHUNK_GENERATION_INTERVAL = 0.1f;

    // == Chunk Generation
    public static int MAX_CHUNKS_PER_INTERVAL { get; private set; } = 4;
    public const float WORLD_HEIGHT = 96f;

    public const double TARGET_CHUNK_BUILD_TIME_MS = 25.0;
    // A column currently contains three vertical chunks, so never select a
    // budget that cannot contain at least one complete column.
    public const int MIN_CHUNKS_PER_INTERVAL = 3;
    public const int MAX_ALLOWED_CHUNKS_PER_INTERVAL = 150;

    private static double _averageChunkBuildTimeMs;

    public static void UpdateChunkBuildBudget(double elapsedMilliseconds, int generatedChunkCount)
    {
        if (generatedChunkCount <= 0 || elapsedMilliseconds <= 0)
            return;

        double sampleTimePerChunk = elapsedMilliseconds / generatedChunkCount;

        // Exponentially weighted average: recent batches influence the budget
        // without making it jump sharply because of one unusually slow batch.
        _averageChunkBuildTimeMs = _averageChunkBuildTimeMs == 0
            ? sampleTimePerChunk
            : _averageChunkBuildTimeMs * 0.75 + sampleTimePerChunk * 0.25;

        int nextBudget = (int)Math.Round(
            TARGET_CHUNK_BUILD_TIME_MS / _averageChunkBuildTimeMs) ;

        MAX_CHUNKS_PER_INTERVAL = Math.Clamp(
            nextBudget,
            MIN_CHUNKS_PER_INTERVAL,
            MAX_ALLOWED_CHUNKS_PER_INTERVAL);
    }
}