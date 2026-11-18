using Microsoft.EntityFrameworkCore;

namespace ClaimFlow.Api.Data;

public enum DatabaseProvider
{
    Sqlite,
    SqlServer,
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

    /// <summary>Apply pending migrations at startup. Fine for this single-instance demo; see README for the trade-off.</summary>
    public bool MigrateOnStartup { get; set; } = true;

    /// <summary>
    /// SQL Server only: close pooled connections after this long without database activity, so an
    /// Azure SQL serverless database sees zero sessions and can auto-pause. Zero disables it.
    /// </summary>
    public TimeSpan ReleaseIdleConnectionsAfter { get; set; } = TimeSpan.FromMinutes(2);
}

public static class DatabaseRegistration
{
    public const string ConnectionStringName = "ClaimFlow";

    /// <summary>
    /// Registers <see cref="ClaimFlowDbContext"/> backed by the provider chosen in configuration.
    /// Everything else in the app asks for the base type and does not know which provider is in use.
    /// </summary>
    public static IServiceCollection AddClaimFlowDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));

        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

        switch (options.Provider)
        {
            case DatabaseProvider.Sqlite:
                services.AddDbContext<ClaimFlowDbContext, SqliteClaimFlowDbContext>(db => db.UseSqlite(connectionString));
                break;

            case DatabaseProvider.SqlServer:
                services.AddSingleton<SqlConnectionActivity>();
                services.AddDbContext<ClaimFlowDbContext, SqlServerClaimFlowDbContext>((provider, db) => db
                    .UseSqlServer(connectionString, sql =>
                        // Azure SQL serverless can be paused; retries cover the resume and other transient faults.
                        sql.EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null))
                    .AddInterceptors(provider.GetRequiredService<SqlConnectionActivity>()));
                if (options.ReleaseIdleConnectionsAfter > TimeSpan.Zero)
                {
                    services.AddSingleton<IdleSqlConnectionReleaser>();
                    services.AddHostedService(provider => provider.GetRequiredService<IdleSqlConnectionReleaser>());
                }

                break;

            default:
                throw new InvalidOperationException($"Unsupported database provider '{options.Provider}'.");
        }

        return services;
    }
}
