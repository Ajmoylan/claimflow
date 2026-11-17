using ClaimFlow.Api.Contracts;
using ClaimFlow.Api.Data;
using ClaimFlow.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClaimFlow.Api.Services;

/// <summary>Read-only figures for the dashboard. Aggregation happens in the database, not in memory.</summary>
public sealed class DashboardService(ClaimFlowDbContext db, TimeProvider clock, IOptions<DemoOptions> demoOptions)
{
    private const int RecentClaimsCount = 5;

    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var claims = db.Claims.AsNoTracking();

        var byStatus = await claims
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Amount = g.Sum(c => c.ClaimedAmount) })
            .ToListAsync(cancellationToken);

        var byCategory = await claims
            .GroupBy(c => c.Category)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var recent = await claims
            .OrderByDescending(c => c.CreatedAtUtc)
            .ThenByDescending(c => c.Id)
            .Take(RecentClaimsCount)
            .Select(c => new ClaimSummaryDto(
                c.Reference, c.Policy!.PolicyNumber, c.Category, c.IncidentDate, c.ClaimedAmount, c.Status, c.CreatedAtUtc, c.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        var visitorClaims = await claims.CountAsync(c => !c.IsDemoSeed, cancellationToken);
        var lastReset = await db.DemoState.AsNoTracking().Select(s => (DateTime?)s.LastResetAtUtc).SingleOrDefaultAsync(cancellationToken);

        // Every status and category appears, even with a zero count, so the UI can render a stable layout.
        var countByStatus = Enum.GetValues<ClaimStatus>().ToDictionary(s => s, s => byStatus.SingleOrDefault(x => x.Status == s)?.Count ?? 0);
        var countByCategory = Enum.GetValues<ClaimCategory>().ToDictionary(c => c, c => byCategory.SingleOrDefault(x => x.Category == c)?.Count ?? 0);

        var approved = countByStatus[ClaimStatus.Approved];
        var rejected = countByStatus[ClaimStatus.Rejected];
        double? approvalRate = approved + rejected == 0 ? null : Math.Round(approved / (double)(approved + rejected), 4);

        var options = demoOptions.Value;
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var nextReset = DemoDataService.MostRecentScheduledResetUtc(nowUtc, options).AddDays(1);

        return new DashboardSummaryDto(
            TotalClaims: countByStatus.Values.Sum(),
            CountByStatus: countByStatus,
            CountByCategory: countByCategory,
            TotalClaimedAmount: byStatus.Sum(x => x.Amount),
            TotalApprovedAmount: byStatus.SingleOrDefault(x => x.Status == ClaimStatus.Approved)?.Amount ?? 0m,
            ApprovalRate: approvalRate,
            OpenClaims: countByStatus[ClaimStatus.Submitted] + countByStatus[ClaimStatus.UnderReview],
            RecentClaims: recent,
            Demo: new DemoInfoDto(visitorClaims, options.MaxVisitorClaims, lastReset, nextReset));
    }
}
