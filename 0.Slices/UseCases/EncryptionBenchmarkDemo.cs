using Benchmark.Client.Services;
using Benchmark.Grains.Interfaces;
using CrossCut.Concerns.Monitor;
using DataAccess.Models.Database.Benchmark;
using DataAccess.Repositories.Database.Benchmark;
using Domain.Services.Encryptions.Benchmark;
using Microsoft.Extensions.Logging;
using Orleans;
using System.Security.Cryptography;

namespace Slices.UseCases;

/// <summary>
/// Slice that exercises the full encryption benchmark pipeline:
///
/// For each algorithm in <paramref name="algorithmIds"/>:
/// 1. Obtains a fresh <see cref="IBenchmarkSessionGrain"/> keyed by a new run GUID.
/// 2. Calls <c>ConfigureAsync</c> to initialise key material on the grain.
/// 3. Generates <paramref name="messageCount"/> random payloads of
///    <paramref name="messageSizeBytes"/> bytes (clamped to the cipher's
///    <c>MaxMessageBytes</c> limit for non-hybrid RSA).
/// 4. Calls <c>RunBenchmarkAsync</c> which drives the cipher + optional MAC
///    through each message and returns one <see cref="MetricsSnapshot"/> per
///    measured message.
/// 5. Maps snapshots to <see cref="BenchmarkMessageResultDaM"/> rows and saves
///    them together with a <see cref="BenchmarkRunDaM"/> header and a
///    <see cref="BenchmarkSessionAggregateDaM"/> summary via
///    <see cref="IBenchmarkRepository"/>.
///
/// If <paramref name="signalRHubUrl"/> is provided the slice also connects a
/// <see cref="BenchmarkSignalRClient"/> and sends one echo per measured message,
/// capturing wall-clock SignalR round-trip latency.  These results are returned
/// in the response object but are NOT stored in the database (no separate
/// SignalR table exists in this schema version).
/// </summary>
public static class EncryptionBenchmarkDemo
{
    /// <summary>
    /// Runs the benchmark and persists results.
    /// </summary>
    /// <param name="logger">Logger from the controller.</param>
    /// <param name="grainFactory">Orleans grain factory (injected).</param>
    /// <param name="repository">Benchmark repository for DB persistence (injected).</param>
    /// <param name="algorithmIds">
    /// Cipher algorithm IDs to benchmark.
    /// <c>null</c> or empty runs all algorithms in the catalogue.
    /// </param>
    /// <param name="authId">
    /// Optional authenticator to layer on top of every cipher.
    /// <c>null</c> skips MAC measurement.
    /// </param>
    /// <param name="messageCount">
    /// Number of messages to measure per algorithm after warmup.
    /// Minimum 1; recommended ≥ 20 for stable percentiles.
    /// </param>
    /// <param name="messageSizeBytes">
    /// Plaintext payload size in bytes. Clamped to each cipher's
    /// <c>MaxMessageBytes</c> limit automatically.
    /// </param>
    /// <param name="warmupCount">
    /// Warm-up iterations before measurement begins. Defaults to 3.
    /// </param>
    /// <param name="signalRHubUrl">
    /// When set, a <see cref="BenchmarkSignalRClient"/> connects to this URL and
    /// performs one echo per measured ciphertext, recording SignalR round-trip
    /// latency.
    /// </param>
    /// <param name="tlsVersion">
    /// TLS protocol version used by the <see cref="BenchmarkSignalRClient"/>.
    /// Only relevant when <paramref name="signalRHubUrl"/> is set.
    /// </param>
    /// <param name="tokenProvider">
    /// JWT token factory for SignalR hub authentication.
    /// Only required when <paramref name="signalRHubUrl"/> is set and the hub
    /// uses <c>[Authorize]</c>.
    /// </param>
    /// <param name="notes">Optional free-text annotation stored on every run header.</param>
    /// <returns>
    /// An anonymous result object containing per-algorithm summaries, the list
    /// of saved run IDs, and optional SignalR latency statistics.
    /// </returns>
    public static async Task<object> Execute(
        ILogger logger,
        IGrainFactory grainFactory,
        IBenchmarkRepository repository,
        IResourceCaptureService? resourceCapture = null,
        IBenchmarkResourceRepository? resourceRepository = null,
        IReadOnlyList<string>? algorithmIds = null,
        IReadOnlyList<string?> authIds = null!,
        int messageCount = 20,
        int messageSizeBytes = 256,
        int warmupCount = 3,
        string? signalRHubUrl = null,
        BenchmarkTlsVersion tlsVersion = BenchmarkTlsVersion.SystemDefault,
        Func<Task<string?>>? tokenProvider = null,
        int repeatCount = 1,
        string? notes = null,
        string? customMessage = null)
    {
        var idsToRun = (algorithmIds is { Count: > 0 })
            ? algorithmIds
            : AlgorithmCatalog.AllCipherIds.ToList();

        // Normalise authIds: null/empty → single null entry (run without MAC).
        IReadOnlyList<string?> effectiveAuthIds = authIds is { Count: > 0 }
            ? authIds
            : new List<string?> { null };

        // Pre-encode custom message once; null means use random bytes per run.
        byte[]? fixedPayload = !string.IsNullOrEmpty(customMessage)
            ? System.Text.Encoding.UTF8.GetBytes(customMessage)
            : null;

        var runSummaries = new List<object>();
        var errors = new List<string>();

        BenchmarkSignalRClient? signalRClient = null;
        bool signalRConnected = false;

        // ── Connect SignalR client once (shared across all algorithm runs) ────
        if (!string.IsNullOrWhiteSpace(signalRHubUrl))
        {
            signalRClient = new BenchmarkSignalRClient();
            try
            {
                await signalRClient.ConnectAsync(
                    signalRHubUrl,
                    tlsVersion,
                    tokenProvider,
                    bypassCertificateValidation: true);  // dev cert bypass
                signalRConnected = true;
                logger.LogInformation(
                    "[EncryptionBenchmarkDemo] SignalR client connected to {Url} (TLS={Tls})",
                    signalRHubUrl, tlsVersion);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "[EncryptionBenchmarkDemo] SignalR connection failed — skipping network latency measurement.");
                await signalRClient.DisposeAsync();
                signalRClient = null;
            }
        }

