using Benchmark.Grains.Interfaces;
using Benchmark.Grains.Models;
using Domain.Services.Encryptions.Benchmark;
using Domain.Services.Encryptions.Benchmark.Ciphers;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Benchmark.Grains.Grains;

/// <summary>
/// Orleans grain that runs an encryption benchmark and returns both performance
/// metrics and the raw ciphertext bytes for each measured message.
///
/// Kept separate from <see cref="BenchmarkSessionGrain"/> so payload runs are
/// independently queryable and the heavier return type does not affect the
/// metrics-only path.
///
/// One grain instance = one run. The primary key is the run ID (<c>Guid.ToString()</c>).
/// The grain deactivates itself after <see cref="RunAndCaptureAsync"/> completes.
/// </summary>
public sealed class BenchmarkPayloadGrain : Grain<BenchmarkPayloadState>, IBenchmarkPayloadGrain
{
    private readonly ILogger<BenchmarkPayloadGrain> _logger;

    private IMessageCipher? _cipher;
    private IMessageAuthenticator? _authenticator;

    public BenchmarkPayloadGrain(ILogger<BenchmarkPayloadGrain> logger)
    {
        _logger = logger;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        State.RunId = this.GetPrimaryKeyString();

        if (State.IsConfigured && !string.IsNullOrEmpty(State.AlgorithmId))
            BuildCipherAndAuthenticator(State.AlgorithmId, State.AuthId);

        await WriteStateAsync();
    }

    public override async Task OnDeactivateAsync(
        DeactivationReason reason,
        CancellationToken cancellationToken)
    {
        DisposeCipherAndAuthenticator();
        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    // ── IBenchmarkPayloadGrain ────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task ConfigureAsync(string algorithmId, string? authId)
    {
        if (string.IsNullOrWhiteSpace(algorithmId))
            throw new ArgumentException("algorithmId must not be empty.", nameof(algorithmId));

        DisposeCipherAndAuthenticator();

        State.AlgorithmId = algorithmId;
        State.AuthId = authId;
        State.IsConfigured = true;

        BuildCipherAndAuthenticator(algorithmId, authId);

        _logger.LogInformation(
            "[BenchmarkPayloadGrain:{RunId}] Configured algorithmId={AlgorithmId} authId={AuthId}",
            State.RunId, algorithmId, authId ?? "(none)");

        await WriteStateAsync();
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<PayloadCapture>> RunAndCaptureAsync(
        IReadOnlyList<byte[]> messages,
        int warmupCount = 3)
    {
        if (_cipher is null)
            throw new InvalidOperationException(
                $"BenchmarkPayloadGrain '{State.RunId}' is not configured. " +
                "Call ConfigureAsync before RunAndCaptureAsync.");

        if (messages is null || messages.Count == 0)
            throw new ArgumentException("messages must not be null or empty.", nameof(messages));

        _logger.LogInformation(
            "[BenchmarkPayloadGrain:{RunId}] RunAndCapture start — algorithm={AlgorithmId} " +
            "messages={MessageCount} warmup={WarmupCount}",
            State.RunId, State.AlgorithmId, messages.Count, warmupCount);

        var captures = BenchmarkMetricsCollector.RunBenchmarkWithPayloads(
            _cipher, _authenticator, messages, warmupCount);

        _logger.LogInformation(
            "[BenchmarkPayloadGrain:{RunId}] RunAndCapture complete — {Count} captures.",
            State.RunId, captures.Count);

        DeactivateOnIdle();

        return Task.FromResult(captures);
    }

    /// <inheritdoc/>
    public Task<(string AlgorithmId, string? AuthId, bool IsConfigured)> GetConfigurationAsync()
        => Task.FromResult((State.AlgorithmId, State.AuthId, State.IsConfigured));

    // ── Private helpers ───────────────────────────────────────────────────────

    private void BuildCipherAndAuthenticator(string algorithmId, string? authId)
    {
        _cipher = CipherFactory.Create(algorithmId);
        _authenticator = authId is not null ? AuthenticatorFactory.Create(authId) : null;
    }

    private void DisposeCipherAndAuthenticator()
    {
        _cipher?.Dispose();
        _cipher = null;
        _authenticator = null;
    }
}
