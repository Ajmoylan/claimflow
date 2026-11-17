using ClaimFlow.Api.Data;
using ClaimFlow.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClaimFlow.Api.Services;

/// <summary>
/// Keeps the public demo in a known state: makes sure the fictional policies exist and,
/// on a reset, replaces every claim with the original example claims.
/// There is deliberately no public endpoint for this. Resets only happen on the server's schedule.
/// </summary>
public sealed partial class DemoDataService(
    ClaimFlowDbContext db,
    TimeProvider clock,
    IOptions<DemoOptions> options,
    ILogger<DemoDataService> logger)
{
    /// <summary>Resets the demo if the most recent scheduled reset time has passed since the last reset.</summary>
    public async Task<bool> ResetIfDueAsync(CancellationToken cancellationToken)
    {
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var state = await db.DemoState.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var due = state is null || state.LastResetAtUtc < MostRecentScheduledResetUtc(nowUtc, options.Value);
        if (due)
        {
            await ResetAsync(cancellationToken);
        }

        return due;
    }

    /// <summary>Deletes every claim and its history, then restores the seed policies and claims, in one transaction.</summary>
    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        // With SQL Server retries enabled, a manual transaction must run inside the execution strategy
        // so the whole unit is retried together after a transient failure.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async ct =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            await db.ClaimStatusChanges.ExecuteDeleteAsync(ct);
            await db.Claims.ExecuteDeleteAsync(ct);

            var policies = await EnsurePoliciesAsync(ct);
            db.Claims.AddRange(DemoData.CreateClaims(policies));

            var nowUtc = clock.GetUtcNow().UtcDateTime;
            var state = await db.DemoState.SingleOrDefaultAsync(ct);
            if (state is null)
            {
                db.DemoState.Add(new DemoState { LastResetAtUtc = nowUtc });
            }
            else
            {
                state.LastResetAtUtc = nowUtc;
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            db.ChangeTracker.Clear();
        }, cancellationToken);

        LogDemoReset(logger);
    }

    /// <summary>The last time a scheduled reset should have happened, e.g. 03:00 Oslo time today or yesterday.</summary>
    internal static DateTime MostRecentScheduledResetUtc(DateTime nowUtc, DemoOptions options)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(options.ResetTimeZone);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        var todaysReset = localNow.Date + options.ResetTimeOfDay.ToTimeSpan();
        var localReset = localNow >= todaysReset ? todaysReset : todaysReset.AddDays(-1);
        if (zone.IsInvalidTime(localReset))
        {
            // The configured time falls in a skipped daylight-saving hour; use the first valid time after it.
            localReset = localReset.AddHours(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localReset, DateTimeKind.Unspecified), zone);
    }

    private async Task<Dictionary<string, Policy>> EnsurePoliciesAsync(CancellationToken cancellationToken)
    {
        var existing = await db.Policies.ToDictionaryAsync(p => p.PolicyNumber, cancellationToken);
        var missing = DemoData.CreatePolicies().Where(p => !existing.ContainsKey(p.PolicyNumber)).ToList();
        if (missing.Count > 0)
        {
            db.Policies.AddRange(missing);
            await db.SaveChangesAsync(cancellationToken);
            foreach (var policy in missing)
            {
                existing[policy.PolicyNumber] = policy;
            }
        }

        return existing;
    }

    [LoggerMessage(EventId = 3000, Level = LogLevel.Information, Message = "Demo data reset to the seed dataset")]
    private static partial void LogDemoReset(ILogger logger);
}
