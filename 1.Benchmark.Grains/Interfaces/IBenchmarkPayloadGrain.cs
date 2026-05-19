using Domain.Services.Encryptions.Benchmark;
using Orleans;

namespace Benchmark.Grains.Interfaces;

/// <summary>
/// Orleans grain interface for running an encryption benchmark that captures
/// raw ciphertext bytes alongside performance metrics.
///
/// Kept as a separate grain from <see cref="IBenchmarkSessionGrain"/> so that
/// payload-bearing runs are independently identifiable and their larger return
/// values do not affect the lighter metrics-only path.
///
/// Typical call sequence:
/// <code>
/// var runId = Guid.NewGuid().ToString();
/// var grain  = grainFactory.GetGrain&lt;IBenchmarkPayloadGrain&gt;(runId);
/// await grain.ConfigureAsync("AES-256-GCM", null);
/// var captures = await grain.RunAndCaptureAsync(messages, warmupCount: 3);
/// </code>
/// </summary>
[Alias("Orleans.Grains.Interfaces.IBenchmarkPayloadGrain")]
public interface IBenchmarkPayloadGrain : IGrainWithStringKey
{
    /// <summary>
    /// Initialises the cipher and optional authenticator for this session.
    /// Must be called once before <see cref="RunAndCaptureAsync"/>.
    /// </summary>
    Task ConfigureAsync(string algorithmId, string? authId);

    /// <summary>
    /// Runs the benchmark and returns one <see cref="PayloadCapture"/> per
    /// non-warmup message. Each capture contains both the performance metrics
    /// and the raw ciphertext bytes.
    /// </summary>
    Task<IReadOnlyList<PayloadCapture>> RunAndCaptureAsync(
        IReadOnlyList<byte[]> messages,
        int warmupCount = 3);

    /// <summary>
    /// Returns the algorithm configuration for this grain instance.
    /// </summary>
    Task<(string AlgorithmId, string? AuthId, bool IsConfigured)> GetConfigurationAsync();
}
