using Benchmark.Client.Services;
using Benchmark.Grains.Interfaces;
using CrossCut.Concerns.Monitor;
using DataAccess.Models.Database.Benchmark;
using DataAccess.Repositories.Database.Benchmark;
using Domain.Services.Encryptions.Benchmark;
using Microsoft.Extensions.Logging;
using Orleans;
using System.IO;
using System.Security.Cryptography;

namespace Slices.UseCases;

/// <summary>
/// Slice that runs the encryption benchmark with SignalR as the actual encrypted-message
/// transport.
///
/// Unlike <see cref="EncryptionBenchmarkDemo"/> (which measures cipher performance
/// in-process), this slice routes every encrypted payload through the SignalR hub so
/// that network overhead is captured as a first-class timing dimension alongside the
/// cipher phases.
///
/// Per-message flow:
/// <list type="number">
///   <item>
///     <c>grain.EncryptMessageAsync(plaintext)</c> — cipher.Encrypt + optional
///     authenticator.Sign are measured inside the grain turn.
///   </item>
///   <item>
///     The resulting wire payload (base64-encoded) is sent to
///     <c>BenchmarkHub.BenchmarkEcho</c> via <see cref="BenchmarkSignalRClient"/>.
///     The hub echoes the payload back unchanged. The client records the wall-clock
///     round-trip as <see cref="MetricsSnapshot.SignalRTransitMicroseconds"/>.
///   </item>
///   <item>
///     <c>grain.DecryptMessageAsync(receivedWireBytes)</c> — cipher.Decrypt + optional
///     authenticator.Verify are measured inside the grain turn.
///   </item>
/// </list>
///
/// <see cref="MetricsSnapshot.TotalRoundTripMicroseconds"/> equals the sum of all
/// three stages plus MAC-verify time, giving a true end-to-end per-message latency.
///
/// Warmup iterations run the encrypt + decrypt grain calls without SignalR to warm
/// up the JIT for cipher code; SignalR is already connected before warmup begins.
/// </summary>
public static class EncryptionBenchmarkSignalRDemo
{
    /// <summary>
    /// Runs the SignalR-transport benchmark and persists results.
    /// </summary>
    /// <param name="logger">Logger from the controller.</param>
    /// <param name="grainFactory">Orleans grain factory (injected).</param>
    /// <param name="repository">Benchmark repository for DB persistence (injected).</param>
    /// <param name="signalRHubUrl">
    /// Full URL of the SignalR hub, e.g. <c>https://localhost:7258/hubs/benchmark</c>.
    /// Required — the slice will fail if this is empty.
    /// </param>
    /// <param name="resourceCapture">Optional resource monitor capture service.</param>
    /// <param name="resourceRepository">
    /// Optional resource repository for persisting CPU/memory samples.
    /// Only used when <paramref name="resourceCapture"/> is also provided.
    /// </param>
    /// <param name="algorithmIds">
    /// Cipher algorithm IDs to benchmark. <c>null</c> or empty runs all algorithms.
    /// </param>
    /// <param name="authId">
    /// Optional MAC authenticator ID layered on every cipher. <c>null</c> skips MAC.
    /// </param>
    /// <param name="messageCount">
    /// Number of messages to measure per algorithm after warmup.
    /// </param>
    /// <param name="messageSizeBytes">
    /// Plaintext payload size in bytes. Clamped to each cipher's
    /// <c>MaxMessageBytes</c> automatically.
    /// </param>
    /// <param name="warmupCount">
    /// Warm-up iterations executed before measurement begins. Defaults to 3.
    /// </param>
    /// <param name="tlsVersion">
    /// TLS protocol version used when connecting the <see cref="BenchmarkSignalRClient"/>.
    /// </param>
    /// <param name="tokenProvider">
    /// JWT token factory for hub authentication. Only required when the hub uses
    /// <c>[Authorize]</c>.
    /// </param>
    /// <param name="notes">Optional free-text annotation stored on every run header.</param>
    public static async Task<object> Execute(
        ILogger logger,
        IGrainFactory grainFactory,
        IBenchmarkRepository repository,
        string signalRHubUrl,
        IResourceCaptureService? resourceCapture = null,
        IBenchmarkResourceRepository? resourceRepository = null,
        IBenchmarkPayloadRepository? payloadRepository = null,
        string? storageRoot = null,
        IReadOnlyList<string>? algorithmIds = null,
        IReadOnlyList<string?> authIds = null!,
        int messageCount = 20,
        int messageSizeBytes = 256,
        int warmupCount = 3,
        BenchmarkTlsVersion tlsVersion = BenchmarkTlsVersion.SystemDefault,
        Func<Task<string?>>? tokenProvider = null,
        int repeatCount = 1,
        string? notes = null,
        string? customMessage = null)
    {
        if (string.IsNullOrWhiteSpace(signalRHubUrl))
            throw new ArgumentException(
                "signalRHubUrl is required for the SignalR-transport benchmark.", nameof(signalRHubUrl));

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

        for (int repeat = 0; repeat < repeatCount; repeat++)
        {
            foreach (var algId in idsToRun)
            {
                AlgorithmInfo info;
                try { info = AlgorithmCatalog.GetCipherInfo(algId); }
                catch (ArgumentException ex)
                {
                    errors.Add($"{algId}: {ex.Message}");
                    logger.LogWarning(
                        "[EncryptionBenchmarkSignalRDemo] Skipping unknown algorithm: {AlgId}", algId);
                    continue;
                }

                int rawSize = fixedPayload is not null ? fixedPayload.Length : messageSizeBytes;
                bool clampedByCipherLimit = rawSize > info.MaxMessageBytes;
                int effectiveMessageSize = clampedByCipherLimit ? info.MaxMessageBytes : rawSize;

                foreach (var authId in effectiveAuthIds)
                {
                    var runId = Guid.NewGuid();

                    logger.LogInformation(
                        "[EncryptionBenchmarkSignalRDemo] Starting run {RunId} — algorithm={AlgId} " +
                        "auth={AuthId} messages={Count} size={Size}B warmup={Warmup}",
                        runId, algId, authId ?? "(none)", messageCount, effectiveMessageSize, warmupCount);

                    // ── Connect a fresh SignalR client for each (algorithm, authId) run ──
                    // A new connection ensures no state leaks between algorithms.
                    await using var signalRClient = new BenchmarkSignalRClient();
                    try
                    {
                        await signalRClient.ConnectAsync(
                            signalRHubUrl,
                            tlsVersion,
                            tokenProvider,
                            bypassCertificateValidation: true);
                        logger.LogInformation(
                            "[EncryptionBenchmarkSignalRDemo] SignalR client connected to {Url} (TLS={Tls}) for {AlgId}",
                            signalRHubUrl, tlsVersion, algId);
                    }
                    catch (Exception ex)
                    {
                        var connMsg = $"{algId}: SignalR connection failed — {ex.Message}";
                        errors.Add(connMsg);
                        logger.LogError(ex,
                            "[EncryptionBenchmarkSignalRDemo] SignalR connection failed for {AlgId} — skipping.",
                            algId);
                        continue;
                    }

                    try
                    {
                        // ── 1. Configure grain ────────────────────────────────────────
                        var grain = grainFactory.GetGrain<IBenchmarkSessionGrain>(runId.ToString());
                        await grain.ConfigureAsync(algId, authId);

                        // ── 2. Generate payloads (random or fixed custom message) ─────
                        var messages = GenerateMessages(messageCount + warmupCount, effectiveMessageSize, fixedPayload);

                        // ── 3. Warmup (cipher JIT only, no SignalR) ───────────────────
                        for (int w = 0; w < warmupCount; w++)
                        {
                            var warmupEnc = await grain.EncryptMessageAsync(messages[w]);
                            await grain.DecryptMessageAsync(warmupEnc.WirePayload);
                        }

                        // ── 4. Measured loop (full SignalR transport) ─────────────────
                        // Force a full blocking GC before opening the capture window so the
                        // memory baseline is at its lowest and not inflated by previous runs.
                        if (resourceCapture is not null)
                        {
                            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                            GC.WaitForPendingFinalizers();
                            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                        }
                        resourceCapture?.StartCapture(runId);

                        var snapshots = new List<MetricsSnapshot>(messageCount);
                        bool savePayloads = payloadRepository is not null && storageRoot is not null;
                        var payloadCaptures = savePayloads
                            ? new List<(byte[] Ciphertext, byte[]? EncapsulatedKey, byte[]? MacTag, int PlaintextBytes, int TotalWireBytes)>(messageCount)
                            : null;

                        for (int i = 0; i < messageCount; i++)
                        {
                            var plaintext = messages[warmupCount + i];

                            // (a) Encrypt + sign in grain
                            var encResult = await grain.EncryptMessageAsync(plaintext);

                            // Decode wire payload to raw byte components for optional payload saving.
                            if (savePayloads)
                            {
                                var (ct, ek, tag) = BenchmarkWireFormat.Decode(encResult.WirePayload);
                                payloadCaptures!.Add((ct, ek, tag, encResult.PlaintextBytes, encResult.TotalWireBytes));
                            }

                            // (b) Send wire payload through SignalR hub and receive echo
                            var base64Payload = Convert.ToBase64String(encResult.WirePayload);
                            var signalRResult = await signalRClient.SendEchoAsync(base64Payload);

                            // (c) Decrypt + verify in grain using the echoed wire bytes
                            var receivedWireBytes = Convert.FromBase64String(signalRResult.ReceivedPayload);
                            var decResult = await grain.DecryptMessageAsync(receivedWireBytes);

                            snapshots.Add(new MetricsSnapshot
                            {
                                EncryptMicroseconds = encResult.EncryptMicroseconds,
                                SignMicroseconds = encResult.SignMicroseconds,
                                SignalRTransitMicroseconds = signalRResult.RoundTripMicroseconds,
                                DecryptMicroseconds = decResult.DecryptMicroseconds,
                                VerifyMicroseconds = decResult.VerifyMicroseconds,
                                PlaintextBytes = encResult.PlaintextBytes,
                                CiphertextBytes = encResult.CiphertextBytes,
                                EncapsulatedKeyBytes = encResult.EncapsulatedKeyBytes,
                                MacTagBytes = encResult.MacTagBytes,
                                GcAllocatedBytes = encResult.GcAllocatedBytes,
                                GcGen0Collections = encResult.GcGen0Collections,
                                DecryptSuccess = decResult.DecryptSuccess,
                                VerifySuccess = decResult.VerifySuccess
                            });
                        }

                        var resourceSummary = resourceCapture?.StopCapture(runId);

                        // Signal the grain to release key material
                        await grain.CompleteSessionAsync();

                        // ── 5. Save run header ────────────────────────────────────────
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
                            Notes = BuildNotes(notes, clampedByCipherLimit, rawSize, effectiveMessageSize, tlsVersion, fixedPayload is not null)
                        };
                        await repository.SaveRunAsync(runEntity);

                        // ── 6. Map and save per-message results ───────────────────────
                        var messageResults = snapshots
                            .Select((snap, i) => MapToMessageResult(snap, runId, i))
                            .ToList();
                        await repository.AddMessageResultsAsync(messageResults);

                        // ── 7. Compute and save aggregate ─────────────────────────────
                        var aggregate = ComputeAggregate(runId, snapshots);
                        await repository.SaveAggregateAsync(aggregate);

                        // ── 7b. Persist resource monitor capture ──────────────────────
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

                        // ── 8. Write payload files and save payload rows (optional) ──
                        if (savePayloads && payloadCaptures is not null)
                        {
                            var runDir = Path.Combine(
                                storageRoot!,
                                runId.ToString(),
                                SanitisePathSegment(algId));
                            Directory.CreateDirectory(runDir);

                            var payloadEntities = new List<BenchmarkMessagePayloadDaM>(payloadCaptures.Count);

                            for (int i = 0; i < payloadCaptures.Count; i++)
                            {
                                var (ct, ek, tag, ptBytes, wireBytes) = payloadCaptures[i];
                                var fileName = $"{i:D3}.bin";
                                var fullPath = Path.Combine(runDir, fileName);
                                var relPath = Path.Combine(runId.ToString(), SanitisePathSegment(algId), fileName);

                                await WritePayloadBytesAsync(fullPath, ct, ek, tag);

                                payloadEntities.Add(new BenchmarkMessagePayloadDaM
                                {
                                    BenchmarkMessagePayloadId = Guid.NewGuid(),
                                    BenchmarkRunId = runId,
                                    MessageIndex = i,
                                    AlgorithmId = info.AlgorithmId,
                                    Ciphertext = ct,
                                    EncapsulatedKey = ek,
                                    MacTag = tag,
                                    PlaintextBytes = ptBytes,
                                    TotalWireBytes = wireBytes,
                                    FilePath = relPath
                                });
                            }

                            await payloadRepository!.SavePayloadsAsync(payloadEntities);

                            logger.LogInformation(
                                "[EncryptionBenchmarkSignalRDemo] Run {RunId} — {Count} payload files saved to {Dir}",
                                runId, payloadCaptures.Count, runDir);
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
                            AvgSignalRTransitMicroseconds = aggregate.AvgSignalRTransitMicroseconds,
                            AvgRoundTripMicroseconds = aggregate.AvgTotalRoundTripMicroseconds,
                            P50RoundTripMicroseconds = aggregate.P50RoundTripMicroseconds,
                            P95RoundTripMicroseconds = aggregate.P95RoundTripMicroseconds,
                            P99RoundTripMicroseconds = aggregate.P99RoundTripMicroseconds,
                            AvgTotalWireBytes = aggregate.AvgTotalWireBytes,
                            AvgEncryptionOverheadBytes = aggregate.AvgEncryptionOverheadBytes,
                            TotalGcAllocatedBytes = aggregate.TotalGcAllocatedBytes,
                            SuccessRate = aggregate.SuccessRate,
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
                            "[EncryptionBenchmarkSignalRDemo] Run {RunId} complete — " +
                            "avgRoundTrip={Avg:F1}µs avgSignalR={SR:F1}µs P99={P99:F1}µs successRate={Rate:P0}",
                            runId,
                            aggregate.AvgTotalRoundTripMicroseconds,
                            aggregate.AvgSignalRTransitMicroseconds,
                            aggregate.P99RoundTripMicroseconds,
                            aggregate.SuccessRate);
                    }
                    catch (Exception ex)
                    {
                        var msg = $"{algId}/{authId ?? "none"}: {ex.Message}";
                        errors.Add(msg);
                        logger.LogError(ex,
                            "[EncryptionBenchmarkSignalRDemo] Run failed for algorithm {AlgId} auth={AuthId}", algId, authId ?? "(none)");
                    }
                } // end foreach authId
            }
        } // end for repeat

