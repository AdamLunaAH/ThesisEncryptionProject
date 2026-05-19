using Microsoft.AspNetCore.SignalR;

namespace App.WebApiEncryption.Hubs;

/// <summary>
/// Minimal SignalR hub used exclusively by the encryption benchmark to measure
/// end-to-end TLS round-trip latency.
///
/// The hub is intentionally unauthenticated and stateless: it does nothing except
/// echo the caller's payload back so the benchmark client can measure the
/// network/TLS overhead independent of encryption work.
/// </summary>
public class BenchmarkHub : Hub
{
    /// <summary>
    /// Receives a Base64-encoded payload and immediately echoes it back to the
    /// caller as <c>BenchmarkEchoResponse</c>.
    ///
    /// The <see cref="Benchmark.Client.Services.BenchmarkSignalRClient"/> times the gap
    /// between its <c>SendAsync</c> call returning and this response firing to
    /// capture per-message TLS + SignalR overhead in microseconds.
    /// </summary>
    /// <param name="payload">Base64-encoded ciphertext (or any opaque byte sequence).</param>
    public async Task BenchmarkEcho(string payload)
    {
        await Clients.Caller.SendAsync("BenchmarkEchoResponse", payload);
    }
}
