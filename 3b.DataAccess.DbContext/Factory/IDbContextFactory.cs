namespace DataAccess.DbContext.Factory;
public interface IDbContextFactory
{
    Task<MainDbContext> CreateDbContextAsync();   
}