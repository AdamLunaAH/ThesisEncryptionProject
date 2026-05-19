using Orleans;

namespace Benchmark.Grains.Models;

/// <summary>
/// Persistent state for a <c>BenchmarkPayloadGrain</c>.
/// Mirrors <see cref="BenchmarkSessionState"/> so key material can be
/// reconstructed on grain reactivation.
/// </summary>
[GenerateSerializer]
[Alias("Orleans.Grains.Models.BenchmarkPayloadState")]
public class BenchmarkPayloadState
{
    /// <summary>Matches the primary key of the grain (a <c>Guid.ToString()</c> run ID).</summary>
    [Id(0)] public string RunId { get; set; } = "";

    /// <summary>AlgorithmId selected for this session.</summary>
    [Id(1)] public string AlgorithmId { get; set; } = "";

    /// <summary>Optional authenticator ID. Null means no MAC.</summary>
    [Id(2)] public string? AuthId { get; set; }

    /// <summary>True once <c>ConfigureAsync</c> has been called successfully.</summary>
    [Id(3)] public bool IsConfigured { get; set; }
}
