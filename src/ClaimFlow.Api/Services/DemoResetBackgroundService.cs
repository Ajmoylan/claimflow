using Microsoft.Extensions.Options;

namespace ClaimFlow.Api.Services;

/// <summary>
/// Belt and braces for the nightly reset: while the app happens to be running, check periodically so the
/// reset also happens when nobody is visiting. Correctness does not depend on this service. The
/// <see cref="DemoResetGate"/> checks before every API request, which covers the free tier unloading the app.
/// </summary>
public sealed class DemoResetBackgroundService(DemoResetGate gate, IOptions<DemoOptions> options, TimeProvider clock)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.AutoResetEnabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.CheckInterval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await gate.EnsureFreshAsync(stoppingToken); // never throws except on shutdown
        }
    }
}
