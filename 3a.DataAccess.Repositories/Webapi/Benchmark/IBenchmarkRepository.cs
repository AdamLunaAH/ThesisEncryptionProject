using DataAccess.Models.Database.Benchmark;

namespace DataAccess.Repositories.Database.Benchmark;

/// <summary>
/// Persistence contract for benchmark run data.
/// All write methods persist immediately (no unit-of-work pattern needed
/// since each benchmark run is self-contained and non-transactional from
/// the business perspective).
/// </summary>
public interface IBenchmarkRepository
{
    /// <summary>
    /// Persists a new benchmark run header row and returns the saved entity.
    /// Must be called before <see cref="AddMessageResultAsync"/> or
    /// <see cref="SaveAggregateAsync"/>.
    /// </summary>
    Task<BenchmarkRunDaM> SaveRunAsync(BenchmarkRunDaM run);

    /// <summary>
    /// Appends a single per-message result row linked to an existing run.
    /// </summary>
    Task AddMessageResultAsync(BenchmarkMessageResultDaM result);

    /// <summary>
    /// Bulk-inserts all per-message results for a run in a single round-trip.
    /// Preferred over calling <see cref="AddMessageResultAsync"/> in a loop
    /// when the full result list is available upfront.
    /// </summary>
    Task AddMessageResultsAsync(IEnumerable<BenchmarkMessageResultDaM> results);

    /// <summary>
    /// Saves the pre-computed aggregate statistics row for a run.
    /// Overwrites any existing aggregate for the same <c>BenchmarkRunId</c>.
    /// </summary>
    Task SaveAggregateAsync(BenchmarkSessionAggregateDaM aggregate);

    /// <summary>
    /// Returns all run headers ordered by <c>RunAt</c> descending.
    /// Message results and aggregate are not included (navigation properties
    /// are not eagerly loaded).
    /// </summary>
    Task<IReadOnlyList<BenchmarkRunDaM>> GetRunsAsync();

    /// <summary>
    /// Returns a single run by ID including its message results and aggregate.
    /// Returns <c>null</c> when no run with the given ID exists.
    /// </summary>
    Task<BenchmarkRunDaM?> GetRunWithDetailsAsync(Guid benchmarkRunId);

    /// <summary>
    /// Returns all runs with their session aggregate eagerly loaded.
    /// Message results are NOT loaded to keep memory usage bounded.
    /// Ordered by <c>RunAt</c> descending.
    /// </summary>
    Task<IReadOnlyList<BenchmarkRunDaM>> GetAllRunsWithAggregatesAsync();

    /// <summary>
    /// Returns all runs with their per-message results eagerly loaded.
    /// Session aggregates are NOT included.
    /// Ordered by <c>RunAt</c> descending.
    /// </summary>
    Task<IReadOnlyList<BenchmarkRunDaM>> GetAllRunsWithMessageResultsAsync();

    /// <summary>
    /// Returns all runs with both session aggregates and per-message results eagerly loaded.
    /// Ordered by <c>RunAt</c> descending.
    /// </summary>
    Task<IReadOnlyList<BenchmarkRunDaM>> GetAllRunsWithAggregatesAndMessageResultsAsync();

    /// <summary>
    /// Deletes all rows from every benchmark table (payloads → message results
    /// → aggregates → runs). Returns the total number of rows deleted.
    /// </summary>
    Task<int> ClearAllAsync();
}
