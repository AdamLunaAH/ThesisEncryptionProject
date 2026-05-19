using DataAccess.Models.Database.Benchmark;

namespace DataAccess.Repositories.Database.Benchmark;

/// <summary>
/// Persistence contract for the resource-monitor data captured during benchmark runs.
/// </summary>
public interface IBenchmarkResourceRepository
{
    /// <summary>
    /// Bulk-inserts all per-tick resource samples for a run in a single round-trip.
    /// </summary>
    Task AddSamplesAsync(IEnumerable<BenchmarkResourceSampleDaM> samples);

    /// <summary>
    /// Saves the pre-computed resource aggregate for a run.
    /// Overwrites any existing aggregate for the same <c>BenchmarkRunId</c>.
    /// </summary>
    Task SaveAggregateAsync(BenchmarkResourceAggregateDaM aggregate);

    /// <summary>
    /// Returns all resource aggregates keyed by <c>BenchmarkRunId</c>.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, BenchmarkResourceAggregateDaM>> GetAllAggregatesAsync();

    /// <summary>
    /// Deletes all rows from the resource sample and aggregate tables.
    /// Returns the total number of rows deleted.
    /// </summary>
    Task<int> ClearAllAsync();
}
