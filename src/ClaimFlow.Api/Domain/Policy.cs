namespace ClaimFlow.Api.Domain;

/// <summary>A fictional insurance policy. Policies are reference data: the demo never lets visitors change them.</summary>
public class Policy
{
    public const int PolicyNumberMaxLength = 20;
    public const int HolderNameMaxLength = 100;

    public int Id { get; set; }

    /// <summary>Business key, e.g. DEMO-POL-1001.</summary>
    public required string PolicyNumber { get; set; }

    /// <summary>Clearly fictional holder label, never a real person.</summary>
    public required string HolderName { get; set; }

    public ClaimCategory CoverageCategory { get; set; }

    /// <summary>Maximum amount (NOK) a single claim may request.</summary>
    public decimal CoverageLimit { get; set; }

    public DateOnly ValidFrom { get; set; }

    public DateOnly ValidTo { get; set; }

    public List<Claim> Claims { get; } = [];

    public bool IsActiveOn(DateOnly date) => date >= ValidFrom && date <= ValidTo;
}
