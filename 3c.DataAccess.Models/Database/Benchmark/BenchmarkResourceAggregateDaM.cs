using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataAccess.Models.Database.Benchmark;

/// <summary>
/// Pre-computed aggregate statistics for the resource snapshots collected
/// during a single benchmark run. One row per run.
///
/// Mirrors the design of <see cref="BenchmarkSessionAggregateDaM"/>: the raw
/// samples live in <c>BenchmarkResourceSample</c>, this table stores derived
/// statistics so queries never need to scan every raw tick.
///
/// Schema: <c>benchmark.BenchmarkResourceAggregate</c>.
/// </summary>
[Table("BenchmarkResourceAggregate", Schema = "benchmark")]
public class BenchmarkResourceAggregateDaM
{
    [Key]
    public Guid BenchmarkResourceAggregateId { get; set; }

    /// <summary>Foreign key to the run these aggregates describe.</summary>
    [Required]
    public Guid BenchmarkRunId { get; set; }

    /// <summary>Number of raw samples that were collected during the run.</summary>
    public int SampleCount { get; set; }

    /// <summary>
    /// Wall-clock duration of the capture window in milliseconds
    /// (lastSample.Timestamp − firstSample.Timestamp).
    /// </summary>
    public double DurationMilliseconds { get; set; }

    // ── CPU (%) ───────────────────────────────────────────────────────────────

    public double CpuMax { get; set; }
    public double CpuAvg { get; set; }
    public double CpuP50 { get; set; }
    public double CpuP95 { get; set; }
    public double CpuP99 { get; set; }

    // ── Memory (MB) ───────────────────────────────────────────────────────────

    public double MemoryMaxMb { get; set; }
    public double MemoryAvgMb { get; set; }
    public double MemoryP50Mb { get; set; }
    public double MemoryP95Mb { get; set; }
    public double MemoryP99Mb { get; set; }

    // ── GC (deltas within the capture window) ────────────────────────────────

    /// <summary>Gen-0 collections that occurred during the run
    /// (lastSample.Gen0 − firstSample.Gen0).</summary>
    public int GcGen0Delta { get; set; }
    public int GcGen1Delta { get; set; }
    public int GcGen2Delta { get; set; }

    // ── Thread pool ───────────────────────────────────────────────────────────

    /// <summary>Peak active thread-pool worker count seen during the run.</summary>
    public int ThreadPoolMaxWorkers { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    [ForeignKey(nameof(BenchmarkRunId))]
    public BenchmarkRunDaM? BenchmarkRun { get; set; }
}
