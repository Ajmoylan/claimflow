namespace ClaimFlow.Api.Domain;

/// <summary>Where a claim is in the handling workflow.</summary>
public enum ClaimStatus
{
    Submitted,
    UnderReview,
    Approved,
    Rejected,
}

/// <summary>
/// The kind of loss being claimed. A policy covers exactly one category,
/// which keeps the "category must match the policy" rule easy to reason about.
/// </summary>
public enum ClaimCategory
{
    Motor,
    Home,
    Contents,
    Travel,
    Pet,
}
