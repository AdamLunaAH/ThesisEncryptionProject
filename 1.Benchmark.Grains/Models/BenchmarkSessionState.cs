using Orleans;

namespace Benchmark.Grains.Models;

/// <summary>
/// Persistent state for a <c>BenchmarkSessionGrain</c>.
/// Stores the algorithm selection made at session-start so that key material
/// can be reconstructed if the grain is reactivated from memory storage.
/// </summary>
[GenerateSerializer]
[Alias("Orleans.Grains.Models.BenchmarkSessionState")]
public class BenchmarkSessionState
{
    /// <summary>
    /// Matches the primary key of the grain (a <c>Guid.ToString()</c> run ID).
    /// </summary>
    [Id(0)] public string RunId { get; set; } = "";

    /// <summary>
    /// AlgorithmId selected for this session (see <c>AlgorithmCatalog.AlgorithmIds</c>).
    /// Empty string means the grain has not been configured yet.
    /// </summary>
    [Id(1)] public string AlgorithmId { get; set; } = "";

    /// <summary>
    /// Optional authenticator ID selected for this session
    /// (see <c>AlgorithmCatalog.AuthIds</c>). Null means no MAC.
    /// </summary>
    [Id(2)] public string? AuthId { get; set; }

    /// <summary>True once <c>ConfigureAsync</c> has been called successfully.</summary>
    [Id(3)] public bool IsConfigured { get; set; }
}
