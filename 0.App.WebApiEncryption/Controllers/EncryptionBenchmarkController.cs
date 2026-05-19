using Benchmark.Client.Services;
using CrossCut.Concerns.Monitor;
using DataAccess.Repositories.Database.Benchmark;
using Domain.Services.Encryptions.Benchmark;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Orleans;
using Slices.UseCases;
using System.Globalization;
using System.Text;

namespace App.WebApiEncryption.Controllers;

// Controller that exposes the encryption benchmark pipeline as HTTP endpoints.
// Calling /run triggers EncryptionBenchmarkDemo.Execute, which exercises every
// requested cipher + optional MAC through Orleans grains and persists results to
// the benchmark schema in PostgreSQL.
// Calling /runs returns stored run headers; /runs/{id} returns the full run with
// per-message details and aggregate statistics.
[ApiController]
[Route("api/[controller]")]
// [Authorize(Roles = "Admin,SystemAdmin")]
public class EncryptionBenchmarkController : ControllerBase
{
    private readonly ILogger<EncryptionBenchmarkController> _logger;
    private readonly IGrainFactory _grainFactory;
    private readonly IBenchmarkRepository _repository;
    private readonly IBenchmarkPayloadRepository _payloadRepository;
    private readonly IBenchmarkResourceRepository _resourceRepository;
    private readonly IResourceCaptureService _captureService;
    private readonly IOptions<ChatSettings> _chatSettings;
    private readonly IOptions<BenchmarkPayloadOptions> _payloadOptions;

    public EncryptionBenchmarkController(
        ILogger<EncryptionBenchmarkController> logger,
        IGrainFactory grainFactory,
        IBenchmarkRepository repository,
        IBenchmarkPayloadRepository payloadRepository,
        IBenchmarkResourceRepository resourceRepository,
        IResourceCaptureService captureService,
        IOptions<ChatSettings> chatSettings,
        IOptions<BenchmarkPayloadOptions> payloadOptions)
    {
        _logger = logger;
        _grainFactory = grainFactory;
        _repository = repository;
        _payloadRepository = payloadRepository;
        _resourceRepository = resourceRepository;
        _captureService = captureService;
        _chatSettings = chatSettings;
        _payloadOptions = payloadOptions;
    }

    /// <summary>
    /// Run the encryption benchmark for one or more algorithms and persist the results.
    /// When <c>savePayloads=true</c> the raw ciphertext bytes are also persisted
    /// to the database and to disk for size-comparison analysis.
    /// </summary>
    /// <param name="options">Benchmark configuration passed as query parameters.</param>
    [HttpGet("run")]
    public async Task<IActionResult> Run([FromQuery] BenchmarkOptions options)
    {
        // Build the algorithm list — empty/null means "all".
        var algorithmIds = options.Algorithms?.Count > 0
            ? options.Algorithms
            : null;

        // Merge AuthId (singular, backward compat) and AuthIds (plural, new).
        // Result is a list of string? where null means "no MAC layer".
        var effectiveAuthIds = new List<string?>();
        if (options.AuthIds?.Count > 0)
            effectiveAuthIds.AddRange(options.AuthIds.Cast<string?>());
        if (!string.IsNullOrEmpty(options.AuthId) && !effectiveAuthIds.Contains(options.AuthId))
            effectiveAuthIds.Add(options.AuthId);
        if (effectiveAuthIds.Count == 0)
            effectiveAuthIds.Add(null); // no MAC — preserve legacy behaviour

        // Resolve hub URL: prefer explicit query param, fall back to appsettings Chat.HubUrl.
        var hubUrl = !string.IsNullOrWhiteSpace(options.SignalRHubUrl)
            ? options.SignalRHubUrl
            : _chatSettings.Value.HubUrl;

        if (options.UseSignalRTransport)
        {
            // SignalR transport wins for routing; payload saving is an additive option
            // that can be combined with SignalR by passing the payload repository/root.
            var signalRResult = await EncryptionBenchmarkSignalRDemo.Execute(
                logger: _logger,
                grainFactory: _grainFactory,
                repository: _repository,
                signalRHubUrl: hubUrl,
                resourceCapture: _captureService,
                resourceRepository: _resourceRepository,
                payloadRepository: options.SavePayloads ? _payloadRepository : null,
                storageRoot: options.SavePayloads ? _payloadOptions.Value.ResolvedStorageRoot : null,
                algorithmIds: algorithmIds,
                authIds: effectiveAuthIds,
                messageCount: options.MessageCount,
                messageSizeBytes: options.MessageSizeBytes,
                warmupCount: options.WarmupCount,
                tlsVersion: options.TlsVersion,
                customMessage: options.CustomMessage,
                notes: options.Notes,
                repeatCount: options.RepeatCount);

            return Content(
                JsonConvert.SerializeObject(signalRResult, Formatting.Indented),
                "application/json");
        }

        if (options.SavePayloads)
        {
            var storageRoot = _payloadOptions.Value.ResolvedStorageRoot;
            var result = await EncryptionBenchmarkPayloadDemo.Execute(
                logger: _logger,
                grainFactory: _grainFactory,
                metricsRepository: _repository,
                payloadRepository: _payloadRepository,
                storageRoot: storageRoot,
                resourceCapture: _captureService,
                resourceRepository: _resourceRepository,
                algorithmIds: algorithmIds,
                authIds: effectiveAuthIds,
                messageCount: options.MessageCount,
                messageSizeBytes: options.MessageSizeBytes,
                warmupCount: options.WarmupCount,
                tlsVersion: options.TlsVersion,
                customMessage: options.CustomMessage,
                notes: options.Notes,
                repeatCount: options.RepeatCount);

            return Content(
                JsonConvert.SerializeObject(result, Formatting.Indented),
                "application/json");
        }

        // Standard in-process benchmark (optionally with SignalR echo side-channel).
        var echoUrl = options.IncludeSignalREcho ? hubUrl : null;

        var standardResult = await EncryptionBenchmarkDemo.Execute(
            logger: _logger,
            grainFactory: _grainFactory,
            repository: _repository,
            resourceCapture: _captureService,
            resourceRepository: _resourceRepository,
            algorithmIds: algorithmIds,
            authIds: effectiveAuthIds,
            messageCount: options.MessageCount,
            messageSizeBytes: options.MessageSizeBytes,
            warmupCount: options.WarmupCount,
            signalRHubUrl: echoUrl,
            tlsVersion: options.TlsVersion,
            customMessage: options.CustomMessage,
            notes: options.Notes,
            repeatCount: options.RepeatCount);

        return Content(
            JsonConvert.SerializeObject(standardResult, Formatting.Indented),
            "application/json");
    }

