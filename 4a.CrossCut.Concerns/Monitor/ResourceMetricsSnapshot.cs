namespace CrossCut.Concerns.Monitor;

/// <summary>
/// Point-in-time resource metrics snapshot broadcast by
/// <c>ResourceMonitorBroadcaster</c> over the monitor hub.
/// </summary>
/// <param name="Timestamp">UTC instant the snapshot was captured.</param>
/// <param name="CpuPercent">Process CPU utilisation across all cores, 0–100.</param>
/// <param name="MemoryUsedMb">Resident memory used by the process in megabytes.</param>
/// <param name="MemoryLimitMb">Container / OS memory ceiling in megabytes.</param>
/// <param name="MemoryPercent">MemoryUsedMb / MemoryLimitMb × 100.</param>
/// <param name="GcHeapMb">Managed GC heap size (GC.GetTotalMemory) in megabytes.</param>
/// <param name="GcGen0Collections">Cumulative Gen-0 GC collections since process start.</param>
/// <param name="GcGen1Collections">Cumulative Gen-1 GC collections since process start.</param>
/// <param name="GcGen2Collections">Cumulative Gen-2 GC collections since process start.</param>
/// <param name="ThreadPoolWorkerThreads">Current number of active thread-pool worker threads.</param>
/// <param name="UptimeSeconds">Seconds elapsed since the process started.</param>
/// <param name="SignalRConnectionCount">Total active SignalR connections across all hubs.</param>
/// <param name="OrleansGrainCount">Total active grain activations reported by the cluster.</param>
public record ResourceMetricsSnapshot(
    DateTimeOffset Timestamp,
    double CpuPercent,
    double MemoryUsedMb,
    double MemoryLimitMb,
    double MemoryPercent,
    double GcHeapMb,
    int GcGen0Collections,
    int GcGen1Collections,
    int GcGen2Collections,
    int ThreadPoolWorkerThreads,
    double UptimeSeconds,
    int SignalRConnectionCount,
    long OrleansGrainCount);
