using Benchmark.Client.Services;
using Domain.Services.Encryptions.Benchmark;
using Microsoft.AspNetCore.Mvc;

namespace App.WebApiEncryption.Controllers;

/// <summary>
/// Query-string parameters accepted by
/// <c>GET /api/encryptionbenchmark/run</c>.
///
/// All properties have safe defaults so the endpoint can be called with no
/// parameters for a quick sanity-check run (all algorithms, no MAC, 20 × 256B
/// messages).
/// </summary>
public class BenchmarkOptions
{
    /// <summary>
    /// One or more cipher algorithm IDs to benchmark.
    /// Pass the query parameter multiple times to include several algorithms,
    /// e.g. <c>?algorithms=AES-256-GCM&amp;algorithms=ChaCha20-Poly1305</c>.
    /// Omit entirely (or leave empty) to run all algorithms in the catalogue.
    ///
    /// Valid values are listed at <c>GET /api/encryptionbenchmark/algorithms</c>.
    /// </summary>
    [FromQuery(Name = "algorithms")]
    public List<string>? Algorithms { get; set; }

    /// <summary>
    /// Single authenticator ID to layer on top of every cipher (encrypt-then-MAC).
    /// Kept for backward compatibility — prefer <see cref="AuthIds"/> when running
    /// multiple MAC algorithms in one request.
    /// <c>null</c> (default) skips the MAC layer.
    ///
    /// Valid values: HMAC-SHA256, HMAC-SHA512, HMAC-SHA3-256, HMAC-SHA3-512,
    /// KMAC-128, KMAC-256.
    /// </summary>
    [FromQuery(Name = "authId")]
    public string? AuthId { get; set; }

    /// <summary>
    /// One or more authenticator IDs to benchmark in combination with every
    /// selected cipher. Pass the parameter multiple times to test several MACs:
    /// <c>?authIds=HMAC-SHA256&amp;authIds=HMAC-SHA512</c>.
    ///
    /// The benchmark creates one <c>BenchmarkRun</c> per (cipher × authId) pair.
    /// When both <see cref="AuthId"/> and <see cref="AuthIds"/> are provided they
    /// are merged (duplicates removed). When neither is set the run uses no MAC.
    /// </summary>
    [FromQuery(Name = "authIds")]
    public List<string>? AuthIds { get; set; }

    /// <summary>
    /// Optional fixed plaintext to use for every measured message.
    /// When set all messages (including warmup) contain the UTF-8 encoding of
    /// this string instead of random bytes. The content is automatically clamped
    /// to each cipher's <c>MaxMessageBytes</c> limit (relevant for non-hybrid RSA).
    ///
    /// When <c>null</c> (default) each message is filled with a fresh random byte
    /// array of <see cref="MessageSizeBytes"/> bytes.
    /// </summary>
    [FromQuery(Name = "customMessage")]
    public string? CustomMessage { get; set; }

    /// <summary>
    /// Number of messages to measure per algorithm after warmup rounds.
    /// Minimum 1; a value ≥ 20 is recommended for stable P95/P99 percentiles.
    /// Default: 20.
    /// </summary>
    [FromQuery(Name = "messageCount")]
    public int MessageCount { get; set; } = 20;

    /// <summary>
    /// Plaintext payload size in bytes used for every message in the run.
    /// For non-hybrid RSA ciphers the value is automatically clamped to the
    /// cipher's maximum (190B for RSA-2048, 446B for RSA-4096).
    /// Default: 256.
    /// </summary>
    [FromQuery(Name = "messageSizeBytes")]
    public int MessageSizeBytes { get; set; } = 256;

    /// <summary>
    /// Number of warmup iterations executed before measurement begins.
    /// Warmup allows the .NET tiered JIT to reach steady-state, preventing
    /// the first measured message from being an outlier.
    /// Default: 3.
    /// </summary>
    [FromQuery(Name = "warmupCount")]
    public int WarmupCount { get; set; } = 3;

    /// <summary>
    /// When <c>true</c>, connects a <see cref="BenchmarkSignalRClient"/> to
    /// the hub URL configured in <c>appsettings.json</c> (Chat.HubUrl) and
    /// sends one echo per measured message to capture real network round-trip
    /// latency.
    ///
    /// Use <see cref="SignalRHubUrl"/> to override the default hub URL.
    /// Default: false.
    /// </summary>
    [FromQuery(Name = "includeSignalREcho")]
    public bool IncludeSignalREcho { get; set; } = false;

    /// <summary>
    /// Explicit SignalR hub URL for the echo test.
    /// Overrides the <c>Chat.HubUrl</c> from appsettings when set.
    /// Only used when <see cref="IncludeSignalREcho"/> is <c>true</c> or this
    /// property itself is set.
    /// </summary>
    [FromQuery(Name = "signalRHubUrl")]
    public string? SignalRHubUrl { get; set; }

    /// <summary>
    /// TLS protocol version the <see cref="BenchmarkSignalRClient"/> will
    /// negotiate when connecting to the hub.
    ///
    /// Values: <c>Tls12</c>, <c>Tls13</c>, <c>SystemDefault</c> (default).
    /// </summary>
    [FromQuery(Name = "tlsVersion")]
    public BenchmarkTlsVersion TlsVersion { get; set; } = BenchmarkTlsVersion.SystemDefault;

    /// <summary>
    /// Optional free-text annotation stored in every <c>BenchmarkRun</c> row
    /// created by this request. Useful for tagging comparison runs.
    /// </summary>
    [FromQuery(Name = "notes")]
    public string? Notes { get; set; }

    /// <summary>
    /// When <c>true</c>, the raw ciphertext bytes for each measured message are
    /// persisted to the database (<c>benchmark.BenchmarkMessagePayload</c>) and
    /// written as binary files under the configured <c>BenchmarkPayload:StorageRoot</c>
    /// directory. This enables file-system size comparisons between algorithms.
    ///
    /// Default: false — adds no binary I/O to the standard benchmark path.
    /// </summary>
    [FromQuery(Name = "savePayloads")]
    public bool SavePayloads { get; set; } = false;

    /// <summary>
    /// When <c>true</c>, uses SignalR as the actual encrypted-message transport.
    /// Each message is: encrypted → sent via <c>BenchmarkHub.BenchmarkEcho</c> →
    /// echoed back → decrypted. SignalR round-trip time is captured as a separate
    /// timing dimension alongside the cipher phases.
    ///
    /// Requires a reachable BenchmarkHub. Use <see cref="SignalRHubUrl"/> to specify
    /// the hub URL, or rely on the <c>Chat.HubUrl</c> appsettings value.
    ///
    /// Default: false.
    /// </summary>
    [FromQuery(Name = "useSignalRTransport")]
    public bool UseSignalRTransport { get; set; } = false;

    /// <summary>
    /// Number of times to repeat the full benchmark from scratch.
    /// Each repeat creates new run IDs, fresh grain activations, warmup rounds,
    /// and a GC baseline — identical to pressing the send button N times.
    /// Default: 1 (no repetition).
    /// </summary>
    [FromQuery(Name = "repeatCount")]
    public int RepeatCount { get; set; } = 1;
}
