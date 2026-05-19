using Benchmark.Grains.Interfaces;
using Benchmark.Grains.Models;
using Domain.Services.Encryptions.Benchmark;
using Domain.Services.Encryptions.Benchmark.Ciphers;
using Microsoft.Extensions.Logging;
using Orleans;
using System.Diagnostics;

namespace Benchmark.Grains.Grains;

/// <summary>
/// Orleans grain that manages a single encryption benchmark session.
///
/// Key design decisions:
/// <list type="bullet">
///   <item>
///     One grain instance = one benchmark run. The grain primary key is the
///     run ID (<c>Guid.ToString()</c>), set by the caller before activation.
///   </item>
///   <item>
///     The <see cref="IMessageCipher"/> and <see cref="IMessageAuthenticator"/>
///     are held as non-serialisable fields. They are created inside
///     <see cref="ConfigureAsync"/> and reconstructed in
///     <see cref="OnActivateAsync"/> from the persisted algorithm IDs so the
///     grain survives a silo restart during a long test run.
///   </item>
///   <item>
///     The grain is single-use and deactivates itself after the benchmark
///     completes, which triggers <see cref="OnDeactivateAsync"/> and
///     disposes the cipher resources.
///   </item>
/// </list>
/// </summary>
public sealed class BenchmarkSessionGrain : Grain<BenchmarkSessionState>, IBenchmarkSessionGrain
{
    private readonly ILogger<BenchmarkSessionGrain> _logger;

    // Non-serialisable key material — rebuilt from State on reactivation.
    private IMessageCipher? _cipher;
    private IMessageAuthenticator? _authenticator;

    public BenchmarkSessionGrain(ILogger<BenchmarkSessionGrain> logger)
    {
        _logger = logger;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        State.RunId = this.GetPrimaryKeyString();

        // Rebuild key material if we are reactivating a previously configured grain.
        if (State.IsConfigured && !string.IsNullOrEmpty(State.AlgorithmId))
        {
            BuildCipherAndAuthenticator(State.AlgorithmId, State.AuthId);
        }

        await WriteStateAsync();
    }

