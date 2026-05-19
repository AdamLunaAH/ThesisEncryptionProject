namespace EncryptionMonitor;

/// <summary>
/// Copy of the server-side model — kept in sync manually.
/// Uses the same property names and order so SignalR JSON deserialisation works
/// without any custom converters.
/// </summary>
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