    /// <summary>
    /// Returns all stored benchmark run headers ordered by run date descending.
    /// </summary>
    [HttpGet("runs")]
    public async Task<IActionResult> GetRuns()
    {
        var runs = await _repository.GetRunsAsync();

        var result = runs.Select(r => new
        {
            r.BenchmarkRunId,
            r.RunAt,
            r.AlgorithmId,
            r.AlgorithmFamily,
            r.Generation,
            r.AuthId,
            r.MessageCount,
            r.MessageSizeBytes,
            r.WarmupCount,
            r.TlsVersion,
            r.Notes
        });

        return Ok(new { Count = runs.Count, Runs = result });
    }

    /// <summary>
    /// Returns a single benchmark run with all per-message results and aggregate statistics.
    /// </summary>
    /// <param name="id">The run GUID.</param>
    [HttpGet("runs/{id:guid}")]
    public async Task<IActionResult> GetRun(Guid id)
    {
        var run = await _repository.GetRunWithDetailsAsync(id);
        if (run is null)
            return NotFound(new { error = $"Benchmark run '{id}' not found." });

        return Content(
            JsonConvert.SerializeObject(run, Formatting.Indented),
            "application/json");
    }

    /// <summary>
    /// Returns all valid algorithm IDs that can be passed to <c>algorithms</c>.
    /// </summary>
    [HttpGet("algorithms")]
    public IActionResult GetAlgorithms()
    {
        var ciphers = AlgorithmCatalog.AllCipherIds.Select(id =>
        {
            var info = AlgorithmCatalog.GetCipherInfo(id);
            return new
            {
                info.AlgorithmId,
                info.AlgorithmFamily,
                info.Generation,
                info.KeySizeBits,
                info.IsHybrid,
                info.MaxMessageBytes,
                info.RequiresThirdPartyLibrary,
                info.LibraryName,
                info.Notes
            };
        });

        var authenticators = AlgorithmCatalog.AllAuthIds.Select(id =>
        {
            var info = AlgorithmCatalog.GetAuthInfo(id);
            return new
            {
                info.AuthId,
                info.Generation,
                info.TagSizeBytes,
                info.Notes
            };
        });

        return Ok(new { Ciphers = ciphers, Authenticators = authenticators });
    }

    // ── Payload / storage endpoints ───────────────────────────────────────────

    /// <summary>
    /// Returns metadata for all encrypted payloads stored for a specific run.
    /// Binary columns (Ciphertext, EncapsulatedKey, MacTag) are NOT included
    /// in the response to keep it lightweight.
    /// </summary>
    /// <param name="id">The benchmark run GUID.</param>
    [HttpGet("runs/{id:guid}/payloads")]
    public async Task<IActionResult> GetPayloads(Guid id)
    {
        var payloads = await _payloadRepository.GetPayloadsByRunAsync(id);
        if (payloads.Count == 0)
            return NotFound(new { error = $"No payloads found for run '{id}'." });

        var result = payloads.Select(p => new
        {
            p.BenchmarkMessagePayloadId,
            p.BenchmarkRunId,
            p.MessageIndex,
            p.AlgorithmId,
            p.PlaintextBytes,
            p.TotalWireBytes,
            OverheadBytes = p.TotalWireBytes - p.PlaintextBytes,
            CiphertextBytes = p.Ciphertext.Length,
            EncapsulatedKeyBytes = p.EncapsulatedKey?.Length ?? 0,
            MacTagBytes = p.MacTag?.Length ?? 0,
            p.FilePath
        });

        return Ok(new { RunId = id, Count = payloads.Count, Payloads = result });
    }

