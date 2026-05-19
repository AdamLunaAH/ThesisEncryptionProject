using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Collections.Generic;

using DataAccess.DbContext.Options;
namespace DataAccess.DbContext.Factory;

public class DatabaseConnections
{
    readonly IConfiguration _configuration;
    readonly DbConnectionSetsOptions _options;
    private readonly DbSetDetailOptions _activeDataSet;

    public DbSetDetailOptions GetActiveDbSet => _activeDataSet;
    public DbConnectionDetailOptions GetDataConnectionDetails(string user) => GetLoginDetails(user, _activeDataSet);
    DbConnectionDetailOptions GetLoginDetails(string user, DbSetDetailOptions dataSet)
    {
        if (string.IsNullOrEmpty(user) || string.IsNullOrWhiteSpace(user))
            throw new ArgumentNullException(nameof(user));

        var conn = dataSet.DbConnections.First(m => m.DbUserLogin.Trim().ToLower() == user.Trim().ToLower());
        return new DbConnectionDetailOptions
        {
            DbUserLogin = conn.DbUserLogin,
            DbConnection = conn.DbConnection,
            DbConnectionString = _configuration.GetConnectionString(conn.DbConnection)
        };
    }

    public DatabaseConnections(IConfiguration configuration, IOptions<DbConnectionSetsOptions> dbSetOption)
    {
        _configuration = configuration;
        _options = dbSetOption.Value;
        var configuredTag = configuration["DatabaseConnections:UseDataSetWithTag"]?.Trim();
        if (string.IsNullOrWhiteSpace(configuredTag))
            throw new ArgumentException("DatabaseConnections:UseDataSetWithTag is missing or empty");

        var candidates = (_options.DataSets ?? new List<DbSetDetailOptions>())
            .Where(ds => !string.IsNullOrWhiteSpace(ds?.DbTag))
            .ToList();

        // Fallback: read datasets directly from configuration to handle sparse or renumbered indexes.
        if (candidates.Count == 0)
        {
            candidates = configuration
                .GetSection("ConnectionSets:DataSets")
                .GetChildren()
                .Select(s => s.Get<DbSetDetailOptions>())
                .Where(ds => !string.IsNullOrWhiteSpace(ds?.DbTag))
                .ToList();
        }

        _activeDataSet = candidates.FirstOrDefault(ds => string.Equals(ds.DbTag.Trim(), configuredTag, StringComparison.OrdinalIgnoreCase));
        if (_activeDataSet == null)
        {
            var available = string.Join(", ", candidates.Select(ds => ds.DbTag));
            throw new ArgumentException($"Dataset with DbTag {configuredTag} not found. Available tags: {available}");
        }
    }
}