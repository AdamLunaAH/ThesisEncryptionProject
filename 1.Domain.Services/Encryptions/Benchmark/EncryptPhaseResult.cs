using Orleans;

namespace Domain.Services.Encryptions.Benchmark;

/// <summary>
/// Captures the output of a single encrypt + optional sign phase performed by
/// <c>IBenchmarkSessionGrain.EncryptMessageAsync</c>.
///
/// The <see cref="WirePayload"/> field holds the length-prefixed binary encoding of
/// [ciphertext | encapsulated key | mac tag] produced by
/// <see cref="BenchmarkWireFormat.Encode"/>. Base64-encode it to send over SignalR,
/// then pass the decoded bytes to <c>IBenchmarkSessionGrain.DecryptMessageAsync</c>.
/// </summary>
[GenerateSerializer]
[Alias("Domain.Services.Encryptions.Benchmark.EncryptPhaseResult")]
public readonly struct EncryptPhaseResult
{
    /// <summary>
    /// Length-prefixed binary encoding of the encrypted payload:
    /// <c>[int32 ciphertextLen][ciphertext][int32 encKeyLen][encKey][int32 tagLen][tag]</c>.
    /// Base64-encode before sending as a SignalR string.
    /// Pass the received (decoded) bytes to <c>DecryptMessageAsync</c> to decrypt.
    /// </summary>
    [Id(0)] public byte[] WirePayload { get; init; }

    /// <summary>Wall-clock time to run <c>IMessageCipher.Encrypt</c> in µs.</summary>
    [Id(1)] public double EncryptMicroseconds { get; init; }

    /// <summary>
    /// Wall-clock time to run <c>IMessageAuthenticator.Sign</c> in µs.
    /// Zero when no authenticator was configured.
    /// </summary>
    [Id(2)] public double SignMicroseconds { get; init; }

    /// <summary>Managed heap bytes allocated on the grain thread during encrypt + sign.</summary>
    [Id(3)] public long GcAllocatedBytes { get; init; }

    /// <summary>Gen-0 GC collections triggered during encrypt + sign.</summary>
    [Id(4)] public int GcGen0Collections { get; init; }

    /// <summary>Original plaintext message size in bytes.</summary>
    [Id(5)] public int PlaintextBytes { get; init; }

    /// <summary>Encrypted payload size in bytes (IV/nonce embedded for symmetric ciphers).</summary>
    [Id(6)] public int CiphertextBytes { get; init; }

    /// <summary>Encapsulated key material size in bytes. Zero for symmetric ciphers.</summary>
    [Id(7)] public int EncapsulatedKeyBytes { get; init; }

    /// <summary>MAC tag size in bytes. Zero when no authenticator was used.</summary>
    [Id(8)] public int MacTagBytes { get; init; }

    /// <summary>
    /// Total bytes that will be transmitted over the wire:
    /// <c>CiphertextBytes + EncapsulatedKeyBytes + MacTagBytes</c>.
    /// </summary>
    public int TotalWireBytes => CiphertextBytes + EncapsulatedKeyBytes + MacTagBytes;

    /// <summary>Byte overhead introduced by encryption: <c>TotalWireBytes - PlaintextBytes</c>.</summary>
    public int EncryptionOverheadBytes => TotalWireBytes - PlaintextBytes;
}
