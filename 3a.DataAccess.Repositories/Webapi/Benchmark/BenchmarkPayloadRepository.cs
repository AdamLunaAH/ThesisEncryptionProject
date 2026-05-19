using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using DataAccess.DbContext;
using DataAccess.DbContext.Factory;
using DataAccess.Models.Database.Benchmark;

namespace DataAccess.Repositories.Database.Benchmark;

public class BenchmarkPayloadRepository : IBenchmarkPayloadRepository
{
    private readonly ILogger<BenchmarkPayloadRepository> _logger;
    private readonly MainDbContext _dbContext;

    public BenchmarkPayloadRepository(
        ILogger<BenchmarkPayloadRepository> logger,
        IDbContextFactory dbContextFactory)
    {
        _logger = logger;
        _dbContext = dbContextFactory.CreateDbContextAsync().Result;
    }

    /// <inheritdoc/>
    public async Task<BenchmarkMessagePayloadDaM> SavePayloadAsync(BenchmarkMessagePayloadDaM payload)
    {
        _dbContext.BenchmarkMessagePayloads.Add(payload);
        await _dbContext.SaveChangesAsync();
        _logger.LogInformation(
            "Saved BenchmarkMessagePayload {Id} — run={RunId} index={Index} algorithm={Algorithm} wireBytes={Wire}",
            payload.BenchmarkMessagePayloadId, payload.BenchmarkRunId,
            payload.MessageIndex, payload.AlgorithmId, payload.TotalWireBytes);
        return payload;
    }

    /// <inheritdoc/>
    public async Task SavePayloadsAsync(IEnumerable<BenchmarkMessagePayloadDaM> payloads)
    {
        var list = payloads.ToList();
        _dbContext.BenchmarkMessagePayloads.AddRange(list);
        await _dbContext.SaveChangesAsync();
        _logger.LogInformation(
            "Saved {Count} BenchmarkMessagePayload rows for run {RunId}",
            list.Count, list.FirstOrDefault()?.BenchmarkRunId);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BenchmarkMessagePayloadDaM>> GetPayloadsByRunAsync(Guid runId)
    {
        return await _dbContext.BenchmarkMessagePayloads
            .Where(p => p.BenchmarkRunId == runId)
            .OrderBy(p => p.MessageIndex)
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BenchmarkMessagePayloadDaM>> GetPayloadMetadataByAlgorithmAsync(
        string algorithmId)
    {
        // Project only metadata — omit binary columns so responses stay lightweight.
        return await _dbContext.BenchmarkMessagePayloads
            .Where(p => p.AlgorithmId == algorithmId)
            .OrderByDescending(p => p.BenchmarkRunId)
            .ThenBy(p => p.MessageIndex)
            .Select(p => new BenchmarkMessagePayloadDaM
            {
                BenchmarkMessagePayloadId = p.BenchmarkMessagePayloadId,
                BenchmarkRunId = p.BenchmarkRunId,
                MessageIndex = p.MessageIndex,
                AlgorithmId = p.AlgorithmId,
                Ciphertext = Array.Empty<byte>(), // omitted
                EncapsulatedKey = null,
                MacTag = null,
                PlaintextBytes = p.PlaintextBytes,
                TotalWireBytes = p.TotalWireBytes,
                FilePath = p.FilePath
            })
            .ToListAsync();
    }
}