    public override async Task OnDeactivateAsync(
        DeactivationReason reason,
        CancellationToken cancellationToken)
    {
        DisposeCipherAndAuthenticator();
        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    // ── IGrainWithStringKey implementation ────────────────────────────────────

    /// <inheritdoc/>
    public async Task ConfigureAsync(string algorithmId, string? authId)
    {
        if (string.IsNullOrWhiteSpace(algorithmId))
            throw new ArgumentException("algorithmId must not be empty.", nameof(algorithmId));

        // Dispose old key material before recreating.
        DisposeCipherAndAuthenticator();

        State.AlgorithmId = algorithmId;
        State.AuthId = authId;
        State.IsConfigured = true;

        BuildCipherAndAuthenticator(algorithmId, authId);

        _logger.LogInformation(
            "[BenchmarkSessionGrain:{RunId}] Configured algorithmId={AlgorithmId} authId={AuthId}",
            State.RunId, algorithmId, authId ?? "(none)");

        await WriteStateAsync();
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<MetricsSnapshot>> RunBenchmarkAsync(
        IReadOnlyList<byte[]> messages,
        int warmupCount = 3)
    {
        if (_cipher is null)
            throw new InvalidOperationException(
                $"BenchmarkSessionGrain '{State.RunId}' is not configured. " +
                "Call ConfigureAsync before RunBenchmarkAsync.");

        if (messages is null || messages.Count == 0)
            throw new ArgumentException("messages must not be null or empty.", nameof(messages));

        _logger.LogInformation(
            "[BenchmarkSessionGrain:{RunId}] RunBenchmark start — algorithm={AlgorithmId} " +
            "messages={MessageCount} warmup={WarmupCount}",
            State.RunId, State.AlgorithmId, messages.Count, warmupCount);

        var results = BenchmarkMetricsCollector.RunBenchmark(
            _cipher, _authenticator, messages, warmupCount);

        _logger.LogInformation(
            "[BenchmarkSessionGrain:{RunId}] RunBenchmark complete — {MeasuredCount} snapshots captured.",
            State.RunId, results.Count);

        // Deactivate after use so cipher key material is released promptly.
        DeactivateOnIdle();

        return Task.FromResult(results);
    }

    /// <inheritdoc/>
    public Task<(string AlgorithmId, string? AuthId, bool IsConfigured)> GetConfigurationAsync()
        => Task.FromResult((State.AlgorithmId, State.AuthId, State.IsConfigured));

    /// <inheritdoc/>
    public Task<EncryptPhaseResult> EncryptMessageAsync(byte[] plaintext)
    {
        if (_cipher is null)
            throw new InvalidOperationException(
                $"BenchmarkSessionGrain '{State.RunId}' is not configured. " +
                "Call ConfigureAsync before EncryptMessageAsync.");

        int gen0Before = GC.CollectionCount(0);
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();

        long t0 = Stopwatch.GetTimestamp();
        CipherResult encrypted = _cipher.Encrypt(plaintext);
        long t1 = Stopwatch.GetTimestamp();

        byte[]? tag = null;
        long t2 = t1, t3 = t1;

        if (_authenticator is not null)
        {
            t2 = Stopwatch.GetTimestamp();
            byte[] dataToSign = encrypted.EncapsulatedKey is null
                ? encrypted.Ciphertext
                : Combine(encrypted.Ciphertext, encrypted.EncapsulatedKey);
            tag = _authenticator.Sign(dataToSign);
            t3 = Stopwatch.GetTimestamp();
        }

        long allocAfter = GC.GetAllocatedBytesForCurrentThread();
        int gen0After = GC.CollectionCount(0);

        var wirePayload = BenchmarkWireFormat.Encode(
            encrypted.Ciphertext, encrypted.EncapsulatedKey, tag);

        return Task.FromResult(new EncryptPhaseResult
        {
            WirePayload = wirePayload,
            EncryptMicroseconds = (t1 - t0) * TicksToMicroseconds,
            SignMicroseconds = (t3 - t2) * TicksToMicroseconds,
            GcAllocatedBytes = Math.Max(0L, allocAfter - allocBefore),
            GcGen0Collections = Math.Max(0, gen0After - gen0Before),
            PlaintextBytes = plaintext.Length,
            CiphertextBytes = encrypted.Ciphertext.Length,
            EncapsulatedKeyBytes = encrypted.EncapsulatedKey?.Length ?? 0,
            MacTagBytes = tag?.Length ?? 0
        });
    }

    /// <inheritdoc/>
    public Task<DecryptPhaseResult> DecryptMessageAsync(byte[] wirePayload)
    {
        if (_cipher is null)
            throw new InvalidOperationException(
                $"BenchmarkSessionGrain '{State.RunId}' is not configured. " +
                "Call ConfigureAsync before DecryptMessageAsync.");

        var (ciphertext, encapsulatedKey, tag) = BenchmarkWireFormat.Decode(wirePayload);

        bool decryptSuccess = false;
        long t4 = Stopwatch.GetTimestamp();
        try
        {
            _ = _cipher.Decrypt(ciphertext, encapsulatedKey);
            decryptSuccess = true;
        }
        catch { /* leave decryptSuccess = false */ }
        long t5 = Stopwatch.GetTimestamp();

        bool verifySuccess = true;
        long t6 = t5, t7 = t5;

        if (_authenticator is not null && tag is not null)
        {
            t6 = Stopwatch.GetTimestamp();
            byte[] dataToVerify = encapsulatedKey is null
                ? ciphertext
                : Combine(ciphertext, encapsulatedKey);
            verifySuccess = _authenticator.Verify(dataToVerify, tag);
            t7 = Stopwatch.GetTimestamp();
        }

        return Task.FromResult(new DecryptPhaseResult
        {
            DecryptMicroseconds = (t5 - t4) * TicksToMicroseconds,
            VerifyMicroseconds = (t7 - t6) * TicksToMicroseconds,
            DecryptSuccess = decryptSuccess,
            VerifySuccess = verifySuccess
        });
    }

    /// <inheritdoc/>
    public Task CompleteSessionAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static readonly double TicksToMicroseconds = 1_000_000.0 / Stopwatch.Frequency;

    private static byte[] Combine(byte[] a, byte[] b)
    {
        var combined = new byte[a.Length + b.Length];
        a.CopyTo(combined, 0);
        b.CopyTo(combined, a.Length);
        return combined;
    }

    private void BuildCipherAndAuthenticator(string algorithmId, string? authId)
    {
        _cipher = CipherFactory.Create(algorithmId);

        _authenticator = authId is not null
            ? AuthenticatorFactory.Create(authId)
            : null;
    }

    private void DisposeCipherAndAuthenticator()
    {
        _cipher?.Dispose();
        _cipher = null;

        _authenticator = null;
    }
}
