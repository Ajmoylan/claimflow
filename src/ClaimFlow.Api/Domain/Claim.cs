namespace ClaimFlow.Api.Domain;

/// <summary>
/// An insurance claim. Status can only change through <see cref="TransitionTo"/>,
/// so every change is checked against <see cref="ClaimWorkflow"/> and recorded in the audit history.
/// </summary>
public class Claim
{
    public const int ReferenceMaxLength = 20;
    public const int DescriptionMinLength = 10;
    public const int DescriptionMaxLength = 1000;

    public int Id { get; set; }

    /// <summary>Business key shown to users, e.g. CLM-2026-7KQ2MD.</summary>
    public required string Reference { get; set; }

    public int PolicyId { get; set; }

    public Policy? Policy { get; set; }

    public ClaimCategory Category { get; set; }

    public DateOnly IncidentDate { get; set; }

    /// <summary>Amount claimed in NOK.</summary>
    public decimal ClaimedAmount { get; set; }

    public required string Description { get; set; }

    public ClaimStatus Status { get; private set; } = ClaimStatus.Submitted;

    /// <summary>True for the example claims restored by the demo reset; false for visitor-created claims.</summary>
    public bool IsDemoSeed { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Optimistic concurrency token, incremented on every change (works the same on SQLite and SQL Server).</summary>
    public int Version { get; set; }

    public List<ClaimStatusChange> StatusHistory { get; } = [];

    /// <summary>Creates a new claim in the Submitted state with its first audit entry.</summary>
    public static Claim Submit(
        string reference,
        Policy policy,
        ClaimCategory category,
        DateOnly incidentDate,
        decimal claimedAmount,
        string description,
        DateTime nowUtc,
        bool isDemoSeed = false)
    {
        var claim = new Claim
        {
            Reference = reference,
            Policy = policy,
            PolicyId = policy.Id,
            Category = category,
            IncidentDate = incidentDate,
            ClaimedAmount = claimedAmount,
            Description = description,
            IsDemoSeed = isDemoSeed,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            Version = 1,
        };

        claim.StatusHistory.Add(new ClaimStatusChange
        {
            FromStatus = null,
            ToStatus = ClaimStatus.Submitted,
            Reason = "Claim submitted",
            ChangedAtUtc = nowUtc,
        });

        return claim;
    }

    /// <summary>Moves the claim to <paramref name="target"/> if the workflow allows it, and records the change.</summary>
    /// <exception cref="InvalidStatusTransitionException">The workflow does not allow this change.</exception>
    /// <exception cref="DomainValidationException">A reason is required but missing.</exception>
    public ClaimStatusChange TransitionTo(ClaimStatus target, string? reason, DateTime nowUtc)
    {
        if (!ClaimWorkflow.CanTransition(Status, target))
        {
            throw new InvalidStatusTransitionException(Reference, Status, target);
        }

        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (ClaimWorkflow.RequiresReason(target) && trimmedReason is null)
        {
            throw new DomainValidationException("reason", $"A reason is required when moving a claim to {target}.");
        }

        var change = new ClaimStatusChange
        {
            ClaimId = Id,
            FromStatus = Status,
            ToStatus = target,
            Reason = trimmedReason,
            ChangedAtUtc = nowUtc,
        };

        Status = target;
        UpdatedAtUtc = nowUtc;
        Version++;
        StatusHistory.Add(change);
        return change;
    }
}
