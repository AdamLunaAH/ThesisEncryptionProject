using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataAccess.Models.Database.Benchmark;

/// <summary>
/// One raw resource-monitor tick captured while a single benchmark run was
/// executing. Each row corresponds to one
/// <c>ResourceMetricsSnapshot</c> broadcast by
/// <c>ResourceMonitorBroadcaster</c>.
///
/// Schema: <c>benchmark.BenchmarkResourceSample</c>.
/// </summary>
[Table("BenchmarkResourceSample", Schema = "benchmark")]
public class BenchmarkResourceSampleDaM
{
    [Key]
    public Guid BenchmarkResourceSampleId { get; set; }

    /// <summary>Foreign key to the benchmark run this sample belongs to.</summary>
    [Required]
    public Guid BenchmarkRunId { get; set; }

    /// <summary>UTC instant the snapshot was captured by the broadcaster.</summary>
    [Required]
    public DateTimeOffset CapturedAt { get; set; }

    // ── CPU ───────────────────────────────────────────────────────────────────

    /// <summary>Process CPU utilisation across all cores, 0–100.</summary>
    public double CpuPercent { get; set; }

    // ── Memory ────────────────────────────────────────────────────────────────

    /// <summary>Resident memory used by the process in megabytes.</summary>
    public double MemoryUsedMb { get; set; }

    /// <summary>GC managed heap size in megabytes.</summary>
    public double GcHeapMb { get; set; }

    // ── GC collections (cumulative since process start at time of capture) ────

    public int GcGen0Collections { get; set; }
    public int GcGen1Collections { get; set; }
    public int GcGen2Collections { get; set; }

    // ── Thread pool ───────────────────────────────────────────────────────────

    /// <summary>Active thread-pool worker threads at the time of capture.</summary>
    public int ThreadPoolWorkerThreads { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    [ForeignKey(nameof(BenchmarkRunId))]
    public BenchmarkRunDaM? BenchmarkRun { get; set; }
}
