using Microsoft.Extensions.Options;

namespace ClaimFlow.Api.Services;

/// <summary>
/// Makes sure the nightly demo reset has happened before any API request is served.
/// <para>
/// The free App Service tier unloads the app when it is idle, so a background timer alone cannot be trusted:
/// the process may not be running at 03:00, and after a wake-up the first visitor could otherwise see,
/// or add to, yesterday's data. This gate closes that gap without touching the database on every request:
/// it caches the time until which the data is known to be fresh (the next scheduled reset), and only when
/// that time has passed does one request run the reset check while the others wait.
/// </para>
/// </summary>
public sealed partial class DemoResetGate(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IOptions<DemoOptions> options,
    ILogger<DemoResetGate> logger) : IDisposable
{
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _lock = new(1, 1);

    // Ticks of the UTC time until which no check is needed. 0 = unknown, check on the next request.
    private long _freshUntilUtcTicks;

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    private bool CheckNeeded => options.Value.AutoResetEnabled && UtcNow.Ticks >= Interlocked.Read(ref _freshUntilUtcTicks);

    /// <summary>Resets the demo data if a scheduled reset time has passed since the last reset. Cheap when nothing is due.</summary>
    public async Task EnsureFreshAsync(CancellationToken cancellationToken)
    {
        if (!CheckNeeded)
        {
            return;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!CheckNeeded)
            {
                return; // another request finished the check while we waited
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DemoDataService>().ResetIfDueAsync(cancellationToken);
            Interlocked.Exchange(ref _freshUntilUtcTicks, DemoDataService.NextScheduledResetUtc(UtcNow, options.Value).Ticks);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never fail a visitor's request because of the demo reset (e.g. the database is still resuming).
            // Serve the request and try again shortly.
            Interlocked.Exchange(ref _freshUntilUtcTicks, (UtcNow + RetryAfterFailure).Ticks);
            LogResetCheckFailed(logger, ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    [LoggerMessage(EventId = 3001, Level = LogLevel.Error, Message = "Demo reset check failed; will retry shortly")]
    private static partial void LogResetCheckFailed(ILogger logger, Exception exception);
}

public static class DemoResetGateMiddleware
{
    /// <summary>Runs the reset gate before every /api request.</summary>
    public static IApplicationBuilder UseDemoResetGate(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                await context.RequestServices.GetRequiredService<DemoResetGate>().EnsureFreshAsync(context.RequestAborted);
            }

            await next();
        });
}
