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
/// Slice that runs an encryption benchmark, persists the raw ciphertext bytes
/// to both the database (<c>benchmark.BenchmarkMessagePayload</c>) and to a
/// directory on disk so that file-system size analysis via
/// <see cref="DirectoryInfo"/>/<see cref="FileInfo"/> is possible.
///
/// The on-disk layout is:
/// <code>
///   {storageRoot}/{runId}/{algorithmId}/{index:D3}.bin
/// </code>
/// Each file contains the concatenated wire bytes:
/// <c>Ciphertext || EncapsulatedKey || MacTag</c>.
///
/// Metrics (timing, sizes, aggregate) are also saved via
/// <see cref="IBenchmarkRepository"/> for consistency with the standard
/// benchmark path.
/// </summary>
public static class EncryptionBenchmarkPayloadDemo
{
    /// <summary>
    /// Executes the payload benchmark for every requested algorithm.
    /// </summary>
    public static async Task<object> Execute(
        ILogger logger,
        IGrainFactory grainFactory,
        IBenchmarkRepository metricsRepository,
        IBenchmarkPayloadRepository payloadRepository,
        string storageRoot,
        IResourceCaptureService? resourceCapture = null,
        IBenchmarkResourceRepository? resourceRepository = null,
        IReadOnlyList<string>? algorithmIds = null,
        IReadOnlyList<string?> authIds = null!,
        int messageCount = 10,
        int messageSizeBytes = 256,
        int warmupCount = 3,
        BenchmarkTlsVersion tlsVersion = BenchmarkTlsVersion.SystemDefault,
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

        for (int repeat = 0; repeat < repeatCount; repeat++)
        {
            foreach (var algId in idsToRun)
            {
                AlgorithmInfo info;
                try { info = AlgorithmCatalog.GetCipherInfo(algId); }
                catch (ArgumentException ex)
                {
                    errors.Add($"{algId}: {ex.Message}");
                    logger.LogWarning("[PayloadDemo] Skipping unknown algorithm: {AlgId}", algId);
                    continue;
                }

                int rawSize = fixedPayload is not null ? fixedPayload.Length : messageSizeBytes;
                bool clampedByCipherLimit = rawSize > info.MaxMessageBytes;
                int effectiveSize = clampedByCipherLimit ? info.MaxMessageBytes : rawSize;

                foreach (var authId in effectiveAuthIds)
                {
                    var runId = Guid.NewGuid();

                    logger.LogInformation(
                        "[PayloadDemo] Starting run {RunId} — algorithm={AlgId} auth={AuthId} messages={Count} size={Size}B",
                        runId, algId, authId ?? "(none)", messageCount, effectiveSize);

                    try
                    {
                        // ── 1. Configure grain ────────────────────────────────────────
                        var grain = grainFactory.GetGrain<IBenchmarkPayloadGrain>(runId.ToString());
                        await grain.ConfigureAsync(algId, authId);

                        // ── 2. Generate payloads (random or fixed custom message) ─────
                        var messages = GenerateMessages(messageCount + warmupCount, effectiveSize, fixedPayload);

                        // ── 3. Run benchmark — returns metrics + raw ciphertext bytes ─
                        // Force a full blocking GC before opening the capture window so the
                        // memory baseline is at its lowest and not inflated by previous runs.
                        if (resourceCapture is not null)
                        {
                            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                            GC.WaitForPendingFinalizers();
                            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                        }
                        resourceCapture?.StartCapture(runId);
                        var captures = await grain.RunAndCaptureAsync(messages, warmupCount);
                        var resourceSummary = resourceCapture?.StopCapture(runId);

                        // ── 4. Save run header (metrics) ──────────────────────────────
                        var runEntity = new BenchmarkRunDaM
                        {
                            BenchmarkRunId = runId,
                            RunAt = DateTime.UtcNow,
                            AlgorithmId = info.AlgorithmId,
                            AlgorithmFamily = info.AlgorithmFamily,
                            Generation = info.Generation,
                            AuthId = authId,
                            MessageCount = captures.Count,
                            MessageSizeBytes = effectiveSize,
                            WarmupCount = warmupCount,
                            TlsVersion = tlsVersion.ToString(),
                            Notes = BuildNotes(notes, clampedByCipherLimit, rawSize, effectiveSize, fixedPayload is not null)
                        };
                        await metricsRepository.SaveRunAsync(runEntity);

                        // ── 5. Save per-message metrics ───────────────────────────────
                        var messageResults = captures
                            .Select((c, i) => MapToMessageResult(c.Metrics, runId, i))
                            .ToList();
                        await metricsRepository.AddMessageResultsAsync(messageResults);

                        // ── 6. Save aggregate ─────────────────────────────────────────
                        var snapshots = captures.Select(c => c.Metrics).ToList();
                        var aggregate = ComputeAggregate(runId, snapshots);
                        await metricsRepository.SaveAggregateAsync(aggregate);

                        // ── 6b. Persist resource monitor capture ──────────────────────
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

                        // ── 7. Write payload files to disk ────────────────────────────
                        var runDir = Path.Combine(
                            storageRoot,
                            runId.ToString(),
                            SanitisePathSegment(algId));
                        Directory.CreateDirectory(runDir);

                        var payloadEntities = new List<BenchmarkMessagePayloadDaM>(captures.Count);

                        for (int i = 0; i < captures.Count; i++)
                        {
                            var cap = captures[i];
                            var fileName = $"{i:D3}.bin";
                            var fullPath = Path.Combine(runDir, fileName);
                            var relPath = Path.Combine(runId.ToString(), SanitisePathSegment(algId), fileName);

                            // Wire format: ciphertext || encapsulated-key || mac-tag
                            await WriteWireBytesAsync(fullPath, cap);

                            payloadEntities.Add(new BenchmarkMessagePayloadDaM
                            {
                                BenchmarkMessagePayloadId = Guid.NewGuid(),
                                BenchmarkRunId = runId,
                                MessageIndex = i,
                                AlgorithmId = info.AlgorithmId,
                                Ciphertext = cap.Ciphertext,
                                EncapsulatedKey = cap.EncapsulatedKey,
                                MacTag = cap.MacTag,
                                PlaintextBytes = cap.Metrics.PlaintextBytes,
                                TotalWireBytes = cap.Metrics.TotalWireBytes,
                                FilePath = relPath
                            });
                        }

                        // ── 8. Save payload rows to DB ────────────────────────────────
                        await payloadRepository.SavePayloadsAsync(payloadEntities);

                        // ── 9. Compute directory size via FileInfo ────────────────────
                        var dirInfo = new DirectoryInfo(runDir);
                        long dirBytes = dirInfo.GetFiles("*.bin").Sum(f => f.Length);

                        runSummaries.Add(new
                        {
                            RepeatIndex = repeat,
                            RunId = runId,
                            AlgorithmId = info.AlgorithmId,
                            AlgorithmFamily = info.AlgorithmFamily,
                            Generation = info.Generation,
                            MessageCount = captures.Count,
                            PlaintextBytes = effectiveSize,
                            AvgCiphertextBytes = captures.Average(c => c.Metrics.CiphertextBytes),
                            AvgEncapsulatedKeyBytes = captures.Average(c => c.Metrics.EncapsulatedKeyBytes),
                            AvgTotalWireBytes = captures.Average(c => c.Metrics.TotalWireBytes),
                            AvgOverheadBytes = captures.Average(c => c.Metrics.EncryptionOverheadBytes),
                            DirectoryBytes = dirBytes,
                            DirectoryPath = dirInfo.FullName,
                            FileCount = captures.Count,
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
                            "[PayloadDemo] Run {RunId} complete — {Count} payloads saved, dirBytes={Bytes}",
                            runId, captures.Count, dirBytes);
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{algId}/{authId ?? "none"}: {ex.Message}");
                        logger.LogError(ex, "[PayloadDemo] Run failed for algorithm {AlgId} auth={AuthId}", algId, authId ?? "(none)");
                    }
                } // end foreach authId
            }
        } // end for repeat

        return new
        {
            Message = "Encryption payload benchmark complete",
            RepeatCount = repeatCount,
            AlgorithmsRun = runSummaries.Count,
            AlgorithmsFailed = errors.Count,
            AuthIds = effectiveAuthIds.Select(a => a ?? "(none)").ToList(),
            CustomMessage = customMessage ?? "(random)",
            MessageCount = messageCount,
            MessageSizeBytes = messageSizeBytes,
            WarmupCount = warmupCount,
            StorageRoot = storageRoot,
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

    private static async Task WriteWireBytesAsync(string filePath, PayloadCapture cap)
    {
        // Wire format: ciphertext || encapsulated-key (if any) || mac-tag (if any)
        int total = cap.Ciphertext.Length
                  + (cap.EncapsulatedKey?.Length ?? 0)
                  + (cap.MacTag?.Length ?? 0);

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write,
            FileShare.None, bufferSize: 4096, useAsync: true);

        await fs.WriteAsync(cap.Ciphertext);
        if (cap.EncapsulatedKey is { Length: > 0 })
            await fs.WriteAsync(cap.EncapsulatedKey);
        if (cap.MacTag is { Length: > 0 })
            await fs.WriteAsync(cap.MacTag);

        _ = total; // suppress unused variable warning
    }

    /// <summary>
    /// Replaces characters that are invalid in a directory-name segment with underscores.
    /// </summary>
    private static string SanitisePathSegment(string segment)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return string.Concat(segment.Select(c => invalid.Contains(c) ? '_' : c));
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
        Guid runId, IReadOnlyList<MetricsSnapshot> snapshots)
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
            AvgEncryptMicroseconds = Avg(snapshots, s => s.EncryptMicroseconds),
            AvgDecryptMicroseconds = Avg(snapshots, s => s.DecryptMicroseconds),
            AvgSignMicroseconds = Avg(snapshots, s => s.SignMicroseconds),
            AvgVerifyMicroseconds = Avg(snapshots, s => s.VerifyMicroseconds),
            AvgTotalRoundTripMicroseconds = Avg(snapshots, s => s.TotalRoundTripMicroseconds),
            MinRoundTripMicroseconds = roundTrips[0],
            MaxRoundTripMicroseconds = roundTrips[n - 1],
            P50RoundTripMicroseconds = Percentile(roundTrips, 0.50),
            P95RoundTripMicroseconds = Percentile(roundTrips, 0.95),
            P99RoundTripMicroseconds = Percentile(roundTrips, 0.99),
            AvgCiphertextBytes = Avg(snapshots, s => s.CiphertextBytes),
            AvgEncapsulatedKeyBytes = Avg(snapshots, s => s.EncapsulatedKeyBytes),
            AvgMacTagBytes = Avg(snapshots, s => s.MacTagBytes),
            AvgTotalWireBytes = Avg(snapshots, s => s.TotalWireBytes),
            AvgEncryptionOverheadBytes = Avg(snapshots, s => s.EncryptionOverheadBytes),
            TotalGcAllocatedBytes = snapshots.Sum(s => s.GcAllocatedBytes),
            AvgGcAllocatedBytesPerMessage = Avg(snapshots, s => s.GcAllocatedBytes),
            TotalGcGen0Collections = snapshots.Sum(s => s.GcGen0Collections),
            SuccessfulMessages = successCount,
            SuccessRate = n > 0 ? (double)successCount / n : 0.0
        };
    }

    private static double Avg<T>(IReadOnlyList<T> src, Func<T, double> sel)
        => src.Count > 0 ? src.Sum(sel) / src.Count : 0.0;

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return 0.0;
        if (sorted.Length == 1) return sorted[0];
        int idx = (int)Math.Ceiling(p * sorted.Length) - 1;
        return sorted[Math.Clamp(idx, 0, sorted.Length - 1)];
    }

    private static string BuildNotes(string? userNotes, bool clampedByCipherLimit, int rawSize, int effectiveSize, bool customPayload = false)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(userNotes))
            parts.Add(userNotes);
        if (clampedByCipherLimit)
            parts.Add($"payload clamped {rawSize}\u2192{effectiveSize}B (cipher limit)");
        if (customPayload)
            parts.Add("custom-payload");
        parts.Add("payload-capture run");
        return string.Join("; ", parts);
    }
}
