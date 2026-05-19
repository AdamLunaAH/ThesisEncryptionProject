using Orleans;

namespace Domain.Services.Encryptions.Benchmark;

/// <summary>
/// Combines the performance metrics for one encrypt/decrypt round-trip with
/// the raw encrypted payload bytes produced during that round.
///
/// Returned by <see cref="BenchmarkMetricsCollector.RunBenchmarkWithPayloads"/>
/// and passed through Orleans grain boundaries, so it is marked with
/// <c>[GenerateSerializer]</c>.
/// </summary>
[GenerateSerializer]
[Alias("Domain.Services.Encryptions.Benchmark.PayloadCapture")]
public sealed class PayloadCapture
{
    /// <summary>Timing and size metrics for this message.</summary>
    [Id(0)] public MetricsSnapshot Metrics { get; init; }

    /// <summary>Raw encrypted ciphertext bytes.</summary>
    [Id(1)] public required byte[] Ciphertext { get; init; }

    /// <summary>
    /// Key-encapsulation material. <c>null</c> for symmetric ciphers.
    /// </summary>
    [Id(2)] public byte[]? EncapsulatedKey { get; init; }

    /// <summary>
    /// MAC tag. <c>null</c> when no authenticator was used.
    /// </summary>
    [Id(3)] public byte[]? MacTag { get; init; }
}