        try
        {
            for (int repeat = 0; repeat < repeatCount; repeat++)
            {
                foreach (var algId in idsToRun)
                {
                    AlgorithmInfo info;
                    try { info = AlgorithmCatalog.GetCipherInfo(algId); }
                    catch (ArgumentException ex)
                    {
                        errors.Add($"{algId}: {ex.Message}");
                        logger.LogWarning("[EncryptionBenchmarkDemo] Skipping unknown algorithm: {AlgId}", algId);
                        continue;
                    }

                    // Determine the natural size the caller intended: the custom payload length
                    // (if any), or the requested messageSizeBytes.
                    // Only clamp when the cipher itself cannot handle that size (non-hybrid RSA).
                    int rawSize = fixedPayload is not null ? fixedPayload.Length : messageSizeBytes;
                    bool clampedByCipherLimit = rawSize > info.MaxMessageBytes;
                    int effectiveMessageSize = clampedByCipherLimit ? info.MaxMessageBytes : rawSize;

                    foreach (var authId in effectiveAuthIds)
                    {
                        var runId = Guid.NewGuid();
                        logger.LogInformation(
                            "[EncryptionBenchmarkDemo] Starting run {RunId} — algorithm={AlgId} auth={AuthId} " +
                            "messages={Count} size={Size}B warmup={Warmup}",
                            runId, algId, authId ?? "(none)", messageCount, effectiveMessageSize, warmupCount);

                        try
                        {
                            // ── 1. Configure grain ────────────────────────────────────
                            var grain = grainFactory.GetGrain<IBenchmarkSessionGrain>(runId.ToString());
                            await grain.ConfigureAsync(algId, authId);

                            // ── 2. Generate payloads (random or fixed custom message) ─────
                            var messages = GenerateMessages(messageCount + warmupCount, effectiveMessageSize, fixedPayload);

                            // ── 3. Run in-process cipher benchmark ───────────────────
                            // Force a full blocking GC before opening the capture window so the
                            // memory baseline is at its lowest and not inflated by previous runs.
                            if (resourceCapture is not null)
                            {
                                GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                                GC.WaitForPendingFinalizers();
                                GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                            }
                            resourceCapture?.StartCapture(runId);
                            var snapshots = await grain.RunBenchmarkAsync(messages, warmupCount);
                            var resourceSummary = resourceCapture?.StopCapture(runId);

                            // ── 4. Save run header ────────────────────────────────────
                            var runEntity = new BenchmarkRunDaM
                            {
                                BenchmarkRunId = runId,
                                RunAt = DateTime.UtcNow,
                                AlgorithmId = info.AlgorithmId,
                                AlgorithmFamily = info.AlgorithmFamily,
                                Generation = info.Generation,
                                AuthId = authId,
                                MessageCount = snapshots.Count,
                                MessageSizeBytes = effectiveMessageSize,
                                WarmupCount = warmupCount,
                                TlsVersion = tlsVersion.ToString(),
                                Notes = BuildNotes(notes, clampedByCipherLimit, rawSize, effectiveMessageSize, tlsVersion, signalRConnected, fixedPayload is not null)
                            };
                            await repository.SaveRunAsync(runEntity);

                            // ── 5. Map and save per-message results ───────────────────
                            var messageResults = snapshots
                                .Select((snap, i) => MapToMessageResult(snap, runId, i))
                                .ToList();
                            await repository.AddMessageResultsAsync(messageResults);

                            // ── 6. Compute and save aggregate ─────────────────────────
                            var aggregate = ComputeAggregate(runId, snapshots);
                            await repository.SaveAggregateAsync(aggregate);

                            // ── 6b. Persist resource monitor capture ──────────────────
                            if (resourceSummary is not null && resourceRepository is not null)
                            {
                                var resourceSamples = resourceSummary.Samples
                                    .Select(s => new BenchmarkResourceSampleDaM
                                    {
                                        BenchmarkResourceSampleId = Guid.NewGuid(),
                                        BenchmarkRunId = runId,
                                        CapturedAt = s.Timestamp,
                                        CpuPercent = s.CpuPercent,
                                        MemoryUsedMb = s.MemoryUsedMb,
                                        GcHeapMb = s.GcHeapMb,
                                        GcGen0Collections = s.GcGen0Collections,
                                        GcGen1Collections = s.GcGen1Collections,
                                        GcGen2Collections = s.GcGen2Collections,
                                        ThreadPoolWorkerThreads = s.ThreadPoolWorkerThreads
                                    })
                                    .ToList();
                                await resourceRepository.AddSamplesAsync(resourceSamples);

                                var resourceAggregate = new BenchmarkResourceAggregateDaM
                                {
                                    BenchmarkResourceAggregateId = Guid.NewGuid(),
                                    BenchmarkRunId = runId,
                                    SampleCount = resourceSummary.SampleCount,
                                    DurationMilliseconds = resourceSummary.DurationMilliseconds,
                                    CpuMax = resourceSummary.CpuMax,
                                    CpuAvg = resourceSummary.CpuAvg,
                                    CpuP50 = resourceSummary.CpuP50,
                                    CpuP95 = resourceSummary.CpuP95,
                                    CpuP99 = resourceSummary.CpuP99,
                                    MemoryMaxMb = resourceSummary.MemoryMaxMb,
                                    MemoryAvgMb = resourceSummary.MemoryAvgMb,
                                    MemoryP50Mb = resourceSummary.MemoryP50Mb,
                                    MemoryP95Mb = resourceSummary.MemoryP95Mb,
                                    MemoryP99Mb = resourceSummary.MemoryP99Mb,
                                    GcGen0Delta = resourceSummary.GcGen0Delta,
                                    GcGen1Delta = resourceSummary.GcGen1Delta,
                                    GcGen2Delta = resourceSummary.GcGen2Delta,
                                    ThreadPoolMaxWorkers = resourceSummary.ThreadPoolMaxWorkers
                                };
                                await resourceRepository.SaveAggregateAsync(resourceAggregate);
                            }

                            // ── 7. Optional: SignalR echo measurement ─────────────────
                            object? signalRStats = null;
                            if (signalRClient is not null && signalRConnected)
                            {
                                signalRStats = await RunSignalREchoAsync(
                                    signalRClient, snapshots, logger, algId);
                            }

                            runSummaries.Add(new
                            {
                                RepeatIndex = repeat,
                                RunId = runId,
                                AlgorithmId = info.AlgorithmId,
                                AlgorithmFamily = info.AlgorithmFamily,
                                Generation = info.Generation,
                                AuthId = authId,
                                MessageCount = snapshots.Count,
                                MessageSizeBytes = effectiveMessageSize,
                                AvgEncryptMicroseconds = aggregate.AvgEncryptMicroseconds,
                                AvgDecryptMicroseconds = aggregate.AvgDecryptMicroseconds,
                                AvgSignMicroseconds = aggregate.AvgSignMicroseconds,
                                AvgVerifyMicroseconds = aggregate.AvgVerifyMicroseconds,
                                AvgRoundTripMicroseconds = aggregate.AvgTotalRoundTripMicroseconds,
                                P50RoundTripMicroseconds = aggregate.P50RoundTripMicroseconds,
                                P95RoundTripMicroseconds = aggregate.P95RoundTripMicroseconds,
                                P99RoundTripMicroseconds = aggregate.P99RoundTripMicroseconds,
                                AvgTotalWireBytes = aggregate.AvgTotalWireBytes,
                                AvgEncryptionOverheadBytes = aggregate.AvgEncryptionOverheadBytes,
                                TotalGcAllocatedBytes = aggregate.TotalGcAllocatedBytes,
                                SuccessRate = aggregate.SuccessRate,
                                SignalR = signalRStats,
                                Resources = resourceSummary is null ? null : new
                                {
                                    resourceSummary.SampleCount,
                                    resourceSummary.DurationMilliseconds,
                                    resourceSummary.CpuMax,
                                    resourceSummary.CpuAvg,
                                    resourceSummary.CpuP50,
                                    resourceSummary.CpuP95,
                                    resourceSummary.CpuP99,
                                    resourceSummary.MemoryMaxMb,
                                    resourceSummary.MemoryAvgMb,
                                    resourceSummary.MemoryP50Mb,
                                    resourceSummary.MemoryP95Mb,
                                    resourceSummary.MemoryP99Mb,
                                    resourceSummary.GcGen0Delta,
                                    resourceSummary.GcGen1Delta,
                                    resourceSummary.GcGen2Delta,
                                    resourceSummary.ThreadPoolMaxWorkers
                                }
                            });

                            logger.LogInformation(
                                "[EncryptionBenchmarkDemo] Run {RunId} complete — " +
                                "avgRoundTrip={Avg:F1}µs P99={P99:F1}µs successRate={Rate:P0}",
                                runId, aggregate.AvgTotalRoundTripMicroseconds,
                                aggregate.P99RoundTripMicroseconds, aggregate.SuccessRate);
                        }
                        catch (Exception ex)
                        {
                            var msg = $"{algId}/{authId ?? "none"}: {ex.Message}";
                            errors.Add(msg);
                            logger.LogError(ex, "[EncryptionBenchmarkDemo] Run failed for algorithm {AlgId} auth={AuthId}", algId, authId ?? "(none)");
                        }
                    } // end foreach authId
                } // end foreach algId
            } // end for repeat
        }
        finally
        {
            if (signalRClient is not null)
                await signalRClient.DisposeAsync();
        }

