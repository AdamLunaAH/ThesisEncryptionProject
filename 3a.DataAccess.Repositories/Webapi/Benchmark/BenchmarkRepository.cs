using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using DataAccess.DbContext;
using DataAccess.DbContext.Factory;
using DataAccess.Models.Database.Benchmark;

namespace DataAccess.Repositories.Database.Benchmark;

public class BenchmarkRepository : IBenchmarkRepository
{
    private readonly ILogger<BenchmarkRepository> _logger;
    private readonly MainDbContext _dbContext;

    public BenchmarkRepository(
        ILogger<BenchmarkRepository> logger,
        IDbContextFactory dbContextFactory)
    {
        _logger = logger;
        _dbContext = dbContextFactory.CreateDbContextAsync().Result;
    }

    /// <inheritdoc/>
    public async Task<BenchmarkRunDaM> SaveRunAsync(BenchmarkRunDaM run)
    {
        run.RunNumber = (_dbContext.BenchmarkRuns.Any()
            ? await _dbContext.BenchmarkRuns.MaxAsync(r => r.RunNumber)
            : 0) + 1;

        run.AlgorithmRunNumber = (_dbContext.BenchmarkRuns.Any(r => r.AlgorithmId == run.AlgorithmId)
            ? await _dbContext.BenchmarkRuns
                .Where(r => r.AlgorithmId == run.AlgorithmId)
                .MaxAsync(r => r.AlgorithmRunNumber)
            : 0) + 1;

        _logger.LogInformation(
            "SaveRunAsync — RunId={RunId} TlsVersion={TlsVersion} (null={IsNull})",
            run.BenchmarkRunId, run.TlsVersion, run.TlsVersion is null);
        _dbContext.BenchmarkRuns.Add(run);
        await _dbContext.SaveChangesAsync();
        _logger.LogInformation(
            "Saved BenchmarkRun {RunId} — algorithm={Algorithm} messages={Count} run=#{RunNumber} algorithmRun=#{AlgorithmRunNumber}",
            run.BenchmarkRunId, run.AlgorithmId, run.MessageCount, run.RunNumber, run.AlgorithmRunNumber);
        return run;
    }

    /// <inheritdoc/>
    public async Task AddMessageResultAsync(BenchmarkMessageResultDaM result)
    {
        _dbContext.BenchmarkMessageResults.Add(result);
        await _dbContext.SaveChangesAsync();
    }

    /// <inheritdoc/>
    public async Task AddMessageResultsAsync(IEnumerable<BenchmarkMessageResultDaM> results)
    {
        _dbContext.BenchmarkMessageResults.AddRange(results);
        await _dbContext.SaveChangesAsync();
        _logger.LogInformation(
            "Saved {Count} BenchmarkMessageResult rows for run {RunId}",
            results.Count(), results.FirstOrDefault()?.BenchmarkRunId);
    }

    /// <inheritdoc/>
    public async Task SaveAggregateAsync(BenchmarkSessionAggregateDaM aggregate)
    {
        var existing = await _dbContext.BenchmarkSessionAggregates
            .FirstOrDefaultAsync(a => a.BenchmarkRunId == aggregate.BenchmarkRunId);

        if (existing is not null)
            _dbContext.BenchmarkSessionAggregates.Remove(existing);

        _dbContext.BenchmarkSessionAggregates.Add(aggregate);
        await _dbContext.SaveChangesAsync();
        _logger.LogInformation(
            "Saved BenchmarkSessionAggregate for run {RunId} — avgRoundTrip={Avg:F2}µs",
            aggregate.BenchmarkRunId, aggregate.AvgTotalRoundTripMicroseconds);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BenchmarkRunDaM>> GetRunsAsync()
    {
        return await _dbContext.BenchmarkRuns
            .OrderByDescending(r => r.RunAt)
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<BenchmarkRunDaM?> GetRunWithDetailsAsync(Guid benchmarkRunId)
    {
        return await _dbContext.BenchmarkRuns
            .Include(r => r.MessageResults)
            .Include(r => r.Aggregate)
            .FirstOrDefaultAsync(r => r.BenchmarkRunId == benchmarkRunId);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BenchmarkRunDaM>> GetAllRunsWithAggregatesAsync()
    {
        return await _dbContext.BenchmarkRuns
            .Include(r => r.Aggregate)
            .OrderByDescending(r => r.RunAt)
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BenchmarkRunDaM>> GetAllRunsWithMessageResultsAsync()
    {
        return await _dbContext.BenchmarkRuns
            .Include(r => r.MessageResults.OrderBy(m => m.MessageIndex))
            .OrderByDescending(r => r.RunAt)
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BenchmarkRunDaM>> GetAllRunsWithAggregatesAndMessageResultsAsync()
    {
        return await _dbContext.BenchmarkRuns
            .Include(r => r.Aggregate)
            .Include(r => r.MessageResults.OrderBy(m => m.MessageIndex))
            .OrderByDescending(r => r.RunAt)
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<int> ClearAllAsync()
    {
        // Delete in FK-safe order: payloads → message results → aggregates → runs
        int total = 0;
        total += await _dbContext.BenchmarkMessagePayloads.ExecuteDeleteAsync();
        total += await _dbContext.BenchmarkMessageResults.ExecuteDeleteAsync();
        total += await _dbContext.BenchmarkSessionAggregates.ExecuteDeleteAsync();
        total += await _dbContext.BenchmarkRuns.ExecuteDeleteAsync();
        _logger.LogInformation("Cleared all benchmark tables — {Total} rows deleted.", total);
        return total;
    }
}
