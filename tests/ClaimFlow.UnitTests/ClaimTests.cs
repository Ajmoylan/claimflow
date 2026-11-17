using ClaimFlow.Api.Domain;

namespace ClaimFlow.UnitTests;

public class ClaimTests
{
    private static readonly DateTime Now = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

    private static Claim NewClaim() =>
        Claim.Submit(
            "CLM-2026-TEST01",
            new Policy
            {
                Id = 7,
                PolicyNumber = "DEMO-POL-9999",
                HolderName = "Fictional Test Holder",
                CoverageCategory = ClaimCategory.Motor,
                CoverageLimit = 100_000m,
                ValidFrom = new(2026, 1, 1),
                ValidTo = new(2026, 12, 31),
            },
            ClaimCategory.Motor,
            new DateOnly(2026, 11, 1),
            1_000m,
            "A test claim description",
            Now);

    [Fact]
    public void Submit_StartsInSubmitted_WithOneAuditEntry()
    {
        var claim = NewClaim();

        Assert.Equal(ClaimStatus.Submitted, claim.Status);
        Assert.Equal(7, claim.PolicyId);
        Assert.Equal(1, claim.Version);
        var entry = Assert.Single(claim.StatusHistory);
        Assert.Null(entry.FromStatus);
        Assert.Equal(ClaimStatus.Submitted, entry.ToStatus);
        Assert.Equal(Now, entry.ChangedAtUtc);
    }

    [Fact]
    public void TransitionTo_AllowedStep_UpdatesStatusVersionAndHistory()
    {
        var claim = NewClaim();
        var later = Now.AddHours(2);

        var change = claim.TransitionTo(ClaimStatus.UnderReview, "  Assigned  ", later);

        Assert.Equal(ClaimStatus.UnderReview, claim.Status);
        Assert.Equal(2, claim.Version);
        Assert.Equal(later, claim.UpdatedAtUtc);
        Assert.Equal(ClaimStatus.Submitted, change.FromStatus);
        Assert.Equal(ClaimStatus.UnderReview, change.ToStatus);
        Assert.Equal("Assigned", change.Reason);
        Assert.Equal(2, claim.StatusHistory.Count);
    }

    [Fact]
    public void TransitionTo_SkippingReview_ThrowsAndChangesNothing()
    {
        var claim = NewClaim();

        var ex = Assert.Throws<InvalidStatusTransitionException>(() => claim.TransitionTo(ClaimStatus.Approved, null, Now));

        Assert.Equal(ClaimStatus.Submitted, ex.From);
        Assert.Equal(ClaimStatus.Approved, ex.To);
        Assert.Equal(ClaimStatus.Submitted, claim.Status);
        Assert.Equal(1, claim.Version);
        Assert.Single(claim.StatusHistory);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TransitionTo_RejectWithoutReason_ThrowsValidationError(string? reason)
    {
        var claim = NewClaim();
        claim.TransitionTo(ClaimStatus.UnderReview, null, Now);

        var ex = Assert.Throws<DomainValidationException>(() => claim.TransitionTo(ClaimStatus.Rejected, reason, Now));

        Assert.True(ex.Errors.ContainsKey("reason"));
        Assert.Equal(ClaimStatus.UnderReview, claim.Status);
    }

    [Fact]
    public void TransitionTo_FromFinalStatus_Throws()
    {
        var claim = NewClaim();
        claim.TransitionTo(ClaimStatus.UnderReview, null, Now);
        claim.TransitionTo(ClaimStatus.Approved, null, Now);

        Assert.Throws<InvalidStatusTransitionException>(() => claim.TransitionTo(ClaimStatus.Rejected, "Changed my mind", Now));
        Assert.Equal(ClaimStatus.Approved, claim.Status);
    }

    [Theory]
    [InlineData(2026, 1, 1, true)]
    [InlineData(2026, 12, 31, true)]
    [InlineData(2025, 12, 31, false)]
    [InlineData(2027, 1, 1, false)]
    public void Policy_IsActiveOn_IncludesBothEnds(int year, int month, int day, bool expected)
    {
        var policy = new Policy { PolicyNumber = "P", HolderName = "H", ValidFrom = new(2026, 1, 1), ValidTo = new(2026, 12, 31) };
        Assert.Equal(expected, policy.IsActiveOn(new DateOnly(year, month, day)));
    }
}
