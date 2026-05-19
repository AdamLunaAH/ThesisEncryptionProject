using Orleans;

namespace Domain.Services.Encryptions.Benchmark;

/// <summary>
/// Captures all measurable metrics for a single encrypt → sign → decrypt → verify
/// round-trip. Stored as a value type so benchmark result lists avoid heap pressure.
///
/// Timing fields are in microseconds (µs) as doubles so sub-millisecond precision
/// is preserved when writing to the database or computing aggregates.
///
/// GC allocation tracking covers the encrypt + sign phase only (the "send" side)
/// because that is the per-message cost a server incurs during a real session.
/// Decrypt + verify are measured for round-trip completeness but GC allocations
/// on the receive side are deliberately excluded to keep the metric focused.
/// </summary>
[GenerateSerializer]
[Alias("Domain.Services.Encryptions.Benchmark.MetricsSnapshot")]
public readonly struct MetricsSnapshot
{
    // ── Timing ────────────────────────────────────────────────────────────────

    /// <summary>Wall-clock time to run <c>IMessageCipher.Encrypt</c> in µs.</summary>
    [Id(0)] public double EncryptMicroseconds { get; init; }

    /// <summary>Wall-clock time to run <c>IMessageCipher.Decrypt</c> in µs.</summary>
    [Id(1)] public double DecryptMicroseconds { get; init; }

    /// <summary>
    /// Wall-clock time to run <c>IMessageAuthenticator.Sign</c> in µs.
    /// Zero when no authenticator was used.
    /// </summary>
    [Id(2)] public double SignMicroseconds { get; init; }

    /// <summary>
    /// Wall-clock time to run <c>IMessageAuthenticator.Verify</c> in µs.
    /// Zero when no authenticator was used.
    /// </summary>
    [Id(3)] public double VerifyMicroseconds { get; init; }

    /// <summary>
    /// Wall-clock SignalR round-trip time in µs for the encrypted wire payload
    /// (from SendAsync returning to BenchmarkEchoResponse firing).
    /// Zero for in-process benchmark runs that do not use SignalR as transport.
    /// </summary>
    [Id(12)] public double SignalRTransitMicroseconds { get; init; }

    /// <summary>
    /// Sum of all timing fields — the total per-message overhead including
    /// both cipher phases and, when applicable, the SignalR network transit.
    /// </summary>
    public double TotalRoundTripMicroseconds =>
        EncryptMicroseconds + SignMicroseconds + SignalRTransitMicroseconds + DecryptMicroseconds + VerifyMicroseconds;

    // ── Sizes ─────────────────────────────────────────────────────────────────

    /// <summary>Original plaintext message size in bytes.</summary>
    [Id(4)] public int PlaintextBytes { get; init; }

    /// <summary>
    /// Size of the encrypted payload in bytes. For symmetric ciphers this includes
    /// the embedded IV/nonce. For hybrid ciphers the data ciphertext only;
    /// the key-encapsulation material is tracked separately in
    /// <see cref="EncapsulatedKeyBytes"/>.
    /// </summary>
    [Id(5)] public int CiphertextBytes { get; init; }

    /// <summary>
    /// Size of the encapsulated key material in bytes.
    /// Zero for symmetric ciphers. For hybrid ciphers:
    /// RSA-Hybrid → wrapped AES key (256 / 512 B),
    /// ECDH-Hybrid → ephemeral public key DER (91 / 120 B),
    /// ML-KEM-Hybrid → KEM ciphertext (1088 / 1568 B).
    /// </summary>
    [Id(6)] public int EncapsulatedKeyBytes { get; init; }

    /// <summary>
    /// MAC tag size in bytes appended to the wire message.
    /// Zero when no authenticator was used.
    /// </summary>
    [Id(7)] public int MacTagBytes { get; init; }

    /// <summary>
    /// Total bytes that would be transmitted over the wire for this message:
    /// <c>CiphertextBytes + EncapsulatedKeyBytes + MacTagBytes</c>.
    /// Compared to <see cref="PlaintextBytes"/> to compute encryption overhead.
    /// </summary>
    public int TotalWireBytes => CiphertextBytes + EncapsulatedKeyBytes + MacTagBytes;

    /// <summary>
    /// Byte overhead introduced by encryption: <c>TotalWireBytes - PlaintextBytes</c>.
    /// Positive for all ciphers (nonce/IV + tag + optional key encapsulation).
    /// </summary>
    public int EncryptionOverheadBytes => TotalWireBytes - PlaintextBytes;

    // ── Memory ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Managed heap bytes allocated on the benchmarking thread during the encrypt
    /// and sign operations, measured via
    /// <c>GC.GetAllocatedBytesForCurrentThread()</c>.
    /// Captures short-lived intermediate buffers created inside the cipher/MAC.
    /// </summary>
    [Id(8)] public long GcAllocatedBytes { get; init; }

    /// <summary>
    /// Number of Gen-0 GC collections that occurred during the encrypt + sign phase.
    /// Non-zero values indicate the operation generated enough allocation pressure to
    /// trigger a collection — relevant for latency jitter analysis.
    /// </summary>
    [Id(9)] public int GcGen0Collections { get; init; }

    // ── Correctness ───────────────────────────────────────────────────────────

    /// <summary>
    /// True if decryption produced the expected plaintext.
    /// Should always be true; false indicates a cipher implementation bug.
    /// </summary>
    [Id(10)] public bool DecryptSuccess { get; init; }

    /// <summary>
    /// True if MAC verification passed (or no authenticator was used).
    /// Should always be true; false indicates an authenticator implementation bug.
    /// </summary>
    [Id(11)] public bool VerifySuccess { get; init; }
}
