using ClaimFlow.Api.Contracts;
using ClaimFlow.Api.Data;
using ClaimFlow.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClaimFlow.Api.Services;

/// <summary>
/// Use cases for claims: search, read, create and move through the workflow.
/// Controllers stay thin and call this; business rules live here and in <see cref="Claim"/>.
/// </summary>
public sealed partial class ClaimService(
    ClaimFlowDbContext db,
    IClaimReferenceGenerator references,
    TimeProvider clock,
    IOptions<DemoOptions> demoOptions,
    ILogger<ClaimService> logger)
{
    private const int MaxReferenceAttempts = 3;

    public async Task<PagedResult<ClaimSummaryDto>> SearchAsync(ClaimQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var claims = db.Claims.AsNoTracking();

        if (query.Status is { } status)
        {
            claims = claims.Where(c => c.Status == status);
        }

        if (query.Category is { } category)
        {
            claims = claims.Where(c => c.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(query.PolicyNumber))
        {
            var policyNumber = query.PolicyNumber.Trim().ToUpperInvariant();
            claims = claims.Where(c => c.Policy!.PolicyNumber == policyNumber);
        }

        if (query.IncidentFrom is { } from)
        {
            claims = claims.Where(c => c.IncidentDate >= from);
        }

        if (query.IncidentTo is { } to)
        {
            claims = claims.Where(c => c.IncidentDate <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // LIKE works on both providers. User input is escaped so % and _ are matched literally.
            var pattern = $"%{EscapeLikePattern(query.Search.Trim())}%";
            claims = claims.Where(c =>
                EF.Functions.Like(c.Reference, pattern, "\\") ||
                EF.Functions.Like(c.Policy!.PolicyNumber, pattern, "\\") ||
                EF.Functions.Like(c.Description, pattern, "\\"));
        }

        claims = query.Sort switch
        {
            ClaimSort.Oldest => claims.OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id),
            ClaimSort.IncidentDate => claims.OrderByDescending(c => c.IncidentDate).ThenByDescending(c => c.Id),
            ClaimSort.AmountHighest => claims.OrderByDescending(c => c.ClaimedAmount).ThenByDescending(c => c.Id),
            _ => claims.OrderByDescending(c => c.CreatedAtUtc).ThenByDescending(c => c.Id),
        };

        var total = await claims.CountAsync(cancellationToken);
        var items = await claims
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(ToSummary)
            .ToListAsync(cancellationToken);

        return new PagedResult<ClaimSummaryDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<ClaimDetailDto> GetAsync(string reference, CancellationToken cancellationToken)
    {
        var claim = await LoadClaimAsync(reference, tracking: false, cancellationToken);
        return ToDetail(claim);
    }

    public async Task<IReadOnlyList<StatusChangeDto>> GetHistoryAsync(string reference, CancellationToken cancellationToken)
    {
        var claim = await LoadClaimAsync(reference, tracking: false, cancellationToken);
        return ToHistory(claim);
    }

    public async Task<ClaimDetailDto> CreateAsync(CreateClaimRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var limit = demoOptions.Value.MaxVisitorClaims;
        var visitorClaims = await db.Claims.CountAsync(c => !c.IsDemoSeed, cancellationToken);
        if (visitorClaims >= limit)
        {
            LogDemoLimitReached(logger, limit);
            throw new DemoLimitReachedException(limit);
        }

        var policyNumber = request.PolicyNumber!.Trim().ToUpperInvariant();
        var policy = await db.Policies.SingleOrDefaultAsync(p => p.PolicyNumber == policyNumber, cancellationToken);
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        ValidateAgainstPolicy(request, policy, DateOnly.FromDateTime(nowUtc));

        for (var attempt = 1; ; attempt++)
        {
            var claim = Claim.Submit(
                references.Create(nowUtc),
                policy!,
                request.Category!.Value,
                request.IncidentDate!.Value,
                request.ClaimedAmount!.Value,
                request.Description!.Trim(),
                nowUtc);

            db.Claims.Add(claim);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                LogClaimCreated(logger, claim.Reference, claim.Category, policy!.PolicyNumber);
                return ToDetail(claim);
            }
            catch (DbUpdateException) when (attempt < MaxReferenceAttempts)
            {
                // Detach the failed claim so it is not saved again with the next attempt.
                db.Entry(claim).State = EntityState.Detached;
                foreach (var entry in claim.StatusHistory)
                {
                    db.Entry(entry).State = EntityState.Detached;
                }

                // Only a reference collision is worth retrying; anything else is a real failure.
                if (!await ReferenceExistsAsync(claim.Reference, cancellationToken))
                {
                    throw;
                }
            }
        }
    }

    public async Task<ClaimDetailDto> TransitionAsync(string reference, TransitionClaimRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var claim = await LoadClaimAsync(reference, tracking: true, cancellationToken);
        var from = claim.Status;
        var target = request.TargetStatus!.Value;

        try
        {
            claim.TransitionTo(target, request.Reason, clock.GetUtcNow().UtcDateTime);
        }
        catch (InvalidStatusTransitionException)
        {
            LogTransitionRejected(logger, claim.Reference, from, target);
            throw;
        }

        // Version is a concurrency token: if someone else changed this claim since we loaded it,
        // SaveChanges throws DbUpdateConcurrencyException, which becomes a 409 response.
        await db.SaveChangesAsync(cancellationToken);
        LogStatusChanged(logger, claim.Reference, from, target);
        return ToDetail(claim);
    }

    private static void ValidateAgainstPolicy(CreateClaimRequest request, Policy? policy, DateOnly today)
    {
        var errors = new Dictionary<string, string[]>();
        if (policy is null)
        {
            errors[nameof(request.PolicyNumber)] = ["No policy exists with this number. Try one of the DEMO-POL policies."];
            throw new DomainValidationException(errors);
        }

        var incidentDate = request.IncidentDate!.Value;
        if (incidentDate > today)
        {
            errors[nameof(request.IncidentDate)] = ["Incident date cannot be in the future."];
        }
        else if (!policy.IsActiveOn(incidentDate))
        {
            errors[nameof(request.IncidentDate)] =
                [$"Incident date must be within the policy period ({policy.ValidFrom:yyyy-MM-dd} to {policy.ValidTo:yyyy-MM-dd})."];
        }

        if (request.Category != policy.CoverageCategory)
        {
            errors[nameof(request.Category)] = [$"Policy {policy.PolicyNumber} covers {policy.CoverageCategory} claims only."];
        }

        if (request.ClaimedAmount > policy.CoverageLimit)
        {
            errors[nameof(request.ClaimedAmount)] = [$"Claimed amount exceeds the policy's coverage limit of {policy.CoverageLimit:N0} NOK."];
        }

        if (errors.Count > 0)
        {
            throw new DomainValidationException(errors);
        }
    }

    private async Task<Claim> LoadClaimAsync(string reference, bool tracking, CancellationToken cancellationToken)
    {
        var normalized = reference.Trim().ToUpperInvariant();
        var query = db.Claims
            .Include(c => c.Policy)
            .Include(c => c.StatusHistory.OrderBy(h => h.ChangedAtUtc).ThenBy(h => h.Id))
            .AsSplitQuery();

        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(c => c.Reference == normalized, cancellationToken)
            ?? throw new NotFoundException("Claim", normalized);
    }

    private Task<bool> ReferenceExistsAsync(string reference, CancellationToken cancellationToken) =>
        db.Claims.AsNoTracking().AnyAsync(c => c.Reference == reference, cancellationToken);

    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal);

    private static readonly System.Linq.Expressions.Expression<Func<Claim, ClaimSummaryDto>> ToSummary = c =>
        new ClaimSummaryDto(c.Reference, c.Policy!.PolicyNumber, c.Category, c.IncidentDate, c.ClaimedAmount, c.Status, c.CreatedAtUtc, c.UpdatedAtUtc);

    private static ClaimDetailDto ToDetail(Claim claim) =>
        new(
            claim.Reference,
            claim.Policy!.PolicyNumber,
            claim.Policy.HolderName,
            claim.Category,
            claim.IncidentDate,
            claim.ClaimedAmount,
            claim.Description,
            claim.Status,
            claim.IsDemoSeed,
            claim.CreatedAtUtc,
            claim.UpdatedAtUtc,
            ClaimWorkflow.NextStatuses(claim.Status),
            ToHistory(claim));

    private static List<StatusChangeDto> ToHistory(Claim claim) =>
        claim.StatusHistory
            .OrderBy(h => h.ChangedAtUtc)
            .ThenBy(h => h.Id)
            .Select(h => new StatusChangeDto(h.FromStatus, h.ToStatus, h.Reason, h.ChangedAtUtc))
            .ToList();

    // Structured logs carry references and statuses only, never descriptions or client details.
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Claim {ClaimReference} created ({Category}) on policy {PolicyNumber}")]
    private static partial void LogClaimCreated(ILogger logger, string claimReference, ClaimCategory category, string policyNumber);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Claim {ClaimReference} moved from {FromStatus} to {ToStatus}")]
    private static partial void LogStatusChanged(ILogger logger, string claimReference, ClaimStatus fromStatus, ClaimStatus toStatus);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "Rejected transition for claim {ClaimReference} from {FromStatus} to {ToStatus}")]
    private static partial void LogTransitionRejected(ILogger logger, string claimReference, ClaimStatus fromStatus, ClaimStatus toStatus);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "Visitor claim limit of {Limit} reached; creation refused")]
    private static partial void LogDemoLimitReached(ILogger logger, int limit);
}
