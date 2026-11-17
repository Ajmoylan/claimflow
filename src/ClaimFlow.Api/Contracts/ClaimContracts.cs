using System.ComponentModel.DataAnnotations;
using ClaimFlow.Api.Domain;

namespace ClaimFlow.Api.Contracts;

// Request and response shapes for the HTTP API. Entities are never returned directly,
// so the database model can change without breaking clients and nothing leaks by accident.

/// <summary>Body of <c>POST /api/claims</c>.</summary>
public sealed record CreateClaimRequest : IValidatableObject
{
    /// <summary>Fictional policy number, e.g. DEMO-POL-1001.</summary>
    [Required]
    [RegularExpression(@"^[A-Za-z0-9-]{3,20}$", ErrorMessage = "Policy number may only contain letters, digits and hyphens (3–20 characters).")]
    public string? PolicyNumber { get; init; }

    [Required]
    public ClaimCategory? Category { get; init; }

    [Required]
    public DateOnly? IncidentDate { get; init; }

    /// <summary>Amount in NOK, at most two decimal places.</summary>
    [Required]
    [Range(typeof(decimal), "1", "10000000", ErrorMessage = "Claimed amount must be between 1 and 10 000 000 NOK.")]
    public decimal? ClaimedAmount { get; init; }

    [Required]
    [StringLength(Claim.DescriptionMaxLength, MinimumLength = Claim.DescriptionMinLength)]
    public string? Description { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ClaimedAmount is { } amount && decimal.Round(amount, 2) != amount)
        {
            yield return new ValidationResult("Claimed amount can have at most two decimal places.", [nameof(ClaimedAmount)]);
        }

        if (Description is not null && string.IsNullOrWhiteSpace(Description))
        {
            yield return new ValidationResult("Description cannot be blank.", [nameof(Description)]);
        }
    }
}

/// <summary>Body of <c>POST /api/claims/{reference}/transitions</c>.</summary>
public sealed record TransitionClaimRequest
{
    [Required]
    public ClaimStatus? TargetStatus { get; init; }

    /// <summary>Required when rejecting; optional otherwise.</summary>
    [StringLength(ClaimStatusChange.ReasonMaxLength)]
    public string? Reason { get; init; }
}

/// <summary>Query string of <c>GET /api/claims</c>. Every filter is optional.</summary>
public sealed record ClaimQuery : IValidatableObject
{
    public const int MaxPageSize = 100;

    /// <summary>Free text matched against claim reference, policy number and description.</summary>
    [StringLength(100)]
    public string? Search { get; init; }

    public ClaimStatus? Status { get; init; }

    public ClaimCategory? Category { get; init; }

    [StringLength(Policy.PolicyNumberMaxLength)]
    public string? PolicyNumber { get; init; }

    public DateOnly? IncidentFrom { get; init; }

    public DateOnly? IncidentTo { get; init; }

    public ClaimSort Sort { get; init; } = ClaimSort.Newest;

    [Range(1, 10_000)]
    public int Page { get; init; } = 1;

    [Range(1, MaxPageSize)]
    public int PageSize { get; init; } = 20;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (IncidentFrom is { } from && IncidentTo is { } to && from > to)
        {
            yield return new ValidationResult("IncidentFrom must be on or before IncidentTo.", [nameof(IncidentFrom), nameof(IncidentTo)]);
        }
    }
}

public enum ClaimSort
{
    Newest,
    Oldest,
    IncidentDate,
    AmountHighest,
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record ClaimSummaryDto(
    string Reference,
    string PolicyNumber,
    ClaimCategory Category,
    DateOnly IncidentDate,
    decimal ClaimedAmount,
    ClaimStatus Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record ClaimDetailDto(
    string Reference,
    string PolicyNumber,
    string PolicyHolder,
    ClaimCategory Category,
    DateOnly IncidentDate,
    decimal ClaimedAmount,
    string Description,
    ClaimStatus Status,
    bool IsDemoSeed,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<ClaimStatus> AllowedNextStatuses,
    IReadOnlyList<StatusChangeDto> History);

public sealed record StatusChangeDto(ClaimStatus? FromStatus, ClaimStatus ToStatus, string? Reason, DateTime ChangedAtUtc);

public sealed record PolicyDto(
    string PolicyNumber,
    string HolderName,
    ClaimCategory CoverageCategory,
    decimal CoverageLimit,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    bool IsActive);

public sealed record DashboardSummaryDto(
    int TotalClaims,
    IReadOnlyDictionary<ClaimStatus, int> CountByStatus,
    IReadOnlyDictionary<ClaimCategory, int> CountByCategory,
    decimal TotalClaimedAmount,
    decimal TotalApprovedAmount,
    double? ApprovalRate,
    int OpenClaims,
    IReadOnlyList<ClaimSummaryDto> RecentClaims,
    DemoInfoDto Demo);

public sealed record DemoInfoDto(int VisitorClaims, int MaxVisitorClaims, DateTime? LastResetAtUtc, DateTime NextResetAtUtc);
