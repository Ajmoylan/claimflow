using ClaimFlow.Api.Contracts;
using ClaimFlow.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ClaimFlow.Api.Services;

/// <summary>Read-only access to the fictional policies. Visitors can never create or change policies.</summary>
public sealed class PolicyService(ClaimFlowDbContext db, TimeProvider clock)
{
    public async Task<IReadOnlyList<PolicyDto>> ListAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return await db.Policies
            .AsNoTracking()
            .OrderBy(p => p.PolicyNumber)
            .Select(p => new PolicyDto(
                p.PolicyNumber,
                p.HolderName,
                p.CoverageCategory,
                p.CoverageLimit,
                p.ValidFrom,
                p.ValidTo,
                p.ValidFrom <= today && p.ValidTo >= today))
            .ToListAsync(cancellationToken);
    }
}
