namespace CrossCut.Concerns.Monitor;

/// <summary>
/// Manages per-benchmark-run resource snapshot capture windows.
///
/// The benchmark slice calls <see cref="StartCapture"/> before executing an
/// algorithm and <see cref="StopCapture"/> after it completes. While the window
/// is open, <see cref="FeedSnapshot"/> (called by
/// <c>ResourceMonitorBroadcaster</c> every tick) appends each
/// <see cref="ResourceMetricsSnapshot"/> to every active session.
///
/// Multiple runs may be active concurrently — each gets its own isolated buffer.
/// </summary>
public interface IResourceCaptureService
{
    /// <summary>
    /// Opens a new capture window for <paramref name="runId"/>.
    /// Ignored if a window is already open for the same ID.
    /// </summary>
    void StartCapture(Guid runId);

    /// <summary>
    /// Closes the capture window for <paramref name="runId"/>, computes
    /// aggregate statistics over all collected samples, and returns the result.
    /// Returns <c>null</c> when no window was open for <paramref name="runId"/>
    /// or no samples were collected (e.g. broadcaster interval longer than the run).
    /// </summary>
    ResourceCaptureSummary? StopCapture(Guid runId);

    /// <summary>
    /// Appends <paramref name="snapshot"/> to every currently open capture window.
    /// Called by <c>ResourceMonitorBroadcaster</c> on every tick.
    /// Must be thread-safe (broadcaster runs on a background thread).
    /// </summary>
    void FeedSnapshot(ResourceMetricsSnapshot snapshot);
}
