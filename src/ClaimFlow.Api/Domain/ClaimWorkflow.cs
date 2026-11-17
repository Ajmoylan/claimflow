namespace ClaimFlow.Api.Domain;

/// <summary>
/// The single source of truth for which status changes are allowed.
/// Submitted → UnderReview → Approved | Rejected. Approved and Rejected are final.
/// </summary>
public static class ClaimWorkflow
{
    private static readonly Dictionary<ClaimStatus, ClaimStatus[]> AllowedTransitions = new()
    {
        [ClaimStatus.Submitted] = [ClaimStatus.UnderReview],
        [ClaimStatus.UnderReview] = [ClaimStatus.Approved, ClaimStatus.Rejected],
        [ClaimStatus.Approved] = [],
        [ClaimStatus.Rejected] = [],
    };

    public static IReadOnlyList<ClaimStatus> NextStatuses(ClaimStatus current) =>
        AllowedTransitions.TryGetValue(current, out var next) ? next : [];

    public static bool CanTransition(ClaimStatus from, ClaimStatus to) => NextStatuses(from).Contains(to);

    public static bool IsFinal(ClaimStatus status) => NextStatuses(status).Count == 0;

    /// <summary>Rejections must always be explained so the audit trail is meaningful.</summary>
    public static bool RequiresReason(ClaimStatus to) => to == ClaimStatus.Rejected;
}
