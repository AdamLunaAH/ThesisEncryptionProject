using Microsoft.AspNetCore.SignalR.Client;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;

namespace Benchmark.Client.Services;

/// <summary>
/// TLS protocol version to use when the benchmark client connects to the SignalR hub.
/// Constraining the allowed protocols lets the benchmark compare the per-message
/// round-trip latency under TLS 1.2 versus TLS 1.3.
/// </summary>
public enum BenchmarkTlsVersion
{
    /// <summary>Allow only TLS 1.2.</summary>
    Tls12,
    /// <summary>Allow only TLS 1.3.</summary>
    Tls13,
    /// <summary>Let the OS/runtime negotiate the best available version (default .NET behaviour).</summary>
    SystemDefault
}

/// <summary>
/// Captures the outcome of a single <see cref="BenchmarkSignalRClient.SendEchoAsync"/> call.
/// </summary>
/// <param name="RoundTripMicroseconds">
/// Wall-clock time from the moment <c>SendAsync("BenchmarkEcho")</c> returns to when
/// the <c>BenchmarkEchoResponse</c> handler fires, in microseconds.
/// This includes SignalR serialisation, HTTP framing, TLS record overhead, and server
/// processing time for the echo method — but excludes client-side cipher operations.
/// </param>
/// <param name="PayloadBytes">
/// Number of UTF-8 bytes in the base64-encoded payload string sent on the wire,
/// giving an approximation of the per-message serialized size.
/// </param>
/// <param name="ReceivedPayload">
/// The base64 string echoed back by the hub, identical to the sent payload for a
/// transparent echo. Decode with <c>Convert.FromBase64String</c> to obtain the
/// original wire bytes for decryption in the SignalR-transport benchmark.
/// </param>
public readonly record struct SignalRRoundTripResult(
    double RoundTripMicroseconds,
    int PayloadBytes,
    string ReceivedPayload);

/// <summary>
/// Lightweight SignalR client designed exclusively for benchmark use.
///
/// Unlike <see cref="ChatClientService"/> it carries no session/presence/matchmaking
/// logic. Its only hub interaction is calling <c>BenchmarkEcho</c> and waiting for
/// the server to reflect the payload back as <c>BenchmarkEchoResponse</c>.
///
/// Key capabilities added over the chat client:
/// <list type="bullet">
///   <item>
///     Configurable TLS protocol version (<see cref="BenchmarkTlsVersion"/>) via
///     <see cref="SocketsHttpHandler.SslOptions"/> so TLS 1.2 / 1.3 latency can be
///     compared without changing the cipher under test.
///   </item>
///   <item>
///     High-resolution round-trip measurement using <see cref="Stopwatch.GetTimestamp"/>
///     around the SignalR send/receive cycle so sub-millisecond precision is preserved.
///   </item>
///   <item>
///     Configurable certificate bypass for development self-signed certificates so the
///     client can connect to a local Kestrel instance the same way the chat client does.
///   </item>
/// </list>
///
/// Typical usage from a benchmark slice:
/// <code>
/// await using var client = new BenchmarkSignalRClient();
/// await client.ConnectAsync(hubUrl, BenchmarkTlsVersion.Tls13, tokenProvider);
/// foreach (var payload in base64Payloads)
/// {
///     var result = await client.SendEchoAsync(payload);
///     // result.RoundTripMicroseconds is the network overhead
/// }
/// </code>
/// </summary>
public sealed class BenchmarkSignalRClient : IAsyncDisposable
{
    private HubConnection? _hubConnection;

    // Shared lock to serialise sends so _pendingEcho is never accessed from two threads.
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    // Completion source written by the echo response handler; awaited by SendEchoAsync.
    // Carries both the receive timestamp and the echoed payload string.
    private TaskCompletionSource<(long Timestamp, string Payload)>? _pendingEcho;

    // Timestamp captured immediately before the hub.SendAsync call returns to the awaiter.
    // Using Stopwatch ticks (not DateTime) gives microsecond resolution.
    private long _sendTimestamp;

