using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

using DataAccess.DbContext.Factory;
using DataAccess.DbContext.Options;
namespace DataAccess.DbContext.Extensions;  
public static class DbContextExtensions
{
    public static IServiceCollection AddUserDbContextFactory(this IServiceCollection serviceCollection)
    {
        // Build a temporary service provider to access IConfiguration
        var sp = serviceCollection.BuildServiceProvider();
        var config = sp.GetRequiredService<IConfiguration>();

        // Configure DbConnectionSetsOptions from configuration
        serviceCollection.Configure<DbConnectionSetsOptions>(
            options => config.GetSection(DbConnectionSetsOptions.Position).Bind(options));
        serviceCollection.AddSingleton<DatabaseConnections>();

        // Register the factory as scoped to ensure proper request-time resolution for various applications
        serviceCollection.AddHttpContextAccessor();   
        serviceCollection.AddScoped<IDbContextFactory, UserBasedDbContextFactory>();

        // Configure DatabaseOptions using the factory
        // Rebuild service provider after registering DatabaseConnections
        sp = serviceCollection.BuildServiceProvider();
        var databaseConnections = sp.GetRequiredService<DatabaseConnections>();
        serviceCollection.Configure<DatabaseOptions>(options => DatabaseOptions.ReadEnvironment(options, config, databaseConnections));
     
       return serviceCollection;
    }
}