using Orleans;

namespace Domain.Services.Encryptions.Benchmark;

/// <summary>
/// Captures the result of a single decrypt + optional verify phase performed by
/// <c>IBenchmarkSessionGrain.DecryptMessageAsync</c>.
/// </summary>
[GenerateSerializer]
[Alias("Domain.Services.Encryptions.Benchmark.DecryptPhaseResult")]
public readonly struct DecryptPhaseResult
{
    /// <summary>Wall-clock time to run <c>IMessageCipher.Decrypt</c> in µs.</summary>
    [Id(0)] public double DecryptMicroseconds { get; init; }

    /// <summary>
    /// Wall-clock time to run <c>IMessageAuthenticator.Verify</c> in µs.
    /// Zero when no authenticator was configured.
    /// </summary>
    [Id(1)] public double VerifyMicroseconds { get; init; }

    /// <summary>
    /// True if <c>cipher.Decrypt</c> completed without throwing.
    /// For AEAD ciphers (AES-GCM, ChaCha20-Poly1305) this implies
    /// authentication tag verification passed inside the cipher.
    /// </summary>
    [Id(2)] public bool DecryptSuccess { get; init; }

    /// <summary>
    /// True if the outer MAC tag verified correctly, or if no authenticator was used.
    /// </summary>
    [Id(3)] public bool VerifySuccess { get; init; }
}
