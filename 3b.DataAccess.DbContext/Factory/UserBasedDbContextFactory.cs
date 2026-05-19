using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

using DataAccess.DbContext.Options;
namespace DataAccess.DbContext.Factory;

public class UserBasedDbContextFactory : IDbContextFactory
{
    private readonly IConfiguration _configuration;
    private readonly DatabaseConnections _databaseConnections;
    private readonly DatabaseOptions _databaseOptions;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<UserBasedDbContextFactory> _logger;

    public UserBasedDbContextFactory(
        IConfiguration configuration,
        DatabaseConnections databaseConnections,
        IOptions<DatabaseOptions> databaseOptions,
        IHttpContextAccessor httpContextAccessor,
        ILogger<UserBasedDbContextFactory> logger)
    {
        _configuration = configuration;
        _databaseConnections = databaseConnections;
        _databaseOptions = databaseOptions.Value;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    // Creates DbContext using current user's role from authentication context, jwt tokens or default role
    public async Task<MainDbContext> CreateDbContextAsync()
    {
        try
        {
            var userRole = await GetCurrentUserRoleAsync();
            return await CreateDbContextAsync(userRole);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create DbContext");
            throw;
        }
    }

    // Determines current user role
    // Depending on application, expanded to multiple sources with fallback strategy
    // Right now, only fallback to default role from configuration
    private async Task<string> GetCurrentUserRoleAsync()
    {
        // var httpContext = _httpContextAccessor.HttpContext;
        // if (httpContext != null)
        // {
        //     // Get user role from claims from various soruces e.g., authentication context or Jwt token
        //     // Right now, only fallback to default role from configuration
        // }

        // Default fallback
        var defaultRole = _configuration["DatabaseConnections:DefaultDataUser"];
        _logger.LogInformation("Using default role: {Role}", defaultRole);
        return defaultRole;
    }

    // Creates DbContext using specified user role
    private async Task<MainDbContext> CreateDbContextAsync(string userRole)
    {
        var conn = _databaseConnections.GetDataConnectionDetails(userRole);
        var options = CreateDbContextOptions(conn);

        _logger.LogInformation("Creating DbContext with connection for role: {UserRole}", userRole);
        return new MainDbContext(options);
    }

    // Creates EF Core DbContextOptions based on connection details
    private DbContextOptions<MainDbContext> CreateDbContextOptions(DbConnectionDetailOptions conn)
    {
        var optionsBuilder = new DbContextOptionsBuilder<MainDbContext>();

        if (_databaseOptions.DataConnectionServer == DatabaseServer.PostgreSql)
        {
            optionsBuilder.UseNpgsql(conn.DbConnectionString);
            optionsBuilder.LogTo(
                message => _logger.LogInformation("{EfSql}", message),
                new[] { "Microsoft.EntityFrameworkCore.Database.Command" },
                Microsoft.Extensions.Logging.LogLevel.Information);
        }
        else
        {
            throw new InvalidDataException($"DbContext for {_databaseOptions.DataConnectionServer} not existing");
        }

        return optionsBuilder.Options;
    }
}