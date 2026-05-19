using Domain.Services.Encryptions.Benchmark;
using Orleans;

namespace Benchmark.Grains.Interfaces;

/// <summary>
/// Orleans grain interface for running an encryption benchmark session.
///
/// One grain instance = one benchmark run. The grain holds the cipher and
/// optional authenticator for its entire lifetime so per-session key-generation
/// cost is captured as part of the activation overhead rather than being
/// repeated per message.
///
/// Typical call sequence from a slice or hub:
/// <code>
/// var runId = Guid.NewGuid().ToString();
/// var grain  = grainFactory.GetGrain&lt;IBenchmarkSessionGrain&gt;(runId);
/// await grain.ConfigureAsync(AlgorithmIds.AES_GCM_256, AuthIds.HMAC_SHA3_256);
/// var snapshots = await grain.RunBenchmarkAsync(messages, warmupCount: 3);
/// </code>
/// </summary>
[Alias("Orleans.Grains.Interfaces.IBenchmarkSessionGrain")]
public interface IBenchmarkSessionGrain : IGrainWithStringKey
{
    /// <summary>
    /// Initialises the cipher and optional authenticator for this session.
    /// Must be called once before <see cref="RunBenchmarkAsync"/>.
    /// Calling again replaces the existing key material and resets the configuration.
    /// </summary>
    /// <param name="algorithmId">One of <c>AlgorithmCatalog.AlgorithmIds.*</c>.</param>
    /// <param name="authId">
    /// One of <c>AlgorithmCatalog.AuthIds.*</c>, or <c>null</c> for no MAC.
    /// </param>
    Task ConfigureAsync(string algorithmId, string? authId);

    /// <summary>
    /// Runs the benchmark over the provided messages and returns one
    /// <see cref="MetricsSnapshot"/> per non-warmup message.
    /// </summary>
    /// <param name="messages">Plaintext message payloads to benchmark.</param>
    /// <param name="warmupCount">
    /// Number of warm-up iterations to run before measurements begin.
    /// Defaults to 3.
    /// </param>
    /// <returns>Read-only list with one snapshot per measured message.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <see cref="ConfigureAsync"/> has not been called first.
    /// </exception>
    Task<IReadOnlyList<MetricsSnapshot>> RunBenchmarkAsync(
        IReadOnlyList<byte[]> messages,
        int warmupCount = 3);

    /// <summary>
    /// Returns the algorithm ID and auth ID this session was configured with,
    /// or empty strings / null if not yet configured.
    /// </summary>
    Task<(string AlgorithmId, string? AuthId, bool IsConfigured)> GetConfigurationAsync();

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> using the configured cipher and optional
    /// authenticator, then encodes the result into a length-prefixed wire payload.
    ///
    /// Use in conjunction with <see cref="DecryptMessageAsync"/> when the slice
    /// orchestrates the SignalR-transport benchmark: encrypt → send via SignalR →
    /// receive echo → decrypt.
    /// </summary>
    /// <param name="plaintext">Raw bytes to encrypt.</param>
    /// <returns>
    /// An <see cref="EncryptPhaseResult"/> containing the binary wire payload
    /// and per-phase timing / GC metrics.
    /// </returns>
    Task<EncryptPhaseResult> EncryptMessageAsync(byte[] plaintext);

    /// <summary>
    /// Decrypts a wire payload previously produced by <see cref="EncryptMessageAsync"/>
    /// (and echoed back through SignalR).
    /// </summary>
    /// <param name="wirePayload">
    /// The bytes decoded from the base64 string received as <c>BenchmarkEchoResponse</c>.
    /// Must have been encoded by <see cref="BenchmarkWireFormat.Encode"/>.
    /// </param>
    /// <returns>
    /// A <see cref="DecryptPhaseResult"/> with per-phase timings and correctness flags.
    /// </returns>
    Task<DecryptPhaseResult> DecryptMessageAsync(byte[] wirePayload);

    /// <summary>
    /// Signals that the SignalR-transport benchmark session is complete and causes
    /// the grain to deactivate, releasing cipher key material promptly.
    /// Call once after the measured message loop finishes.
    /// </summary>
    Task CompleteSessionAsync();
}