    /// <summary>
    /// Returns payload metadata grouped and summarised per algorithm, filtered
    /// to a specific algorithm ID.
    /// </summary>
    /// <param name="algorithmId">Cipher algorithm ID, e.g. <c>AES-256-GCM</c>.</param>
    [HttpGet("payloads/by-algorithm")]
    public async Task<IActionResult> GetPayloadsByAlgorithm([FromQuery] string algorithmId)
    {
        if (string.IsNullOrWhiteSpace(algorithmId))
            return BadRequest(new { error = "algorithmId query parameter is required." });

        var payloads = await _payloadRepository.GetPayloadMetadataByAlgorithmAsync(algorithmId);
        if (payloads.Count == 0)
            return NotFound(new { error = $"No payloads found for algorithm '{algorithmId}'." });

        return Ok(new
        {
            AlgorithmId = algorithmId,
            TotalMessages = payloads.Count,
            AvgPlaintextBytes = payloads.Average(p => p.PlaintextBytes),
            AvgTotalWireBytes = payloads.Average(p => p.TotalWireBytes),
            AvgOverheadBytes = payloads.Average(p => p.TotalWireBytes - p.PlaintextBytes),
            Payloads = payloads.Select(p => new
            {
                p.BenchmarkMessagePayloadId,
                p.BenchmarkRunId,
                p.MessageIndex,
                p.PlaintextBytes,
                p.TotalWireBytes,
                p.FilePath
            })
        });
    }

    /// <summary>
    /// Uses <see cref="DirectoryInfo"/> and <see cref="FileInfo"/> to report the
    /// on-disk size of the payload files for a given run, broken down by algorithm.
    /// </summary>
    /// <param name="id">The benchmark run GUID.</param>
    [HttpGet("runs/{id:guid}/storage")]
    public IActionResult GetRunStorage(Guid id)
    {
        var storageRoot = _payloadOptions.Value.ResolvedStorageRoot;
        var runDir = new DirectoryInfo(Path.Combine(storageRoot, id.ToString()));

        if (!runDir.Exists)
            return NotFound(new { error = $"No storage directory found for run '{id}'." });

        var algorithmBreakdowns = runDir.GetDirectories()
            .Select(algDir =>
            {
                var files = algDir.GetFiles("*.bin");
                long totalBytes = files.Sum(f => f.Length);
                return new
                {
                    AlgorithmId = algDir.Name,
                    FileCount = files.Length,
                    TotalBytes = totalBytes,
                    AvgFileSizeBytes = files.Length > 0 ? (double)totalBytes / files.Length : 0.0,
                    MinFileSizeBytes = files.Length > 0 ? files.Min(f => f.Length) : 0L,
                    MaxFileSizeBytes = files.Length > 0 ? files.Max(f => f.Length) : 0L,
                    DirectoryPath = algDir.FullName
                };
            })
            .ToList();

        return Ok(new
        {
            RunId = id,
            Algorithms = algorithmBreakdowns,
            TotalBytes = algorithmBreakdowns.Sum(a => a.TotalBytes),
            TotalFiles = algorithmBreakdowns.Sum(a => a.FileCount),
            StorageRoot = runDir.FullName
        });
    }

    /// <summary>
    /// Compares on-disk storage sizes across multiple algorithms, aggregated over
    /// all runs that have payload files stored.
    /// Pass <c>?algorithms=AES-256-GCM&amp;algorithms=ML-KEM-768-Hybrid</c>
    /// to compare a subset; omit to compare all algorithm directories found.
    /// </summary>
    [HttpGet("storage/compare")]
    public IActionResult CompareStorage([FromQuery] List<string>? algorithms)
    {
        var storageRoot = new DirectoryInfo(_payloadOptions.Value.ResolvedStorageRoot);

        if (!storageRoot.Exists)
            return Ok(new { Message = "Storage root does not exist yet. Run a payload benchmark first." });

        // Collect all algorithm subdirectories across all run directories.
        var byAlgorithm = storageRoot
            .GetDirectories()            // run-id directories
            .SelectMany(runDir => runDir.GetDirectories())  // algorithm directories
            .GroupBy(algDir => algDir.Name)
            .Where(g => algorithms is not { Count: > 0 } || algorithms.Contains(g.Key))
            .Select(g =>
            {
                var allFiles = g.SelectMany(d => d.GetFiles("*.bin")).ToList();
                long totalBytes = allFiles.Sum(f => f.Length);
                return new
                {
                    AlgorithmId = g.Key,
                    RunCount = g.Count(),
                    TotalFiles = allFiles.Count,
                    TotalBytes = totalBytes,
                    AvgFileSizeBytes = allFiles.Count > 0 ? (double)totalBytes / allFiles.Count : 0.0,
                    MinFileSizeBytes = allFiles.Count > 0 ? allFiles.Min(f => f.Length) : 0L,
                    MaxFileSizeBytes = allFiles.Count > 0 ? allFiles.Max(f => f.Length) : 0L
                };
            })
            .OrderBy(a => a.AlgorithmId)
            .ToList();

        return Ok(new
        {
            AlgorithmCount = byAlgorithm.Count,
            Algorithms = byAlgorithm
        });
    }

