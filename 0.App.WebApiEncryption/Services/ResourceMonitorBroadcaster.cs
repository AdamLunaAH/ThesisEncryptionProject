using System.Diagnostics;
using App.WebApiEncryption.Hubs;
using CrossCut.Concerns.Monitor;
using Microsoft.AspNetCore.SignalR;
using Orleans;
using Orleans.Runtime;

namespace App.WebApiEncryption.Services;

/// <summary>
/// Background service that samples process and cluster resources every
/// <see cref="BroadcastIntervalMicroS"/> microseconds and broadcasts a
/// <see cref="ResourceMetricsSnapshot"/> to all connected monitor clients.
///
/// All CPU and memory figures are scoped to <em>this process only</em> via
/// <see cref="Process.GetCurrentProcess"/>:
///   • CPU   — delta(TotalProcessorTime) / (wall-clock delta x core count)
///   • Memory — WorkingSet64 (physical RAM pages mapped into this process)
///
/// To change the sampling rate, adjust <see cref="BroadcastIntervalMicroS"/>.
/// </summary>
public sealed class ResourceMonitorBroadcaster : BackgroundService
{
    // How often (in microseconds) to sample metrics and push to clients.
    private const double BroadcastIntervalMicroS = 1000;

    // CPU delta tracking — only accessed sequentially inside ExecuteAsync.
    private TimeSpan _lastCpuTime = TimeSpan.Zero;
    private DateTime _lastMeasureTime = DateTime.UtcNow;

    private readonly IHubContext<MonitorHub> _hubContext;
    private readonly ConnectionTracker _connectionTracker;
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<ResourceMonitorBroadcaster> _logger;
    private readonly IResourceCaptureService _captureService;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    public ResourceMonitorBroadcaster(
        IHubContext<MonitorHub> hubContext,
        ConnectionTracker connectionTracker,
        IGrainFactory grainFactory,
        ILogger<ResourceMonitorBroadcaster> logger,
        IResourceCaptureService captureService)
    {
        _hubContext = hubContext;
        _connectionTracker = connectionTracker;
        _grainFactory = grainFactory;
        _logger = logger;
        _captureService = captureService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ResourceMonitorBroadcaster started (interval: {Interval} µs).",
            BroadcastIntervalMicroS);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var snapshot = await BuildSnapshotAsync();
                _captureService.FeedSnapshot(snapshot);
                await _hubContext.Clients.All.SendAsync("Metrics", snapshot, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ResourceMonitorBroadcaster: error building or sending snapshot.");
            }

            await Task.Delay(TimeSpan.FromMicroseconds(BroadcastIntervalMicroS), stoppingToken);
        }

        _logger.LogInformation("ResourceMonitorBroadcaster stopped.");
    }

    private async Task<ResourceMetricsSnapshot> BuildSnapshotAsync()
    {
        // ── Process CPU ───────────────────────────────────────────────────────
        // CPU% = delta(TotalProcessorTime) / (wall-clock delta x logical core count).
        // Clamped to [0, 100] to guard against timer-resolution jitter on first tick.
        var proc = Process.GetCurrentProcess();
        proc.Refresh();
        var now = DateTime.UtcNow;
        var cpuDelta = proc.TotalProcessorTime - _lastCpuTime;
        var wallDelta = now - _lastMeasureTime;
        var cpuPercent = wallDelta.TotalMilliseconds > 0
            ? Math.Clamp(
                cpuDelta.TotalMilliseconds / (wallDelta.TotalMilliseconds * Environment.ProcessorCount) * 100.0,
                0, 100)
            : 0;
        _lastCpuTime = proc.TotalProcessorTime;
        _lastMeasureTime = now;

        // ── Process memory ────────────────────────────────────────────────────
        // WorkingSet64: physical RAM pages currently mapped into this process.
        // TotalAvailableMemoryBytes: physical RAM visible to this process/container.
        var memUsedMb = proc.WorkingSet64 / 1_048_576.0;
        var gcInfo = GC.GetGCMemoryInfo();
        var memLimitMb = gcInfo.TotalAvailableMemoryBytes > 0
            ? gcInfo.TotalAvailableMemoryBytes / 1_048_576.0
            : memUsedMb;
        var memPercent = memLimitMb > 0 ? memUsedMb / memLimitMb * 100.0 : 0;

        // ── GC ────────────────────────────────────────────────────────────────
        var gcHeapMb = GC.GetTotalMemory(forceFullCollection: false) / 1_048_576.0;
        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);

        // ── Thread pool ───────────────────────────────────────────────────────
        ThreadPool.GetAvailableThreads(out int availableWorkers, out _);
        ThreadPool.GetMaxThreads(out int maxWorkers, out _);
        var activeWorkers = maxWorkers - availableWorkers;

        // ── Uptime ────────────────────────────────────────────────────────────
        var uptime = (DateTimeOffset.UtcNow - _startedAt).TotalSeconds;

        // ── Orleans grain activations ─────────────────────────────────────────
        long grainCount = 0;
        try
        {
            var mgmt = _grainFactory.GetGrain<IManagementGrain>(0);
            var hosts = await mgmt.GetHosts(onlyActive: true);
            if (hosts.Count > 0)
            {
                var stats = await mgmt.GetRuntimeStatistics(hosts.Keys.ToArray());
                grainCount = stats.Sum(s => s.ActivationCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read Orleans grain count.");
        }

        return new ResourceMetricsSnapshot(
            Timestamp: DateTimeOffset.UtcNow,
            CpuPercent: cpuPercent,
            MemoryUsedMb: memUsedMb,
            MemoryLimitMb: memLimitMb,
            MemoryPercent: memPercent,
            GcHeapMb: gcHeapMb,
            GcGen0Collections: gen0,
            GcGen1Collections: gen1,
            GcGen2Collections: gen2,
            ThreadPoolWorkerThreads: activeWorkers,
            UptimeSeconds: uptime,
            SignalRConnectionCount: _connectionTracker.Count,
            OrleansGrainCount: grainCount);
    }
}
