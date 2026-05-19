using DataAccess.DbContext.Factory;
using Microsoft.Extensions.Configuration;
namespace DataAccess.DbContext.Options;

public enum DatabaseServer { Unknown, SQLServer, MySql, PostgreSql, SQLite }

public class DatabaseOptions
{
    public string DataConnectionTag { get; set; }
    public string DefaultDataUser { get; set; }
    public string MigrationUser { get; set; }
    public DatabaseServer DataConnectionServer { get; set; }

    public DatabaseOptions()
    {
        DataConnectionTag = string.Empty;
        DefaultDataUser = string.Empty;
        MigrationUser = string.Empty;
        DataConnectionServer = DatabaseServer.Unknown;
    }

    //for json clear text
    public string DataConnectionServerString => DataConnectionServer.ToString();

    public static void ReadEnvironment(DatabaseOptions options, IConfiguration _configuration, DatabaseConnections databaseConnections)
    {
        options.DataConnectionTag = databaseConnections.GetActiveDbSet.DbTag;
        options.DefaultDataUser = _configuration["DatabaseConnections:DefaultDataUser"];
        options.MigrationUser = _configuration["DatabaseConnections:MigrationUser"];
        options.DataConnectionServer = databaseConnections.GetActiveDbSet.DbServer.Trim().ToLower() switch
        {
            "postgresql" => DatabaseServer.PostgreSql,
            _ => throw new NotSupportedException($"DbServer {databaseConnections.GetActiveDbSet.DbServer} not supported")
        };
    }
}
