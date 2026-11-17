using Microsoft.Extensions.Options;

namespace ClaimFlow.Api.Services;

/// <summary>
/// Checks periodically whether the nightly reset is due. On the free App Service plan the app sleeps when idle,
/// so the check also runs at startup: the first visitor after 03:00 always sees fresh demo data.
/// </summary>
public sealed partial class DemoResetBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<DemoOptions> options,
    TimeProvider clock,
    ILogger<DemoResetBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.AutoResetEnabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.CheckInterval, clock);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var demo = scope.ServiceProvider.GetRequiredService<DemoDataService>();
                await demo.ResetIfDueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never crash the host because of a demo reset; try again on the next tick.
                LogResetCheckFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(EventId = 3001, Level = LogLevel.Error, Message = "Demo reset check failed")]
    private static partial void LogResetCheckFailed(ILogger logger, Exception exception);
}
