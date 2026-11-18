using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace ClaimFlow.Api.Data;

/// <summary>Records when the app last opened a database connection (EF Core interceptor).</summary>
public sealed class SqlConnectionActivity(TimeProvider clock) : DbConnectionInterceptor
{
    private long _lastOpenedUtcTicks;

    /// <summary>Last time a connection was opened, or null if none has been opened since the pools were last released.</summary>
    public DateTime? LastOpenedUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastOpenedUtcTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => Touch();

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Touch();
        return Task.CompletedTask;
    }

    /// <summary>Forget the last activity, but only if nothing has opened a connection since <paramref name="seenUtc"/>.</summary>
    internal void ResetIfUnchanged(DateTime seenUtc) =>
        Interlocked.CompareExchange(ref _lastOpenedUtcTicks, 0, seenUtc.Ticks);

    private void Touch() => Interlocked.Exchange(ref _lastOpenedUtcTicks, clock.GetUtcNow().UtcDateTime.Ticks);
}

/// <summary>
/// Lets an Azure SQL serverless database auto-pause. ADO.NET keeps idle pooled connections open for
/// roughly 4–8 minutes, and a serverless database only pauses after it has had no sessions for its
/// auto-pause delay. Once the app has not used the database for <see cref="DatabaseOptions.ReleaseIdleConnectionsAfter"/>,
/// this closes the pooled connections. The next request simply opens a new one.
/// </summary>
public sealed partial class IdleSqlConnectionReleaser(
    SqlConnectionActivity activity,
    TimeProvider clock,
    IOptions<DatabaseOptions> options,
    ILogger<IdleSqlConnectionReleaser> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    /// <summary>Releases pooled connections if the database has been idle long enough. Returns true if it did.</summary>
    public bool ReleaseIfIdle()
    {
        if (activity.LastOpenedUtc is not { } lastOpened ||
            clock.GetUtcNow().UtcDateTime - lastOpened < options.Value.ReleaseIdleConnectionsAfter)
        {
            return false;
        }

        // Idle pooled connections are closed now; any in use are closed when they are returned to the pool.
        SqlConnection.ClearAllPools();
        activity.ResetIfUnchanged(lastOpened);
        LogReleased(logger);
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            ReleaseIfIdle();
        }
    }

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Released idle SQL connections so the database can auto-pause")]
    private static partial void LogReleased(ILogger logger);
}
