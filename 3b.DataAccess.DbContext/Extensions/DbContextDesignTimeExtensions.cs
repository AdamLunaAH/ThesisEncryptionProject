using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;

using DataAccess.DbContext.Factory;
using DataAccess.DbContext.Options;
using CrossCut.Secrets.Options;
using CrossCut.Secrets.Extensions;
namespace DataAccess.DbContext.Extensions;
public static class DbContextDesignTimeExtensions
{
    public static DbContextOptionsBuilder ConfigureForDesignTime(
        this DbContextOptionsBuilder optionsBuilder, 
        Func<DbContextOptionsBuilder, string, DbContextOptionsBuilder> databaseOptions)
    {
        System.Console.WriteLine($"Executing DesignTimeConfigure...");
        
        var (configuration, databaseConnections) = CreateDesignTimeServices();
        
        var connection = GetDatabaseConnection(configuration, databaseConnections);

        optionsBuilder = databaseOptions(optionsBuilder, connection.DbConnectionString);
        System.Console.WriteLine($"DesignTimeConfigure completed successfully");
        System.Console.WriteLine($"Proceeding with migration.");
        System.Console.WriteLine($"   User: {connection.DbUserLogin}");
        System.Console.WriteLine($"   Database connection: {connection.DbConnection}");
        
        return optionsBuilder;
    }

    private static (IConfiguration configuration, DatabaseConnections databaseConnections) CreateDesignTimeServices()
    {
        //ASP.NET Core program.cs has not run by efc design-time, configure and create services as in program.cs

        // Get folder where appsettings.json and UserSecretAssembly is located from environment variable
        var appsettingsFolder = Environment.GetEnvironmentVariable("EFC_AppSettingsFolder");
        if (string.IsNullOrEmpty(appsettingsFolder)) throw new InvalidOperationException($"Error: EFC_AppSettingsFolder environment variable should not be set");
        var assemblyFolder = Environment.GetEnvironmentVariable("EFC_AssemblyFolder");
        if (string.IsNullOrEmpty(assemblyFolder)) throw new InvalidOperationException($"Error: EFC_AssemblyFolder environment variable should not be set");

        System.Console.WriteLine($"   using appsettings.json in folder: {appsettingsFolder}");
        if (File.Exists(Path.Combine(appsettingsFolder, "appsettings.json")))
        {
            System.Console.WriteLine($"   appsettings.json: {Path.Combine(appsettingsFolder, "appsettings.json")}");
        }
        else
        {
            throw new FileNotFoundException($"Error: appsettings.json not found in folder: {appsettingsFolder}");
        }

        // Create a configuration
        System.Console.WriteLine($"   configuring");
        var conf = new ConfigurationBuilder();
        conf.AddSecretsDesignTime(new HostingEnvironment { EnvironmentName = "Development" }, (appsettingsFolder, assemblyFolder));

        System.Console.WriteLine($"   building configuring");
        var configuration = conf.Build();

        System.Console.WriteLine($"   creating services");
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddOptions(); // Add the Options framework
        serviceCollection.AddDatabaseConnections(configuration);
        serviceCollection.AddSingleton<IConfiguration>(configuration);
        serviceCollection.AddEnvironmentInfo();

        // Build the service provider and get the DbContext
        System.Console.WriteLine($"   retrieving services from serviceProvider");

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var configurationService = serviceProvider.GetRequiredService<IConfiguration>();
        System.Console.WriteLine($"   {nameof(IConfiguration)} retrieved");

        var databaseConnections = serviceProvider.GetRequiredService<DatabaseConnections>();
        var environmentOptions = serviceProvider.GetRequiredService<IOptions<EnvironmentOptions>>().Value;

        System.Console.WriteLine($"   {nameof(DatabaseConnections)} retrieved");
        System.Console.WriteLine($"   secret source: {environmentOptions.SecretSource}");
        System.Console.WriteLine($"   secret id: {environmentOptions.SecretId}");
        System.Console.WriteLine($"   DataConnectionTag: {databaseConnections.GetActiveDbSet.DbTag}");
        System.Console.WriteLine($"   DataConnectionServer: {databaseConnections.GetActiveDbSet.DbServer}");
  
  
        return (configurationService, databaseConnections);
    }

    private static DbConnectionDetailOptions GetDatabaseConnection(IConfiguration configuration, DatabaseConnections databaseConnections)
    {
        var connection = databaseConnections.GetDataConnectionDetails(configuration["DatabaseConnections:MigrationUser"]);
        if (connection.DbConnectionString == null)
        {
            throw new InvalidDataException($"Error: Connection string for {connection.DbConnection}, {connection.DbUserLogin} not set");
        }

        return connection;
    }
}
