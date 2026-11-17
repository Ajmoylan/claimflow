using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using ClaimFlow.Api.Contracts;
using ClaimFlow.Api.Data;
using ClaimFlow.Api.Domain;
using ClaimFlow.Api.Services;

namespace ClaimFlow.UnitTests;

public partial class ClaimReferenceGeneratorTests
{
    [GeneratedRegex("^CLM-2026-[0-9A-HJKMNP-TV-Z]{6}$")]
    private static partial Regex ReferenceFormat();

    [Fact]
    public void Create_UsesYearAndUnambiguousCharacters()
    {
        var generator = new ClaimReferenceGenerator();
        var now = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 1_000; i++)
        {
            Assert.Matches(ReferenceFormat(), generator.Create(now));
        }
    }

    [Fact]
    public void Create_IsRandomEnoughThatCollisionsAreRare()
    {
        var generator = new ClaimReferenceGenerator();
        var now = DateTime.UtcNow;
        var references = Enumerable.Range(0, 10_000).Select(_ => generator.Create(now)).ToHashSet();

        // 32^6 ≈ 1.07 billion combinations; 10 000 draws should essentially never collide.
        Assert.True(references.Count >= 9_998);
    }
}

public class DemoResetScheduleTests
{
    private static readonly DemoOptions Options = new() { ResetTimeOfDay = new TimeOnly(3, 0), ResetTimeZone = "Europe/Oslo" };

    [Fact]
    public void BeforeThreeOsloTime_UsesYesterdaysReset()
    {
        // 01:30 UTC on 15 Nov = 02:30 in Oslo (UTC+1 in winter): today's 03:00 has not happened yet.
        var now = new DateTime(2026, 11, 15, 1, 30, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 11, 14, 2, 0, 0, DateTimeKind.Utc), DemoDataService.MostRecentScheduledResetUtc(now, Options));
    }

    [Fact]
    public void AfterThreeOsloTime_UsesTodaysReset()
    {
        var now = new DateTime(2026, 11, 15, 2, 30, 0, DateTimeKind.Utc); // 03:30 in Oslo
        Assert.Equal(new DateTime(2026, 11, 15, 2, 0, 0, DateTimeKind.Utc), DemoDataService.MostRecentScheduledResetUtc(now, Options));
    }

    [Fact]
    public void InSummer_AccountsForDaylightSavingTime()
    {
        var now = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc); // Oslo is UTC+2 in summer
        Assert.Equal(new DateTime(2026, 7, 1, 1, 0, 0, DateTimeKind.Utc), DemoDataService.MostRecentScheduledResetUtc(now, Options));
    }
}

public class RequestValidationTests
{
    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    private static CreateClaimRequest ValidRequest() => new()
    {
        PolicyNumber = "DEMO-POL-1001",
        Category = ClaimCategory.Motor,
        IncidentDate = new DateOnly(2026, 11, 1),
        ClaimedAmount = 1_234.50m,
        Description = "Bumper damaged in a car park",
    };

    [Fact]
    public void ValidRequest_HasNoErrors() => Assert.Empty(Validate(ValidRequest()));

    [Fact]
    public void MissingFields_AreAllReported()
    {
        var errors = Validate(new CreateClaimRequest());
        var fields = errors.SelectMany(e => e.MemberNames).ToHashSet();

        Assert.Superset(
            new HashSet<string> { "PolicyNumber", "Category", "IncidentDate", "ClaimedAmount", "Description" },
            fields);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10_000_001)]
    public void AmountOutsideRange_IsRejected(decimal amount) =>
        Assert.Contains(Validate(ValidRequest() with { ClaimedAmount = amount }), e => e.MemberNames.Contains("ClaimedAmount"));

    [Fact]
    public void AmountWithMoreThanTwoDecimals_IsRejected() =>
        Assert.Contains(Validate(ValidRequest() with { ClaimedAmount = 10.005m }), e => e.MemberNames.Contains("ClaimedAmount"));

    [Theory]
    [InlineData("DEMO POL")]
    [InlineData("DEMO-POL-1001; DROP TABLE")]
    [InlineData("<script>")]
    public void PolicyNumberWithUnexpectedCharacters_IsRejected(string policyNumber) =>
        Assert.Contains(Validate(ValidRequest() with { PolicyNumber = policyNumber }), e => e.MemberNames.Contains("PolicyNumber"));

    [Fact]
    public void QueryWithReversedDateRange_IsRejected()
    {
        var query = new ClaimQuery { IncidentFrom = new DateOnly(2026, 12, 1), IncidentTo = new DateOnly(2026, 1, 1) };
        Assert.Contains(Validate(query), e => e.MemberNames.Contains("IncidentFrom"));
    }
}

public class DemoDataTests
{
    [Fact]
    public void SeedClaims_AreConsistentWithTheirPolicies()
    {
        var policies = DemoData.CreatePolicies().ToDictionary(p => p.PolicyNumber);
        var claims = DemoData.CreateClaims(policies);

        Assert.NotEmpty(claims);
        Assert.Equal(claims.Count, claims.Select(c => c.Reference).Distinct().Count());
        foreach (var claim in claims)
        {
            var policy = claim.Policy!;
            Assert.True(claim.IsDemoSeed);
            Assert.Equal(policy.CoverageCategory, claim.Category);
            Assert.True(policy.IsActiveOn(claim.IncidentDate), $"{claim.Reference} incident outside policy period");
            Assert.InRange(claim.ClaimedAmount, 1m, policy.CoverageLimit);
            Assert.Equal(claim.StatusHistory.Count, claim.Version);
        }
    }

    [Fact]
    public void SeedClaims_CoverEveryStatus()
    {
        var claims = DemoData.CreateClaims(DemoData.CreatePolicies().ToDictionary(p => p.PolicyNumber));
        Assert.All(Enum.GetValues<ClaimStatus>(), status => Assert.Contains(claims, c => c.Status == status));
    }
}
