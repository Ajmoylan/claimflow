using ClaimFlow.Api.Domain;

namespace ClaimFlow.Api.Data;

/// <summary>
/// The synthetic dataset the demo starts from and returns to on every reset.
/// Every name, number and story here is invented. None of it is real customer data.
/// </summary>
public static class DemoData
{
    public static IReadOnlyList<Policy> CreatePolicies() =>
    [
        NewPolicy("DEMO-POL-1001", "Fictional Customer A", ClaimCategory.Motor, 500_000m, new(2025, 1, 1), new(2027, 12, 31)),
        NewPolicy("DEMO-POL-1002", "Fictional Customer B", ClaimCategory.Home, 3_000_000m, new(2025, 3, 1), new(2027, 12, 31)),
        NewPolicy("DEMO-POL-1003", "Fictional Customer C", ClaimCategory.Contents, 750_000m, new(2025, 6, 1), new(2027, 12, 31)),
        NewPolicy("DEMO-POL-1004", "Fictional Customer D", ClaimCategory.Travel, 150_000m, new(2026, 1, 1), new(2027, 12, 31)),
        NewPolicy("DEMO-POL-1005", "Fictional Customer E", ClaimCategory.Pet, 60_000m, new(2025, 9, 1), new(2027, 12, 31)),
        NewPolicy("DEMO-POL-1006", "Fictional Customer F", ClaimCategory.Motor, 400_000m, new(2026, 2, 1), new(2027, 12, 31)),
        NewPolicy("DEMO-POL-1007", "Fictional Customer G", ClaimCategory.Home, 2_500_000m, new(2025, 1, 1), new(2027, 12, 31)),
        // Expired on purpose, so the "incident must fall within the policy period" rule can be demonstrated.
        NewPolicy("DEMO-POL-1008", "Fictional Customer H", ClaimCategory.Travel, 100_000m, new(2024, 1, 1), new(2024, 12, 31)),
    ];