    // ── Admin / maintenance ───────────────────────────────────────────────────

    /// <summary>
    /// Exports all benchmark results as a downloadable CSV file.
    /// <para>
    /// With <c>detail=false</c> (default) each row represents one run's
    /// aggregate statistics — run metadata, per-phase timing (µs), wire sizes
    /// (bytes), GC allocation totals, and optional resource-monitoring columns
    /// (CPU %, RAM MB) when a resource capture was active.
    /// </para>
    /// <para>
    /// With <c>detail=true</c> each row represents one measured message,
    /// useful for distribution analysis or scatter plots in Excel.
    /// </para>
    /// </summary>
    /// <param name="detail">
    /// <c>false</c> (default) — one row per run aggregate;<br/>
    /// <c>true</c> — one row per measured message.
    /// </param>
    [HttpGet("export")]
    public async Task<IActionResult> ExportCsv([FromQuery] bool detail = false)
    {
        if (detail)
        {
            var runs = await _repository.GetAllRunsWithMessageResultsAsync();
            var csv = BuildDetailCsv(runs);
            var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
            return File(bytes, "text/csv", "benchmark-message-results.csv");
        }
        else
        {
            var runs = await _repository.GetAllRunsWithAggregatesAsync();
            var resourceAggregates = await _resourceRepository.GetAllAggregatesAsync();
            var csv = BuildSummaryCsv(runs, resourceAggregates);
            var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
            return File(bytes, "text/csv", "benchmark-summary.csv");
        }
    }

    /// <summary>
    /// Exports benchmark results as a CSV file extended with per-run totals.
    /// Each row represents one run and includes:
    /// <list type="bullet">
    ///   <item>All columns from the standard summary export.</item>
    ///   <item><c>PlaintextBytesPerMessage</c> and <c>TotalWireBytesPerMessage</c> — byte sizes for one representative message.</item>
    ///   <item><c>TotalPlaintextBytes</c> and <c>TotalWireBytes</c> — summed across every measured message in the run.</item>
    ///   <item><c>TotalEncryptMicroseconds</c>, <c>TotalDecryptMicroseconds</c>, <c>TotalSignMicroseconds</c>,
    ///         <c>TotalVerifyMicroseconds</c>, <c>TotalRoundTripMicroseconds</c> — summed over all measured messages.</item>
    /// </list>
    /// </summary>
    [HttpGet("export/totals")]
    public async Task<IActionResult> ExportTotalsCsv()
    {
        var runs = await _repository.GetAllRunsWithAggregatesAndMessageResultsAsync();
        var resourceAggregates = await _resourceRepository.GetAllAggregatesAsync();
        var csv = BuildTotalsCsv(runs, resourceAggregates);
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        return File(bytes, "text/csv", "benchmark-totals.csv");
    }

    /// <summary>
    /// Exports a combined CSV where each row is the cross-run average for all runs
    /// that share the same setup: algorithm, authenticator, TLS version, message size,
    /// message count, and warmup count.
    /// <para>
    /// Timing and size columns hold the average-of-per-run-averages.
    /// <c>AvgMin</c> / <c>AvgMax</c> / <c>AvgP50</c> etc. are the arithmetic mean of
    /// the corresponding percentile across contributing runs.
    /// <c>OverallMin</c> and <c>OverallMax</c> are the true bounds seen across all runs.
    /// </para>
    /// </summary>
    [HttpGet("export/combined")]
    public async Task<IActionResult> ExportCombinedCsv()
    {
        var runs = await _repository.GetAllRunsWithAggregatesAsync();
        var resourceAggregates = await _resourceRepository.GetAllAggregatesAsync();
        var csv = BuildCombinedCsv(runs, resourceAggregates);
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        return File(bytes, "text/csv", "benchmark-combined.csv");
    }

    // ── CSV helpers ───────────────────────────────────────────────────────────

