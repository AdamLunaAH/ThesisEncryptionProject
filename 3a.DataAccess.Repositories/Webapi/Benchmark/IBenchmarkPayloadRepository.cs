using DataAccess.Models.Database.Benchmark;

namespace DataAccess.Repositories.Database.Benchmark;

/// <summary>
/// Persistence operations for <see cref="BenchmarkMessagePayloadDaM"/> rows —
/// the raw ciphertext bytes captured during a payload benchmark run.
/// </summary>
public interface IBenchmarkPayloadRepository
{
    /// <summary>
    /// Persists a single encrypted payload row.
    /// </summary>
    Task<BenchmarkMessagePayloadDaM> SavePayloadAsync(BenchmarkMessagePayloadDaM payload);

    /// <summary>
    /// Bulk-inserts all payload rows for a run in a single database round-trip.
    /// </summary>
    Task SavePayloadsAsync(IEnumerable<BenchmarkMessagePayloadDaM> payloads);

    /// <summary>
    /// Returns all payload rows for a given run, ordered by <c>MessageIndex</c>.
    /// </summary>
    Task<IReadOnlyList<BenchmarkMessagePayloadDaM>> GetPayloadsByRunAsync(Guid runId);

    /// <summary>
    /// Returns all payload rows for a given algorithm across all runs,
    /// ordered by run date descending then message index.
    /// Only metadata columns are loaded — binary columns (<c>Ciphertext</c> etc.)
    /// are NOT projected to keep the response small.
    /// Use <see cref="GetPayloadsByRunAsync"/> when you need the actual bytes.
    /// </summary>
    Task<IReadOnlyList<BenchmarkMessagePayloadDaM>> GetPayloadMetadataByAlgorithmAsync(string algorithmId);
}
