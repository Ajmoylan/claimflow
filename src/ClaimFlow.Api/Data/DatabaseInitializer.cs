using ClaimFlow.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClaimFlow.Api.Data;

/// <summary>Runs once at startup: applies migrations (if enabled) and seeds the demo data on an empty database.</summary>
public static partial class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<ClaimFlowDbContext>();
        var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));

        if (options.MigrateOnStartup)
        {
            await db.Database.MigrateAsync(cancellationToken);
            LogMigrated(logger, options.Provider);
        }

        if (!await db.Policies.AnyAsync(cancellationToken))
        {
            await provider.GetRequiredService<DemoDataService>().ResetAsync(cancellationToken);
        }
    }

    [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "Database migrations applied ({Provider})")]
    private static partial void LogMigrated(ILogger logger, DatabaseProvider provider);
}