    private static string BuildSummaryCsv(
        IReadOnlyList<DataAccess.Models.Database.Benchmark.BenchmarkRunDaM> runs,
        IReadOnlyDictionary<Guid, DataAccess.Models.Database.Benchmark.BenchmarkResourceAggregateDaM> resourceAggregates)
    {
        var sb = new StringBuilder();

        sb.AppendLine(CsvRow(
            "RunNumber", "AlgorithmRunNumber", "RunAt", "AlgorithmId", "AlgorithmFamily",
            "Generation", "AuthId", "TlsVersion", "MessageCount", "MessageSizeBytes", "WarmupCount", "Notes",
            // Cipher timing (µs)
            "AvgEncryptMicroseconds", "AvgDecryptMicroseconds",
            "AvgSignMicroseconds", "AvgVerifyMicroseconds",
            "AvgSignalRTransitMicroseconds", "AvgTotalRoundTripMicroseconds",
            "MinRoundTripMicroseconds", "P50RoundTripMicroseconds",
            "P95RoundTripMicroseconds", "P99RoundTripMicroseconds", "MaxRoundTripMicroseconds",
            // Wire sizes (bytes)
            "AvgCiphertextBytes", "AvgEncapsulatedKeyBytes",
            "AvgTotalWireBytes", "AvgEncryptionOverheadBytes",
            // GC
            "TotalGcAllocatedBytes", "AvgGcAllocatedBytesPerMessage", "TotalGcGen0Collections",
            // Correctness
            "SuccessfulMessages", "SuccessRate",
            // Resource monitoring (prefixed Res_)
            "Res_CpuAvg", "Res_CpuMax", "Res_CpuP50", "Res_CpuP95", "Res_CpuP99",
            "Res_MemAvgMb", "Res_MemMaxMb", "Res_MemP50Mb", "Res_MemP95Mb", "Res_MemP99Mb",
            "Res_DurationMilliseconds", "Res_GcGen0Delta", "Res_GcGen1Delta", "Res_GcGen2Delta",
            "Res_ThreadPoolMaxWorkers", "Res_SampleCount"));

        foreach (var run in runs)
        {
            var a = run.Aggregate;
            resourceAggregates.TryGetValue(run.BenchmarkRunId, out var r);

            sb.AppendLine(CsvRow(
                run.RunNumber, run.AlgorithmRunNumber, run.RunAt, run.AlgorithmId, run.AlgorithmFamily,
                run.Generation, run.AuthId, run.TlsVersion, run.MessageCount, run.MessageSizeBytes, run.WarmupCount, run.Notes,
                a?.AvgEncryptMicroseconds, a?.AvgDecryptMicroseconds,
                a?.AvgSignMicroseconds, a?.AvgVerifyMicroseconds,
                a?.AvgSignalRTransitMicroseconds, a?.AvgTotalRoundTripMicroseconds,
                a?.MinRoundTripMicroseconds, a?.P50RoundTripMicroseconds,
                a?.P95RoundTripMicroseconds, a?.P99RoundTripMicroseconds, a?.MaxRoundTripMicroseconds,
                a?.AvgCiphertextBytes, a?.AvgEncapsulatedKeyBytes,
                a?.AvgTotalWireBytes, a?.AvgEncryptionOverheadBytes,
                a?.TotalGcAllocatedBytes, a?.AvgGcAllocatedBytesPerMessage, a?.TotalGcGen0Collections,
                a?.SuccessfulMessages, a?.SuccessRate,
                r?.CpuAvg, r?.CpuMax, r?.CpuP50, r?.CpuP95, r?.CpuP99,
                r?.MemoryAvgMb, r?.MemoryMaxMb, r?.MemoryP50Mb, r?.MemoryP95Mb, r?.MemoryP99Mb,
                r?.DurationMilliseconds, r?.GcGen0Delta, r?.GcGen1Delta, r?.GcGen2Delta,
                r?.ThreadPoolMaxWorkers, r?.SampleCount));
        }

        return sb.ToString();
    }

    private static string BuildDetailCsv(
        IReadOnlyList<DataAccess.Models.Database.Benchmark.BenchmarkRunDaM> runs)
    {
        var sb = new StringBuilder();

        sb.AppendLine(CsvRow(
            "RunNumber", "AlgorithmRunNumber", "RunAt", "AlgorithmId", "AuthId", "TlsVersion",
            "MessageIndex",
            "EncryptMicroseconds", "DecryptMicroseconds",
            "SignMicroseconds", "VerifyMicroseconds",
            "SignalRTransitMicroseconds", "TotalRoundTripMicroseconds",
            "PlaintextBytes", "CiphertextBytes", "EncapsulatedKeyBytes", "MacTagBytes",
            "TotalWireBytes", "EncryptionOverheadBytes",
            "GcAllocatedBytes", "GcGen0Collections",
            "DecryptSuccess", "VerifySuccess"));

        foreach (var run in runs)
        {
            foreach (var m in run.MessageResults)
            {
                sb.AppendLine(CsvRow(
                    run.RunNumber, run.AlgorithmRunNumber, run.RunAt, run.AlgorithmId, run.AuthId, run.TlsVersion,
                    m.MessageIndex,
                    m.EncryptMicroseconds, m.DecryptMicroseconds,
                    m.SignMicroseconds, m.VerifyMicroseconds,
                    m.SignalRTransitMicroseconds, m.TotalRoundTripMicroseconds,
                    m.PlaintextBytes, m.CiphertextBytes, m.EncapsulatedKeyBytes, m.MacTagBytes,
                    m.TotalWireBytes, m.EncryptionOverheadBytes,
                    m.GcAllocatedBytes, m.GcGen0Collections,
                    m.DecryptSuccess, m.VerifySuccess));
            }
        }

        return sb.ToString();
    }

