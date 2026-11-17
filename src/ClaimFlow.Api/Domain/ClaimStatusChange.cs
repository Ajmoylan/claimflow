namespace ClaimFlow.Api.Domain;

/// <summary>One row of a claim's audit history. Rows are only ever appended, never edited.</summary>
public class ClaimStatusChange
{
    public const int ReasonMaxLength = 500;

    public long Id { get; set; }

    public int ClaimId { get; set; }

    public Claim? Claim { get; set; }

    /// <summary>Null for the first entry, which records the claim being submitted.</summary>
    public ClaimStatus? FromStatus { get; set; }

    public ClaimStatus ToStatus { get; set; }

    public string? Reason { get; set; }

    public DateTime ChangedAtUtc { get; set; }
}
