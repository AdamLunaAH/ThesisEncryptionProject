using System.Collections.Concurrent;
using CrossCut.Concerns.Monitor;

namespace App.WebApiEncryption.Services;

/// <inheritdoc cref="IResourceCaptureService"/>
public sealed class ResourceCaptureService : IResourceCaptureService
{
    // Active capture sessions keyed by benchmark run ID.
    // ConcurrentDictionary so StartCapture/StopCapture (slice thread) and
    // FeedSnapshot (broadcaster background thread) never race on the map itself.
    private readonly ConcurrentDictionary<Guid, CaptureSession> _sessions = new();

    /// <inheritdoc/>
    public void StartCapture(Guid runId)
        => _sessions.TryAdd(runId, new CaptureSession());

    /// <inheritdoc/>
    public ResourceCaptureSummary? StopCapture(Guid runId)
    {
        if (!_sessions.TryRemove(runId, out var session))
            return null;

        var samples = session.Snapshot();
        if (samples.Count == 0)
            return null;

        return BuildSummary(samples);
    }

    /// <inheritdoc/>
    public void FeedSnapshot(ResourceMetricsSnapshot snapshot)
    {
        // Iterate a snapshot of the values to avoid locking on the dictionary.
        foreach (var session in _sessions.Values)
            session.Add(snapshot);
    }

    // ── Inner session ─────────────────────────────────────────────────────────

    private sealed class CaptureSession
    {
        private readonly List<ResourceMetricsSnapshot> _samples = [];
        private readonly object _lock = new();

        public void Add(ResourceMetricsSnapshot snapshot)
        {
            lock (_lock)
                _samples.Add(snapshot);
        }

        /// <summary>Returns a stable copy of collected samples.</summary>
        public IReadOnlyList<ResourceMetricsSnapshot> Snapshot()
        {
            lock (_lock)
                return _samples.ToArray();
        }
    }

    // ── Aggregate computation ─────────────────────────────────────────────────

    private static ResourceCaptureSummary BuildSummary(
        IReadOnlyList<ResourceMetricsSnapshot> samples)
    {
        int n = samples.Count;

        double duration = n >= 2
            ? (samples[n - 1].Timestamp - samples[0].Timestamp).TotalMilliseconds
            : 0.0;

        // CPU: skip the first sample.
        // The broadcaster computes CPU% as Δ(TotalProcessorTime) / Δwall from its
        // previous tick, so the first snapshot after StartCapture always covers
        // [T−interval, T] — straddling the pre-run boundary.  Dropping it means
        // every retained sample reflects only in-run CPU activity.
        var cpuSource = n >= 2 ? samples.Skip(1) : (IEnumerable<ResourceMetricsSnapshot>)samples;
        var cpuSorted = cpuSource.Select(s => s.CpuPercent).OrderBy(v => v).ToArray();
        int cpuN = cpuSorted.Length;

        // Memory: report deltas relative to the opening GC heap baseline.
        // GcHeapMb (GC.GetTotalMemory) is used instead of WorkingSet64 (MemoryUsedMb).
        // After the pre-run compacting GC, the CLR releases empty segments via VirtualFree,
        // but WorkingSet64 does not drop immediately — the OS reclaims those pages
        // asynchronously over the next several hundred milliseconds.  This causes
        // samples taken early in the run to show a steadily shrinking WorkingSet64,
        // producing large spurious negative deltas (e.g. −300 MB for a run that
        // allocates only a few hundred bytes).
        // GcHeapMb reflects the true compacted heap size immediately and only changes
        // as managed objects are allocated or collected, giving accurate in-run deltas.
        double memBaseline = samples[0].GcHeapMb;
        var memDeltas = samples.Select(s => s.GcHeapMb - memBaseline).OrderBy(v => v).ToArray();

        return new ResourceCaptureSummary
        {
            SampleCount = n,
            DurationMilliseconds = duration,
            Samples = samples,

            // CPU (first sample excluded — straddles pre-run boundary)
            CpuMax = cpuN > 0 ? cpuSorted[cpuN - 1] : 0,
            CpuAvg = cpuN > 0 ? cpuSorted.Average() : 0,
            CpuP50 = cpuN > 0 ? Percentile(cpuSorted, 0.50) : 0,
            CpuP95 = cpuN > 0 ? Percentile(cpuSorted, 0.95) : 0,
            CpuP99 = cpuN > 0 ? Percentile(cpuSorted, 0.99) : 0,

            // Memory (deltas relative to run-start baseline; negative = GC freed memory)
            MemoryMaxMb = memDeltas[memDeltas.Length - 1],
            MemoryAvgMb = memDeltas.Average(),
            MemoryP50Mb = Percentile(memDeltas, 0.50),
            MemoryP95Mb = Percentile(memDeltas, 0.95),
            MemoryP99Mb = Percentile(memDeltas, 0.99),

            // GC deltas (cumulative counters: last − first)
            GcGen0Delta = samples[n - 1].GcGen0Collections - samples[0].GcGen0Collections,
            GcGen1Delta = samples[n - 1].GcGen1Collections - samples[0].GcGen1Collections,
            GcGen2Delta = samples[n - 1].GcGen2Collections - samples[0].GcGen2Collections,

            // Thread pool
            ThreadPoolMaxWorkers = samples.Max(s => s.ThreadPoolWorkerThreads),
        };
    }

    /// <summary>
    /// Nearest-rank percentile over a pre-sorted array.
    /// Interval-independent: works identically at 50 ms or 1 000 ms sampling;
    /// more samples give higher resolution, no code change required.
    /// </summary>
    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 1) return sorted[0];
        int index = (int)Math.Ceiling(p * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}