    private static string BuildTotalsCsv(
        IReadOnlyList<DataAccess.Models.Database.Benchmark.BenchmarkRunDaM> runs,
        IReadOnlyDictionary<Guid, DataAccess.Models.Database.Benchmark.BenchmarkResourceAggregateDaM> resourceAggregates)
    {
        var sb = new StringBuilder();

        sb.AppendLine(CsvRow(
            "RunNumber", "AlgorithmRunNumber", "RunAt", "AlgorithmId", "AlgorithmFamily",
            "Generation", "AuthId", "TlsVersion", "MessageCount", "MessageSizeBytes", "WarmupCount", "Notes",
            // Single-message sizes
            "PlaintextBytesPerMessage", "TotalWireBytesPerMessage",
            // Run totals — bytes
            "TotalPlaintextBytes", "TotalWireBytes",
            // Avg cipher timing (µs)
            "AvgEncryptMicroseconds", "AvgDecryptMicroseconds",
            "AvgSignMicroseconds", "AvgVerifyMicroseconds",
            "AvgSignalRTransitMicroseconds", "AvgTotalRoundTripMicroseconds",
            // Run totals — timing (µs)
            "TotalEncryptMicroseconds", "TotalDecryptMicroseconds",
            "TotalSignMicroseconds", "TotalVerifyMicroseconds",
            "TotalRoundTripMicroseconds",
            // Round-trip percentiles
            "MinRoundTripMicroseconds", "P50RoundTripMicroseconds",
            "P95RoundTripMicroseconds", "P99RoundTripMicroseconds", "MaxRoundTripMicroseconds",
            // Wire sizes (bytes)
            "AvgCiphertextBytes", "AvgEncapsulatedKeyBytes",
            "AvgTotalWireBytes", "AvgEncryptionOverheadBytes",
            // GC
            "TotalGcAllocatedBytes", "AvgGcAllocatedBytesPerMessage", "TotalGcGen0Collections",
            // Correctness
            "SuccessfulMessages", "SuccessRate",
            // Resource monitoring (prefixed Res_)
            "Res_CpuAvg", "Res_CpuMax", "Res_CpuP50", "Res_CpuP95", "Res_CpuP99",
            "Res_MemAvgMb", "Res_MemMaxMb", "Res_MemP50Mb", "Res_MemP95Mb", "Res_MemP99Mb",
            "Res_DurationMilliseconds", "Res_GcGen0Delta", "Res_GcGen1Delta", "Res_GcGen2Delta",
            "Res_ThreadPoolMaxWorkers", "Res_SampleCount"));

        foreach (var run in runs)
        {
            var a = run.Aggregate;
            var msgs = run.MessageResults;
            resourceAggregates.TryGetValue(run.BenchmarkRunId, out var r);

            var first = msgs.Count > 0 ? msgs[0] : null;
            var totalPlaintext = msgs.Count > 0 ? (long)msgs.Sum(m => m.PlaintextBytes) : 0L;
            var totalWire = msgs.Count > 0 ? (long)msgs.Sum(m => m.TotalWireBytes) : 0L;
            var totalEncrypt = msgs.Count > 0 ? msgs.Sum(m => m.EncryptMicroseconds) : 0d;
            var totalDecrypt = msgs.Count > 0 ? msgs.Sum(m => m.DecryptMicroseconds) : 0d;
            var totalSign = msgs.Count > 0 ? msgs.Sum(m => m.SignMicroseconds) : 0d;
            var totalVerify = msgs.Count > 0 ? msgs.Sum(m => m.VerifyMicroseconds) : 0d;
            var totalRoundTrip = msgs.Count > 0 ? msgs.Sum(m => m.TotalRoundTripMicroseconds) : 0d;

            sb.AppendLine(CsvRow(
                run.RunNumber, run.AlgorithmRunNumber, run.RunAt, run.AlgorithmId, run.AlgorithmFamily,
                run.Generation, run.AuthId, run.TlsVersion, run.MessageCount, run.MessageSizeBytes, run.WarmupCount, run.Notes,
                first?.PlaintextBytes, first?.TotalWireBytes,
                totalPlaintext, totalWire,
                a?.AvgEncryptMicroseconds, a?.AvgDecryptMicroseconds,
                a?.AvgSignMicroseconds, a?.AvgVerifyMicroseconds,
                a?.AvgSignalRTransitMicroseconds, a?.AvgTotalRoundTripMicroseconds,
                totalEncrypt, totalDecrypt, totalSign, totalVerify, totalRoundTrip,
                a?.MinRoundTripMicroseconds, a?.P50RoundTripMicroseconds,
                a?.P95RoundTripMicroseconds, a?.P99RoundTripMicroseconds, a?.MaxRoundTripMicroseconds,
                a?.AvgCiphertextBytes, a?.AvgEncapsulatedKeyBytes,
                a?.AvgTotalWireBytes, a?.AvgEncryptionOverheadBytes,
                a?.TotalGcAllocatedBytes, a?.AvgGcAllocatedBytesPerMessage, a?.TotalGcGen0Collections,
                a?.SuccessfulMessages, a?.SuccessRate,
                r?.CpuAvg, r?.CpuMax, r?.CpuP50, r?.CpuP95, r?.CpuP99,
                r?.MemoryAvgMb, r?.MemoryMaxMb, r?.MemoryP50Mb, r?.MemoryP95Mb, r?.MemoryP99Mb,
                r?.DurationMilliseconds, r?.GcGen0Delta, r?.GcGen1Delta, r?.GcGen2Delta,
                r?.ThreadPoolMaxWorkers, r?.SampleCount));
        }

        return sb.ToString();
    }