    // ── Connection ────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the <see cref="HubConnection"/>, configures TLS, and starts the connection.
    /// </summary>
    /// <param name="hubUrl">Full URL of the SignalR hub, e.g. <c>https://localhost:7258/chathub</c>.</param>
    /// <param name="tlsVersion">TLS protocol version to negotiate.</param>
    /// <param name="tokenProvider">
    /// Optional JWT token factory. Required when the hub uses <c>[Authorize]</c>.
    /// Called once per connection/reconnect by the SignalR client internals.
    /// </param>
    /// <param name="bypassCertificateValidation">
    /// When <c>true</c>, skips server certificate validation. Should only be <c>true</c>
    /// in local development with self-signed certificates.
    /// </param>
    public async Task ConnectAsync(
        string hubUrl,
        BenchmarkTlsVersion tlsVersion = BenchmarkTlsVersion.SystemDefault,
        Func<Task<string?>>? tokenProvider = null,
        bool bypassCertificateValidation = false)
    {
        if (_hubConnection is not null)
            throw new InvalidOperationException("Already connected. Call DisposeAsync before reconnecting.");

        var sslOptions = BuildSslOptions(tlsVersion, bypassCertificateValidation);
        var httpHandler = new SocketsHttpHandler { SslOptions = sslOptions };

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => httpHandler;
                if (tokenProvider is not null)
                    options.AccessTokenProvider = tokenProvider;
            })
            .Build();

        // Register the echo response handler once before connecting.
        _hubConnection.On<string>("BenchmarkEchoResponse", payload =>
        {
            // Capture receive timestamp as early as possible in the callback.
            var ts = Stopwatch.GetTimestamp();
            _pendingEcho?.TrySetResult((ts, payload));
        });

        await _hubConnection.StartAsync();
    }

    /// <summary>
    /// Returns <c>true</c> when the underlying hub connection is in the Connected state.
    /// </summary>
    public bool IsConnected =>
        _hubConnection?.State == HubConnectionState.Connected;

    // ── Benchmark ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends <paramref name="base64Payload"/> to the hub's <c>BenchmarkEcho</c> method
    /// and waits for the server to reflect it back as <c>BenchmarkEchoResponse</c>.
    /// Returns timing and size metrics for the round-trip.
    /// </summary>
    /// <param name="base64Payload">
    /// The pre-encrypted message as a base64 string, produced by the benchmark slice.
    /// Using a string keeps the wire format identical to normal chat messages.
    /// </param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when not connected.</exception>
    public async Task<SignalRRoundTripResult> SendEchoAsync(
        string base64Payload,
        CancellationToken cancellationToken = default)
    {
        if (_hubConnection is null || _hubConnection.State != HubConnectionState.Connected)
            throw new InvalidOperationException(
                "BenchmarkSignalRClient is not connected. Call ConnectAsync first.");

        // Serialise sends so the single _pendingEcho field is never contested.
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            _pendingEcho = new TaskCompletionSource<(long Timestamp, string Payload)>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            // Register cancellation so we don't leak the TCS on timeout.
            using var reg = cancellationToken.Register(
                () => _pendingEcho.TrySetCanceled(cancellationToken));

            // Capture send timestamp just before the actual write to the socket.
            _sendTimestamp = Stopwatch.GetTimestamp();
            await _hubConnection.SendAsync("BenchmarkEcho", base64Payload, cancellationToken);

            var (receiveTimestamp, receivedPayload) = await _pendingEcho.Task;

            var elapsedTicks = receiveTimestamp - _sendTimestamp;
            var microseconds = elapsedTicks * 1_000_000.0 / Stopwatch.Frequency;

            return new SignalRRoundTripResult(
                RoundTripMicroseconds: microseconds,
                PayloadBytes: System.Text.Encoding.UTF8.GetByteCount(base64Payload),
                ReceivedPayload: receivedPayload);
        }
        finally
        {
            _pendingEcho = null;
            _sendLock.Release();
        }
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        _sendLock.Dispose();
        if (_hubConnection is not null)
        {
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static SslClientAuthenticationOptions BuildSslOptions(
        BenchmarkTlsVersion tlsVersion,
        bool bypassCertificateValidation)
    {
        var protocols = tlsVersion switch
        {
            BenchmarkTlsVersion.Tls12 => SslProtocols.Tls12,
            BenchmarkTlsVersion.Tls13 => SslProtocols.Tls13,
            _ => SslProtocols.None // let the runtime choose
        };

        var options = new SslClientAuthenticationOptions
        {
            EnabledSslProtocols = protocols
        };

        if (bypassCertificateValidation)
        {
            options.RemoteCertificateValidationCallback =
                (_, _, _, _) => true;
        }

        return options;
    }
}
