using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataAccess.Models.Database.Benchmark;

/// <summary>
/// Header record for one benchmark run.
/// One row is created each time the benchmark controller endpoint is called.
/// Child rows in <see cref="MessageResults"/> hold per-message metrics.
/// The <see cref="Aggregate"/> row holds pre-computed summary statistics.
/// </summary>
[Table("BenchmarkRun", Schema = "benchmark")]
public class BenchmarkRunDaM
{
    [Key]
    public Guid BenchmarkRunId { get; set; }

    /// <summary>UTC timestamp when the run was triggered.</summary>
    [Required]
    public DateTime RunAt { get; set; }

    /// <summary>Canonical cipher algorithm ID, e.g. "AES-256-GCM".</summary>
    [Required]
    public string AlgorithmId { get; set; } = "";

    /// <summary>Canonical algorithm family, e.g. "Symmetric".</summary>
    [Required]
    public string AlgorithmFamily { get; set; } = "";

    /// <summary>Algorithm generation, e.g. "Modern".</summary>
    [Required]
    public string Generation { get; set; } = "";

    /// <summary>
    /// Canonical authenticator ID, e.g. "HMAC-SHA256".
    /// <c>null</c> when no MAC layer was applied.
    /// </summary>
    public string? AuthId { get; set; }

    /// <summary>Number of messages measured (excluding warmup rounds).</summary>
    [Required]
    public int MessageCount { get; set; }

    /// <summary>Plaintext payload size in bytes used for every message in this run.</summary>
    [Required]
    public int MessageSizeBytes { get; set; }

    /// <summary>Number of warmup iterations executed before measurement started.</summary>
    [Required]
    public int WarmupCount { get; set; }

    /// <summary>
    /// TLS version used during the run (e.g. "Tls13", "SystemDefault").
    /// Set for SignalR-transport and standard benchmark runs; <c>null</c> for
    /// payload-storage runs where no network transport is involved.
    /// </summary>
    public string? TlsVersion { get; set; }

    /// <summary>Optional free-text annotation (e.g. "TLS 1.3 comparison run").</summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Global run counter. Increments by 1 for every new <see cref="BenchmarkRunDaM"/>
    /// row, regardless of algorithm. Resets to 1 after a full table clear.
    /// </summary>
    [Required]
    public int RunNumber { get; set; }

    /// <summary>
    /// Per-algorithm run counter. Increments by 1 for every new row that shares
    /// the same <see cref="AlgorithmId"/>. Resets to 1 after a full table clear.
    /// </summary>
    [Required]
    public int AlgorithmRunNumber { get; set; }

    public List<BenchmarkMessageResultDaM> MessageResults { get; set; } = new();
    public BenchmarkSessionAggregateDaM? Aggregate { get; set; }
    public List<BenchmarkMessagePayloadDaM> Payloads { get; set; } = new();
}
