using System.Diagnostics;

public sealed class VramMonitor
{
    private readonly List<PerformanceCounter> _dedicated = [];
    private readonly string _pidPrefix = $"pid_{Environment.ProcessId}_";
    private double _nextPoll;
    private long _cachedBytes;

    // Call once per frame; pass total elapsed seconds. Polls twice per second.
    public long GetDedicatedBytes(double timeSeconds)
    {
        if (!OperatingSystem.IsWindows()) return -1;
        if (timeSeconds < _nextPoll) return _cachedBytes;
        _nextPoll = timeSeconds + 0.5;

        try
        {
            // Instances only appear once the GPU is in use, so enumerate lazily.
            if (_dedicated.Count == 0)
            {
                var cat = new PerformanceCounterCategory("GPU Process Memory");
                foreach (string inst in cat.GetInstanceNames())
                    if (inst.StartsWith(_pidPrefix))
                        _dedicated.Add(new PerformanceCounter(
                            "GPU Process Memory", "Dedicated Usage", inst, true));
            }

            long sum = 0;
            foreach (var c in _dedicated)
                sum += (long)c.NextValue();

            _cachedBytes = sum;
        }
        catch
        {
            _dedicated.Clear(); // instance vanished, re-enumerate next poll
        }

        return _cachedBytes;
    }
}

public static class PerfMonitors
{
    public static long GetRamUsage()
    {
        using Process process = Process.GetCurrentProcess();
        return process.WorkingSet64 / (1024 * 1024);
    }
}