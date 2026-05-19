namespace CrossCut.Concerns.Monitor;

/// <summary>
/// In-memory result produced by <see cref="IResourceCaptureService.StopCapture"/>.
/// Contains both the raw per-tick samples collected during a benchmark run and
/// the pre-computed aggregate statistics derived from those samples.
///
/// Passed from the benchmark slice back to the repository layer for persistence.
/// </summary>
public sealed record ResourceCaptureSummary
{
    /// <summary>Number of snapshots collected during the capture window.</summary>
    public int SampleCount { get; init; }

    /// <summary>
    /// Wall-clock duration of the capture window in milliseconds
    /// (<c>lastSample.Timestamp − firstSample.Timestamp</c>).
    /// Zero when fewer than two samples were collected.
    /// </summary>
    public double DurationMilliseconds { get; init; }

    // ── CPU (%) ───────────────────────────────────────────────────────────────

    public double CpuMax { get; init; }
    public double CpuAvg { get; init; }
    public double CpuP50 { get; init; }
    public double CpuP95 { get; init; }
    public double CpuP99 { get; init; }

    // ── Memory (MB) ───────────────────────────────────────────────────────────

    public double MemoryMaxMb { get; init; }
    public double MemoryAvgMb { get; init; }
    public double MemoryP50Mb { get; init; }
    public double MemoryP95Mb { get; init; }
    public double MemoryP99Mb { get; init; }

    // ── GC collections (delta within the capture window) ─────────────────────

    /// <summary>Gen-0 collections that occurred during the run
    /// (<c>lastSample.Gen0 − firstSample.Gen0</c>).</summary>
    public int GcGen0Delta { get; init; }

    /// <summary>Gen-1 collections delta.</summary>
    public int GcGen1Delta { get; init; }

    /// <summary>Gen-2 collections delta.</summary>
    public int GcGen2Delta { get; init; }

    // ── Thread pool ───────────────────────────────────────────────────────────

    /// <summary>Peak active thread-pool worker count seen during the run.</summary>
    public int ThreadPoolMaxWorkers { get; init; }

    // ── Raw samples ───────────────────────────────────────────────────────────

    /// <summary>
    /// Ordered list of all raw snapshots collected during the capture window.
    /// Used by the repository to persist <c>BenchmarkResourceSample</c> rows.
    /// </summary>
    public IReadOnlyList<ResourceMetricsSnapshot> Samples { get; init; } = [];
}
