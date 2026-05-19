using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataAccess.Models.Database.Benchmark;

/// <summary>
/// Stores the raw metrics captured for a single message within a benchmark run.
/// All timing values are in microseconds (µs) as doubles to preserve sub-millisecond
/// precision. Computed convenience columns (TotalWireBytes, EncryptionOverheadBytes,
/// TotalRoundTripMicroseconds) are stored rather than computed at query time to
/// simplify aggregate and comparison queries.
/// </summary>
[Table("BenchmarkMessageResult", Schema = "benchmark")]
public class BenchmarkMessageResultDaM
{
    [Key]
    public Guid BenchmarkMessageResultId { get; set; }

    [Required]
    public Guid BenchmarkRunId { get; set; }

    [ForeignKey(nameof(BenchmarkRunId))]
    public BenchmarkRunDaM? Run { get; set; }

    /// <summary>Zero-based index of this message within the benchmark run.</summary>
    [Required]
    public int MessageIndex { get; set; }

    // ── Timing (µs) ───────────────────────────────────────────────────────────

    [Required]
    public double EncryptMicroseconds { get; set; }

    [Required]
    public double DecryptMicroseconds { get; set; }

    /// <summary>Zero when no authenticator was used.</summary>
    [Required]
    public double SignMicroseconds { get; set; }

    /// <summary>Zero when no authenticator was used.</summary>
    [Required]
    public double VerifyMicroseconds { get; set; }

    /// <summary>
    /// Wall-clock SignalR round-trip time in µs (encrypted wire payload sent to
    /// BenchmarkHub and echoed back). Zero for in-process benchmark runs.
    /// </summary>
    [Required]
    public double SignalRTransitMicroseconds { get; set; }

    /// <summary>Sum of all timing fields (including SignalR transit when applicable).</summary>
    [Required]
    public double TotalRoundTripMicroseconds { get; set; }

    // ── Sizes (bytes) ─────────────────────────────────────────────────────────

    [Required]
    public int PlaintextBytes { get; set; }

    [Required]
    public int CiphertextBytes { get; set; }

    /// <summary>
    /// Key-encapsulation material size. Zero for symmetric ciphers.
    /// RSA-Hybrid: wrapped AES key; ECDH-Hybrid: ephemeral public key DER;
    /// ML-KEM-Hybrid: KEM ciphertext.
    /// </summary>
    [Required]
    public int EncapsulatedKeyBytes { get; set; }

    /// <summary>MAC tag size. Zero when no authenticator was used.</summary>
    [Required]
    public int MacTagBytes { get; set; }

    /// <summary>CiphertextBytes + EncapsulatedKeyBytes + MacTagBytes.</summary>
    [Required]
    public int TotalWireBytes { get; set; }

    /// <summary>TotalWireBytes - PlaintextBytes.</summary>
    [Required]
    public int EncryptionOverheadBytes { get; set; }

    // ── Memory ────────────────────────────────────────────────────────────────

    /// <summary>Managed heap bytes allocated on the benchmark thread during encrypt + sign.</summary>
    [Required]
    public long GcAllocatedBytes { get; set; }

    /// <summary>Gen-0 GC collections triggered during encrypt + sign.</summary>
    [Required]
    public int GcGen0Collections { get; set; }

    // ── Correctness ───────────────────────────────────────────────────────────

    /// <summary>True if decryption produced the expected plaintext.</summary>
    [Required]
    public bool DecryptSuccess { get; set; }

    /// <summary>True if MAC verification passed (or no authenticator was used).</summary>
    [Required]
    public bool VerifySuccess { get; set; }
}
