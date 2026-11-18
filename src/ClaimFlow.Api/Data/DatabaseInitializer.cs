using ClaimFlow.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClaimFlow.Api.Data;

/// <summary>
/// Runs once at startup: applies migrations (if enabled), or otherwise checks that someone else already has,
/// then seeds the demo data on an empty database.
/// </summary>
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
        else
        {
            // In Azure the app's identity may only read and write data, so the schema is migrated separately
            // (see infra/DEPLOYMENT.md). Refuse to start against an out-of-date schema instead of failing on
            // every request; the deployment smoke test then fails and the problem is visible straight away.
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            if (pending.Count > 0)
            {
                var names = string.Join(", ", pending);
                LogPendingMigrations(logger, pending.Count, names);
                throw new InvalidOperationException(
                    $"The database schema is out of date: {pending.Count} migration(s) have not been applied " +
                    $"({names}). Apply them with the migration script before starting the app, " +
                    "or set Database:MigrateOnStartup=true where the app's identity may change the schema.");
            }
        }

        if (!await db.Policies.AnyAsync(cancellationToken))
        {
            await provider.GetRequiredService<DemoDataService>().ResetAsync(cancellationToken);
        }
    }

    [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "Database migrations applied ({Provider})")]
    private static partial void LogMigrated(ILogger logger, DatabaseProvider provider);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Critical,
        Message = "Database schema is out of date: {Count} pending migration(s): {Migrations}")]
    private static partial void LogPendingMigrations(ILogger logger, int count, string migrations);
}
