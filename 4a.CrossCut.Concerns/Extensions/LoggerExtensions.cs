using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using CrossCut.Concerns.Loggers;
namespace CrossCut.Concerns.Extensions;
public static class LoggerExtensions
{
    public static IServiceCollection AddInMemoryLogger(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<ILoggerProvider, InMemoryLoggerProvider>();
        
        return serviceCollection;
    }
}
