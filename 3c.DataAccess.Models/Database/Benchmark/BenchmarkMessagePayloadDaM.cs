using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataAccess.Models.Database.Benchmark;

/// <summary>
/// Stores the raw encrypted payload bytes captured during a benchmark run.
/// One row per measured message, linked 1-to-1 with the corresponding
/// <see cref="BenchmarkMessageResultDaM"/> row.
///
/// Kept in a separate table from <see cref="BenchmarkMessageResultDaM"/> so that
/// the heavy binary columns do not bloat metrics-only queries.
/// </summary>
[Table("BenchmarkMessagePayload", Schema = "benchmark")]
public class BenchmarkMessagePayloadDaM
{
    [Key]
    public Guid BenchmarkMessagePayloadId { get; set; }

    [Required]
    public Guid BenchmarkRunId { get; set; }

    [ForeignKey(nameof(BenchmarkRunId))]
    public BenchmarkRunDaM? Run { get; set; }

    /// <summary>Zero-based index matching the corresponding BenchmarkMessageResult row.</summary>
    [Required]
    public int MessageIndex { get; set; }

    /// <summary>
    /// Denormalised algorithm ID (from BenchmarkRun) so payloads can be
    /// queried and grouped by algorithm without a JOIN.
    /// </summary>
    [Required]
    public string AlgorithmId { get; set; } = "";

    // ── Raw payload bytes ─────────────────────────────────────────────────────

    /// <summary>
    /// Encrypted ciphertext bytes produced by <c>IMessageCipher.Encrypt</c>.
    /// For symmetric ciphers includes the embedded IV/nonce.
    /// For hybrid ciphers this is the data ciphertext only;
    /// key-encapsulation material is in <see cref="EncapsulatedKey"/>.
    /// </summary>
    [Required]
    public byte[] Ciphertext { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Key-encapsulation bytes. <c>null</c> for symmetric ciphers.
    /// RSA-Hybrid: wrapped AES key; ECDH-Hybrid: ephemeral public key DER;
    /// ML-KEM-Hybrid: KEM ciphertext.
    /// </summary>
    public byte[]? EncapsulatedKey { get; set; }

    /// <summary>
    /// MAC tag bytes. <c>null</c> when no authenticator was used.
    /// </summary>
    public byte[]? MacTag { get; set; }

    // ── Size convenience columns ──────────────────────────────────────────────

    /// <summary>Original plaintext message size in bytes.</summary>
    [Required]
    public int PlaintextBytes { get; set; }

    /// <summary>
    /// Total wire bytes: <c>Ciphertext.Length + (EncapsulatedKey?.Length ?? 0) + (MacTag?.Length ?? 0)</c>.
    /// Stored for fast size-comparison queries without loading binary columns.
    /// </summary>
    [Required]
    public int TotalWireBytes { get; set; }

    // ── Storage tracking ──────────────────────────────────────────────────────

    /// <summary>
    /// Relative path of the <c>.bin</c> file written to disk (relative to the
    /// configured <c>BenchmarkPayload:StorageRoot</c>).
    /// <c>null</c> if the file could not be written.
    /// </summary>
    public string? FilePath { get; set; }
}
