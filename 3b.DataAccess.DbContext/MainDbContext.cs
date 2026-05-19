using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using DataAccess.DbContext.Extensions;
using DataAccess.Models.Database.Benchmark;

namespace DataAccess.DbContext;

//DbContext namespace is a fundamental EFC layer of the database context and is
//used for all Database connection as well as for EFC CodeFirst migration and database updates
public class MainDbContext : Microsoft.EntityFrameworkCore.DbContext
{
#if DEBUG
    // remove password from connection string in debug mode
    // this is useful for debugging and logging purposes, but should not be used in production code
    public string dbConnection => System.Text.RegularExpressions.Regex.Replace(
        this.Database.GetConnectionString() ?? "", @"(pwd|password)=[^;]*;?", "",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
#endif

    //Tables

    // Benchmark Tables/DbSets (schema: benchmark)
    public DbSet<BenchmarkRunDaM> BenchmarkRuns => Set<BenchmarkRunDaM>();
    public DbSet<BenchmarkMessageResultDaM> BenchmarkMessageResults => Set<BenchmarkMessageResultDaM>();
    public DbSet<BenchmarkSessionAggregateDaM> BenchmarkSessionAggregates => Set<BenchmarkSessionAggregateDaM>();
    public DbSet<BenchmarkMessagePayloadDaM> BenchmarkMessagePayloads => Set<BenchmarkMessagePayloadDaM>();
    public DbSet<BenchmarkResourceSampleDaM> BenchmarkResourceSamples => Set<BenchmarkResourceSampleDaM>();
    public DbSet<BenchmarkResourceAggregateDaM> BenchmarkResourceAggregates => Set<BenchmarkResourceAggregateDaM>();

    //Views

    #region constructors
    public MainDbContext() { }
    public MainDbContext(DbContextOptions options) : base(options) { }
    #endregion

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        //model the Views

        base.OnModelCreating(modelBuilder);
    }

    #region Derived DbContext for some popular databases, which one to use is configured in appsettings.json

    public class PostgresDbContext : MainDbContext
    {
        public PostgresDbContext() { }
        public PostgresDbContext(DbContextOptions options) : base(options) { }


        //Used only for CodeFirst Database Migration
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder = optionsBuilder.ConfigureForDesignTime(
                    (options, connectionString) => options.UseNpgsql(connectionString));
            }

            base.OnConfiguring(optionsBuilder);
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.Properties<string>().HaveColumnType("varchar(200)");
            base.ConfigureConventions(configurationBuilder);
        }
    }
    #endregion
}