        return new
        {
            Message = "Encryption benchmark complete",
            RepeatCount = repeatCount,
            AlgorithmsRun = runSummaries.Count,
            AlgorithmsFailed = errors.Count,
            AuthIds = effectiveAuthIds.Select(a => a ?? "(none)").ToList(),
            CustomMessage = customMessage ?? "(random)",
            MessageCount = messageCount,
            MessageSizeBytes = messageSizeBytes,
            WarmupCount = warmupCount,
            TlsVersion = tlsVersion.ToString(),
            SignalREchoEnabled = signalRConnected,
            Runs = runSummaries,
            Errors = errors
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static IReadOnlyList<byte[]> GenerateMessages(int total, int size, byte[]? fixedPayload = null)
    {
        // Clamp the fixed payload to the effective size (e.g. RSA MaxMessageBytes).
        byte[]? template = fixedPayload is not null
            ? fixedPayload[..Math.Min(fixedPayload.Length, size)]
            : null;

        var messages = new byte[total][];
        for (int i = 0; i < total; i++)
        {
            if (template is not null)
            {
                messages[i] = template[..]; // copy so each message is an independent array
            }
            else
            {
                messages[i] = new byte[size];
                RandomNumberGenerator.Fill(messages[i]);
            }
        }
        return messages;
    }

    private static BenchmarkMessageResultDaM MapToMessageResult(
        MetricsSnapshot snap, Guid runId, int index)
        => new()
        {
            BenchmarkMessageResultId = Guid.NewGuid(),
            BenchmarkRunId = runId,
            MessageIndex = index,
            EncryptMicroseconds = snap.EncryptMicroseconds,
            DecryptMicroseconds = snap.DecryptMicroseconds,
            SignMicroseconds = snap.SignMicroseconds,
            VerifyMicroseconds = snap.VerifyMicroseconds,
            TotalRoundTripMicroseconds = snap.TotalRoundTripMicroseconds,
            PlaintextBytes = snap.PlaintextBytes,
            CiphertextBytes = snap.CiphertextBytes,
            EncapsulatedKeyBytes = snap.EncapsulatedKeyBytes,
            MacTagBytes = snap.MacTagBytes,
            TotalWireBytes = snap.TotalWireBytes,
            EncryptionOverheadBytes = snap.EncryptionOverheadBytes,
            GcAllocatedBytes = snap.GcAllocatedBytes,
            GcGen0Collections = snap.GcGen0Collections,
            DecryptSuccess = snap.DecryptSuccess,
            VerifySuccess = snap.VerifySuccess
        };

    private static BenchmarkSessionAggregateDaM ComputeAggregate(
        Guid runId,
        IReadOnlyList<MetricsSnapshot> snapshots)
    {
        int n = snapshots.Count;

        var roundTrips = snapshots
            .Select(s => s.TotalRoundTripMicroseconds)
            .OrderBy(v => v)
            .ToArray();

        int successCount = snapshots.Count(s => s.DecryptSuccess && s.VerifySuccess);

        return new BenchmarkSessionAggregateDaM
        {
            BenchmarkSessionAggregateId = Guid.NewGuid(),
            BenchmarkRunId = runId,

            AvgEncryptMicroseconds = Average(snapshots, s => s.EncryptMicroseconds),
            AvgDecryptMicroseconds = Average(snapshots, s => s.DecryptMicroseconds),
            AvgSignMicroseconds = Average(snapshots, s => s.SignMicroseconds),
            AvgVerifyMicroseconds = Average(snapshots, s => s.VerifyMicroseconds),
            AvgTotalRoundTripMicroseconds = Average(snapshots, s => s.TotalRoundTripMicroseconds),

            MinRoundTripMicroseconds = roundTrips[0],
            MaxRoundTripMicroseconds = roundTrips[n - 1],
            P50RoundTripMicroseconds = Percentile(roundTrips, 0.50),
            P95RoundTripMicroseconds = Percentile(roundTrips, 0.95),
            P99RoundTripMicroseconds = Percentile(roundTrips, 0.99),

            AvgCiphertextBytes = Average(snapshots, s => s.CiphertextBytes),
            AvgEncapsulatedKeyBytes = Average(snapshots, s => s.EncapsulatedKeyBytes),
            AvgMacTagBytes = Average(snapshots, s => s.MacTagBytes),
            AvgTotalWireBytes = Average(snapshots, s => s.TotalWireBytes),
            AvgEncryptionOverheadBytes = Average(snapshots, s => s.EncryptionOverheadBytes),

            TotalGcAllocatedBytes = snapshots.Sum(s => s.GcAllocatedBytes),
            AvgGcAllocatedBytesPerMessage = Average(snapshots, s => s.GcAllocatedBytes),
            TotalGcGen0Collections = snapshots.Sum(s => s.GcGen0Collections),

            SuccessfulMessages = successCount,
            SuccessRate = n > 0 ? (double)successCount / n : 0.0
        };
    }

    private static async Task<object> RunSignalREchoAsync(
        BenchmarkSignalRClient client,
        IReadOnlyList<MetricsSnapshot> snapshots,
        ILogger logger,
        string algId)
    {
        var roundTripMicroseconds = new List<double>(snapshots.Count);

        for (int i = 0; i < snapshots.Count; i++)
        {
            try
            {
                // Produce a base64 string of the same byte length as the encrypted
                // wire payload so the SignalR frame size is realistic.
                var fakeWireBytes = new byte[snapshots[i].TotalWireBytes > 0
                    ? snapshots[i].TotalWireBytes
                    : snapshots[i].PlaintextBytes];
                RandomNumberGenerator.Fill(fakeWireBytes);
                var payload = Convert.ToBase64String(fakeWireBytes);

                var result = await client.SendEchoAsync(payload);
                roundTripMicroseconds.Add(result.RoundTripMicroseconds);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "[EncryptionBenchmarkDemo] SignalR echo {Index} failed for {AlgId}", i, algId);
            }
        }

        if (roundTripMicroseconds.Count == 0)
            return new { Error = "All SignalR echo calls failed." };

        var sorted = roundTripMicroseconds.OrderBy(v => v).ToArray();

        return new
        {
            EchoCount = sorted.Length,
            AvgMicroseconds = sorted.Average(),
            MinMicroseconds = sorted[0],
            MaxMicroseconds = sorted[^1],
            P50Microseconds = Percentile(sorted, 0.50),
            P95Microseconds = Percentile(sorted, 0.95),
            P99Microseconds = Percentile(sorted, 0.99)
        };
    }

    // ── Statistics helpers ────────────────────────────────────────────────────

    private static double Average<T>(IReadOnlyList<T> source, Func<T, double> selector)
        => source.Count > 0 ? source.Sum(selector) / source.Count : 0.0;

    /// <summary>
    /// Nearest-rank percentile over a pre-sorted array.
    /// Returns 0 for empty arrays.
    /// </summary>
    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return 0.0;
        if (sorted.Length == 1) return sorted[0];
        int index = (int)Math.Ceiling(p * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static string BuildNotes(
        string? userNotes,
        bool clampedByCipherLimit,
        int rawSize,
        int effectiveSize,
        BenchmarkTlsVersion tls,
        bool signalREnabled,
        bool customPayload = false)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(userNotes))
            parts.Add(userNotes);

        if (clampedByCipherLimit)
            parts.Add($"payload clamped {rawSize}→{effectiveSize}B (cipher limit)");

        if (customPayload)
            parts.Add("custom-payload");

        if (signalREnabled)
            parts.Add($"SignalR echo TLS={tls}");

        return parts.Count > 0 ? string.Join("; ", parts) : "";
    }
}