    /// <summary>Builds the example claims, including their audit history, for the given (already saved) policies.</summary>
    public static IReadOnlyList<Claim> CreateClaims(IReadOnlyDictionary<string, Policy> policiesByNumber)
    {
        var claims = new List<Claim>();

        void Add(
            string reference,
            string policyNumber,
            DateOnly incidentDate,
            decimal amount,
            string description,
            DateTime submittedUtc,
            params (ClaimStatus Status, string? Reason, int DaysLater)[] steps)
        {
            var policy = policiesByNumber[policyNumber];
            var claim = Claim.Submit(reference, policy, policy.CoverageCategory, incidentDate, amount, description, submittedUtc, isDemoSeed: true);
            foreach (var (status, reason, daysLater) in steps)
            {
                claim.TransitionTo(status, reason, submittedUtc.AddDays(daysLater));
            }

            claims.Add(claim);
        }

        var review = (ClaimStatus.UnderReview, (string?)"Assigned to a claims handler", 1);

        Add("CLM-2026-D00001", "DEMO-POL-1001", new(2026, 8, 14), 18_450.00m,
            "Rear bumper damaged in a low-speed car park collision. Repair quote attached.",
            Utc(2026, 8, 15, 9, 12), review, (ClaimStatus.Approved, "Repair quote within normal range", 6));
        Add("CLM-2026-D00002", "DEMO-POL-1002", new(2026, 8, 20), 64_000.00m,
            "Water leak from a burst pipe under the kitchen sink damaged flooring and cabinets.",
            Utc(2026, 8, 21, 7, 40), review, (ClaimStatus.Approved, "Damage confirmed by inspection report", 9));
        Add("CLM-2026-D00003", "DEMO-POL-1003", new(2026, 8, 29), 23_990.00m,
            "Laptop and headphones stolen from a locked apartment storage room.",
            Utc(2026, 8, 30, 18, 5), review, (ClaimStatus.Rejected, "Storage room not covered under this contents policy", 7));
        Add("CLM-2026-D00004", "DEMO-POL-1004", new(2026, 9, 3), 7_850.00m,
            "Flight cancelled due to a storm; extra hotel night and meals abroad.",
            Utc(2026, 9, 6, 11, 0), review, (ClaimStatus.Approved, "Receipts verified", 3));
        Add("CLM-2026-D00005", "DEMO-POL-1005", new(2026, 9, 10), 12_300.00m,
            "Veterinary treatment after the dog swallowed a small toy.",
            Utc(2026, 9, 10, 20, 30), review, (ClaimStatus.Approved, "Treatment covered", 4));
        Add("CLM-2026-D00006", "DEMO-POL-1006", new(2026, 9, 18), 142_000.00m,
            "Car written off after hitting a moose on a rural road. No injuries.",
            Utc(2026, 9, 19, 6, 55), review, (ClaimStatus.Approved, "Total loss valuation agreed", 12));
        Add("CLM-2026-D00007", "DEMO-POL-1007", new(2026, 9, 25), 310_000.00m,
            "Roof damaged by a fallen tree during an autumn storm.",
            Utc(2026, 9, 26, 8, 15), review);
        Add("CLM-2026-D00008", "DEMO-POL-1001", new(2026, 10, 2), 4_200.00m,
            "Windscreen cracked by a stone chip on the motorway.",
            Utc(2026, 10, 2, 16, 45), review, (ClaimStatus.Approved, "Glass damage, standard excess applied", 2));
        Add("CLM-2026-D00009", "DEMO-POL-1004", new(2026, 10, 7), 31_500.00m,
            "Emergency dental treatment while travelling outside Norway.",
            Utc(2026, 10, 9, 10, 10), review, (ClaimStatus.Rejected, "Pre-existing condition excluded by the policy terms", 8));
        Add("CLM-2026-D00010", "DEMO-POL-1002", new(2026, 10, 12), 9_800.00m,
            "Smoke damage to a living room wall after a candle fire.",
            Utc(2026, 10, 13, 12, 0), review);
        Add("CLM-2026-D00011", "DEMO-POL-1003", new(2026, 10, 19), 15_600.00m,
            "Bicycle stolen from a shared bike room despite being locked.",
            Utc(2026, 10, 20, 9, 30), review);
        Add("CLM-2026-D00012", "DEMO-POL-1005", new(2026, 10, 24), 5_400.00m,
            "Cat treated for an eye infection.",
            Utc(2026, 10, 25, 14, 20), review, (ClaimStatus.Rejected, "Routine treatment below the policy excess", 5));
        Add("CLM-2026-D00013", "DEMO-POL-1006", new(2026, 10, 30), 27_750.00m,
            "Side mirror and door scratched by an unknown vehicle while parked.",
            Utc(2026, 10, 31, 8, 0), review);
        Add("CLM-2026-D00014", "DEMO-POL-1007", new(2026, 11, 3), 48_200.00m,
            "Basement flooded after heavy rain; dehumidifier hire and cleaning.",
            Utc(2026, 11, 4, 7, 25));
        Add("CLM-2026-D00015", "DEMO-POL-1001", new(2026, 11, 6), 11_900.00m,
            "Minor collision at a roundabout; front wing and headlight damaged.",
            Utc(2026, 11, 6, 17, 50));
        Add("CLM-2026-D00016", "DEMO-POL-1004", new(2026, 11, 8), 2_650.00m,
            "Luggage delayed for three days; essential clothing purchased.",
            Utc(2026, 11, 10, 13, 5));
        Add("CLM-2026-D00017", "DEMO-POL-1003", new(2026, 11, 9), 8_499.00m,
            "Television damaged by a power surge during a thunderstorm.",
            Utc(2026, 11, 10, 19, 40));
        Add("CLM-2026-D00018", "DEMO-POL-1002", new(2026, 11, 11), 125_000.00m,
            "Bathroom water damage discovered during renovation.",
            Utc(2026, 11, 12, 10, 0));

        return claims;
    }

    private static Policy NewPolicy(string number, string holder, ClaimCategory category, decimal limit, DateOnly from, DateOnly to) =>
        new()
        {
            PolicyNumber = number,
            HolderName = holder,
            CoverageCategory = category,
            CoverageLimit = limit,
            ValidFrom = from,
            ValidTo = to,
        };

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}