    private static string BuildCombinedCsv(
        IReadOnlyList<DataAccess.Models.Database.Benchmark.BenchmarkRunDaM> runs,
        IReadOnlyDictionary<Guid, DataAccess.Models.Database.Benchmark.BenchmarkResourceAggregateDaM> resourceAggregates)
    {
        var sb = new StringBuilder();

        sb.AppendLine(CsvRow(
            "AlgorithmId", "AlgorithmFamily", "Generation", "AuthId", "TlsVersion",
            "MessageCount", "MessageSizeBytes", "WarmupCount",
            "RunCount", "TotalMessageCount", "FirstRunAt", "LastRunAt",
            // Timing averages (µs) — average of per-run averages
            "AvgEncryptMicroseconds", "AvgDecryptMicroseconds",
            "AvgSignMicroseconds", "AvgVerifyMicroseconds",
            "AvgSignalRTransitMicroseconds", "AvgTotalRoundTripMicroseconds",
            // Round-trip distribution — average of per-run percentiles
            "AvgMinRoundTripMicroseconds", "AvgP50RoundTripMicroseconds",
            "AvgP95RoundTripMicroseconds", "AvgP99RoundTripMicroseconds", "AvgMaxRoundTripMicroseconds",
            // True bounds across all contributing runs
            "OverallMinRoundTripMicroseconds", "OverallMaxRoundTripMicroseconds",
            // Wire sizes (bytes)
            "AvgCiphertextBytes", "AvgEncapsulatedKeyBytes", "AvgMacTagBytes",
            "AvgTotalWireBytes", "AvgEncryptionOverheadBytes",
            // GC
            "AvgTotalGcAllocatedBytes", "AvgGcAllocatedBytesPerMessage", "AvgTotalGcGen0Collections",
            // Correctness
            "AvgSuccessRate",
            // Resource monitoring — average of per-run aggregates
            "Res_AvgCpuAvg", "Res_AvgCpuMax", "Res_AvgCpuP50", "Res_AvgCpuP95", "Res_AvgCpuP99",
            "Res_AvgMemAvgMb", "Res_AvgMemMaxMb", "Res_AvgMemP50Mb", "Res_AvgMemP95Mb", "Res_AvgMemP99Mb",
            "Res_AvgDurationMilliseconds", "Res_AvgGcGen0Delta", "Res_AvgGcGen1Delta", "Res_AvgGcGen2Delta",
            "Res_AvgThreadPoolMaxWorkers", "Res_AvgSampleCount", "Res_RunsWithData"));

        var groups = runs
            .GroupBy(r => (
                r.AlgorithmId,
                AuthId: r.AuthId ?? "",
                TlsVersion: r.TlsVersion ?? "",
                r.MessageSizeBytes,
                r.WarmupCount,
                r.MessageCount))
            .OrderBy(g => g.Key.AlgorithmId)
            .ThenBy(g => g.Key.AuthId)
            .ThenBy(g => g.Key.TlsVersion)
            .ThenBy(g => g.Key.MessageSizeBytes);

        foreach (var group in groups)
        {
            var groupRuns = group.ToList();
            int n = groupRuns.Count;

            var aggs = groupRuns
                .Select(r => r.Aggregate)
                .Where(a => a is not null)
                .Select(a => a!)
                .ToList();

            var resAggs = groupRuns
                .Select(r => resourceAggregates.TryGetValue(r.BenchmarkRunId, out var ra) ? ra : null)
                .Where(ra => ra is not null)
                .Select(ra => ra!)
                .ToList();

            // Use the earliest run as the representative for family/generation fields.
            var rep = groupRuns.OrderBy(r => r.RunAt).First();

            double? AggAvg(Func<DataAccess.Models.Database.Benchmark.BenchmarkSessionAggregateDaM, double> f)
                => aggs.Count == 0 ? null : aggs.Average(a => f(a));

            double? ResAvg(Func<DataAccess.Models.Database.Benchmark.BenchmarkResourceAggregateDaM, double> f)
                => resAggs.Count == 0 ? null : resAggs.Average(r => f(r));

            sb.AppendLine(CsvRow(
                rep.AlgorithmId, rep.AlgorithmFamily, rep.Generation,
                string.IsNullOrEmpty(group.Key.AuthId) ? null : (object?)group.Key.AuthId,
                string.IsNullOrEmpty(group.Key.TlsVersion) ? null : (object?)group.Key.TlsVersion,
                group.Key.MessageCount, group.Key.MessageSizeBytes, group.Key.WarmupCount,
                n, n * group.Key.MessageCount,
                groupRuns.Min(r => r.RunAt),
                groupRuns.Max(r => r.RunAt),
                // Timing
                AggAvg(a => a.AvgEncryptMicroseconds), AggAvg(a => a.AvgDecryptMicroseconds),
                AggAvg(a => a.AvgSignMicroseconds), AggAvg(a => a.AvgVerifyMicroseconds),
                AggAvg(a => a.AvgSignalRTransitMicroseconds), AggAvg(a => a.AvgTotalRoundTripMicroseconds),
                // Distribution
                AggAvg(a => a.MinRoundTripMicroseconds), AggAvg(a => a.P50RoundTripMicroseconds),
                AggAvg(a => a.P95RoundTripMicroseconds), AggAvg(a => a.P99RoundTripMicroseconds), AggAvg(a => a.MaxRoundTripMicroseconds),
                aggs.Count == 0 ? null : (double?)aggs.Min(a => a.MinRoundTripMicroseconds),
                aggs.Count == 0 ? null : (double?)aggs.Max(a => a.MaxRoundTripMicroseconds),
                // Wire sizes
                AggAvg(a => a.AvgCiphertextBytes), AggAvg(a => a.AvgEncapsulatedKeyBytes), AggAvg(a => a.AvgMacTagBytes),
                AggAvg(a => a.AvgTotalWireBytes), AggAvg(a => a.AvgEncryptionOverheadBytes),
                // GC
                aggs.Count == 0 ? null : (double?)aggs.Average(a => (double)a.TotalGcAllocatedBytes),
                AggAvg(a => a.AvgGcAllocatedBytesPerMessage),
                aggs.Count == 0 ? null : (double?)aggs.Average(a => (double)a.TotalGcGen0Collections),
                // Correctness
                AggAvg(a => a.SuccessRate),
                // Resource
                ResAvg(r => r.CpuAvg), ResAvg(r => r.CpuMax), ResAvg(r => r.CpuP50), ResAvg(r => r.CpuP95), ResAvg(r => r.CpuP99),
                ResAvg(r => r.MemoryAvgMb), ResAvg(r => r.MemoryMaxMb), ResAvg(r => r.MemoryP50Mb), ResAvg(r => r.MemoryP95Mb), ResAvg(r => r.MemoryP99Mb),
                ResAvg(r => r.DurationMilliseconds),
                resAggs.Count == 0 ? null : (double?)resAggs.Average(r => (double)r.GcGen0Delta),
                resAggs.Count == 0 ? null : (double?)resAggs.Average(r => (double)r.GcGen1Delta),
                resAggs.Count == 0 ? null : (double?)resAggs.Average(r => (double)r.GcGen2Delta),
                resAggs.Count == 0 ? null : (double?)resAggs.Average(r => (double)r.ThreadPoolMaxWorkers),
                resAggs.Count == 0 ? null : (double?)resAggs.Average(r => (double)r.SampleCount),
                resAggs.Count));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats a row of values as a Swedish-locale CSV row.
    /// Column delimiter is <c>;</c> (semicolon) because the decimal separator
    /// in Swedish formatting is <c>,</c> — using comma as both decimal mark and
    /// field separator would break every spreadsheet tool.
    /// Doubles use <c>sv-SE</c> culture so Excel and other Swedish tools parse
    /// them as numbers without manual locale conversion.
    /// Strings containing semicolons, double-quotes, or newlines are wrapped in
    /// double-quotes with internal quotes escaped as <c>""</c>.
    /// </summary>
    private static string CsvRow(params object?[] values)
        => string.Join(";", values.Select(CsvEscape));

    private static readonly CultureInfo _svSE =
        new CultureInfo("sv-SE");

    private static string CsvEscape(object? value)
    {
        if (value is null) return "";
        var str = value switch
        {
            double d => d.ToString("0.##########", _svSE),
            float f => f.ToString("0.##########", _svSE),
            DateTime dt => dt.ToString("o"),
            bool b => b ? "TRUE" : "FALSE",
            _ => value.ToString() ?? ""
        };
        return str.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0
            ? '"' + str.Replace("\"", "\"\"") + '"'
            : str;
    }

    // ── Admin / maintenance ───────────────────────────────────────────────────

    /// <summary>
    /// Deletes all rows from every benchmark table (payloads → message results
    /// → aggregates → runs). Returns the number of rows deleted.
    /// </summary>
    [HttpDelete("data")]
    public async Task<IActionResult> ClearData()
    {
        var deleted = await _repository.ClearAllAsync();
        return Ok(new { Message = "All benchmark data cleared.", RowsDeleted = deleted });
    }
}
