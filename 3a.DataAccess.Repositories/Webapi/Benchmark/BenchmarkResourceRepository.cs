using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using DataAccess.DbContext;
using DataAccess.DbContext.Factory;
using DataAccess.Models.Database.Benchmark;

namespace DataAccess.Repositories.Database.Benchmark;

public class BenchmarkResourceRepository : IBenchmarkResourceRepository
{
    private readonly ILogger<BenchmarkResourceRepository> _logger;
    private readonly MainDbContext _dbContext;

    public BenchmarkResourceRepository(
        ILogger<BenchmarkResourceRepository> logger,
        IDbContextFactory dbContextFactory)
    {
        _logger = logger;
        _dbContext = dbContextFactory.CreateDbContextAsync().Result;
    }

    /// <inheritdoc/>
    public async Task AddSamplesAsync(IEnumerable<BenchmarkResourceSampleDaM> samples)
    {
        var list = samples.ToList();
        if (list.Count == 0)
            return;

        _dbContext.BenchmarkResourceSamples.AddRange(list);
        await _dbContext.SaveChangesAsync();
        _logger.LogInformation(
            "Saved {Count} BenchmarkResourceSample rows for run {RunId}",
            list.Count, list[0].BenchmarkRunId);
    }

    /// <inheritdoc/>
    public async Task SaveAggregateAsync(BenchmarkResourceAggregateDaM aggregate)
    {
        var existing = await _dbContext.BenchmarkResourceAggregates
            .FirstOrDefaultAsync(a => a.BenchmarkRunId == aggregate.BenchmarkRunId);

        if (existing is not null)
            _dbContext.BenchmarkResourceAggregates.Remove(existing);

        _dbContext.BenchmarkResourceAggregates.Add(aggregate);
        await _dbContext.SaveChangesAsync();
        _logger.LogInformation(
            "Saved BenchmarkResourceAggregate for run {RunId} — cpuMax={CpuMax:F1}% memMax={MemMax:F1}MB samples={Count}",
            aggregate.BenchmarkRunId, aggregate.CpuMax, aggregate.MemoryMaxMb, aggregate.SampleCount);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<Guid, BenchmarkResourceAggregateDaM>> GetAllAggregatesAsync()
    {
        var list = await _dbContext.BenchmarkResourceAggregates.ToListAsync();
        return list.ToDictionary(a => a.BenchmarkRunId);
    }

    /// <inheritdoc/>
    public async Task<int> ClearAllAsync()
    {
        int total = 0;
        total += await _dbContext.BenchmarkResourceSamples.ExecuteDeleteAsync();
        total += await _dbContext.BenchmarkResourceAggregates.ExecuteDeleteAsync();
        _logger.LogInformation("Cleared {Total} resource monitoring rows", total);
        return total;
    }
}