        return new
        {
            Message = "SignalR-transport benchmark complete",
            RepeatCount = repeatCount,
            AlgorithmsRun = runSummaries.Count,
            AlgorithmsFailed = errors.Count,
            AuthIds = effectiveAuthIds.Select(a => a ?? "(none)").ToList(),
            CustomMessage = customMessage ?? "(random)",
            MessageCount = messageCount,
            MessageSizeBytes = messageSizeBytes,
            WarmupCount = warmupCount,
            TlsVersion = tlsVersion.ToString(),
            SignalRHubUrl = signalRHubUrl,
            Runs = runSummaries,
            Errors = errors
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static IReadOnlyList<byte[]> GenerateMessages(int total, int size, byte[]? fixedPayload = null)
    {
        byte[]? template = fixedPayload is not null
            ? fixedPayload[..Math.Min(fixedPayload.Length, size)]
            : null;

        var messages = new byte[total][];
        for (int i = 0; i < total; i++)
        {
            if (template is not null)
                messages[i] = template[..]; // copy so each message is an independent array
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
            SignalRTransitMicroseconds = snap.SignalRTransitMicroseconds,
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
            AvgSignalRTransitMicroseconds = Average(snapshots, s => s.SignalRTransitMicroseconds),
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

    private static string BuildNotes(
        string? userNotes,
        bool clampedByCipherLimit,
        int rawSize,
        int effectiveSize,
        BenchmarkTlsVersion tls,
        bool customPayload = false)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(userNotes))
            parts.Add(userNotes);

        if (clampedByCipherLimit)
            parts.Add($"payload clamped {rawSize}\u2192{effectiveSize}B (cipher limit)");

        if (customPayload)
            parts.Add("custom-payload");

        parts.Add($"SignalR-transport TLS={tls}");

        return string.Join("; ", parts);
    }

    private static string SanitisePathSegment(string segment)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return string.Concat(segment.Select(c => invalid.Contains(c) ? '_' : c));
    }

    private static async Task WritePayloadBytesAsync(
        string filePath, byte[] ciphertext, byte[]? encapsulatedKey, byte[]? macTag)
    {
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write,
            FileShare.None, bufferSize: 4096, useAsync: true);
        await fs.WriteAsync(ciphertext);
        if (encapsulatedKey is { Length: > 0 })
            await fs.WriteAsync(encapsulatedKey);
        if (macTag is { Length: > 0 })
            await fs.WriteAsync(macTag);
    }

    private static double Average<T>(IReadOnlyList<T> source, Func<T, double> selector)
        => source.Count > 0 ? source.Sum(selector) / source.Count : 0.0;

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return 0.0;
        if (sorted.Length == 1) return sorted[0];
        int index = (int)Math.Ceiling(p * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}
