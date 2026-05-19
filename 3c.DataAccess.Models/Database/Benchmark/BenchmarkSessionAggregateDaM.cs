using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataAccess.Models.Database.Benchmark;

/// <summary>
/// Pre-computed aggregate statistics for a benchmark run.
/// One row per <see cref="BenchmarkRunDaM"/> — written once after all messages
/// are measured so comparison queries never need to re-aggregate raw rows.
///
/// Percentile fields (P50, P95, P99) are computed over TotalRoundTripMicroseconds
/// at write time. All timing fields are in microseconds (µs).
/// </summary>
[Table("BenchmarkSessionAggregate", Schema = "benchmark")]
public class BenchmarkSessionAggregateDaM
{
    [Key]
    public Guid BenchmarkSessionAggregateId { get; set; }

    [Required]
    public Guid BenchmarkRunId { get; set; }

    [ForeignKey(nameof(BenchmarkRunId))]
    public BenchmarkRunDaM? Run { get; set; }

    // ── Timing averages (µs) ──────────────────────────────────────────────────

    [Required]
    public double AvgEncryptMicroseconds { get; set; }

    [Required]
    public double AvgDecryptMicroseconds { get; set; }

    [Required]
    public double AvgSignMicroseconds { get; set; }

    [Required]
    public double AvgVerifyMicroseconds { get; set; }

    /// <summary>
    /// Average SignalR transit time per message in µs.
    /// Zero for in-process benchmark runs that do not use SignalR as transport.
    /// </summary>
    [Required]
    public double AvgSignalRTransitMicroseconds { get; set; }

    [Required]
    public double AvgTotalRoundTripMicroseconds { get; set; }

    // ── Round-trip distribution (µs) ──────────────────────────────────────────

    [Required]
    public double MinRoundTripMicroseconds { get; set; }

    [Required]
    public double MaxRoundTripMicroseconds { get; set; }

    /// <summary>Median (50th percentile) round-trip latency in µs.</summary>
    [Required]
    public double P50RoundTripMicroseconds { get; set; }

    /// <summary>95th percentile round-trip latency in µs.</summary>
    [Required]
    public double P95RoundTripMicroseconds { get; set; }

    /// <summary>99th percentile round-trip latency in µs.</summary>
    [Required]
    public double P99RoundTripMicroseconds { get; set; }

    // ── Size averages (bytes) ─────────────────────────────────────────────────

    [Required]
    public double AvgCiphertextBytes { get; set; }

    /// <summary>Average encapsulated key size. Zero for symmetric ciphers.</summary>
    [Required]
    public double AvgEncapsulatedKeyBytes { get; set; }

    /// <summary>Average MAC tag size. Zero when no authenticator is used.</summary>
    [Required]
    public double AvgMacTagBytes { get; set; }

    [Required]
    public double AvgTotalWireBytes { get; set; }

    [Required]
    public double AvgEncryptionOverheadBytes { get; set; }

    // ── Memory totals ─────────────────────────────────────────────────────────

    /// <summary>Sum of GcAllocatedBytes across all messages in the run.</summary>
    [Required]
    public long TotalGcAllocatedBytes { get; set; }

    /// <summary>Average GC bytes allocated per message.</summary>
    [Required]
    public double AvgGcAllocatedBytesPerMessage { get; set; }

    /// <summary>Total Gen-0 collections triggered across all messages.</summary>
    [Required]
    public int TotalGcGen0Collections { get; set; }

    // ── Correctness ───────────────────────────────────────────────────────────

    /// <summary>Number of messages where both DecryptSuccess and VerifySuccess were true.</summary>
    [Required]
    public int SuccessfulMessages { get; set; }

    /// <summary>
    /// Fraction of messages that succeeded: SuccessfulMessages / MessageCount.
    /// Stored as a double in [0, 1]. Should be 1.0 for any correct implementation.
    /// </summary>
    [Required]
    public double SuccessRate { get; set; }
}
