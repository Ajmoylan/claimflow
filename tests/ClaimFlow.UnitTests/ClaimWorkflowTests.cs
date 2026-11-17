using ClaimFlow.Api.Domain;

namespace ClaimFlow.UnitTests;

public class ClaimWorkflowTests
{
    public static TheoryData<ClaimStatus, ClaimStatus, bool> AllTransitions()
    {
        var allowed = new HashSet<(ClaimStatus, ClaimStatus)>
        {
            (ClaimStatus.Submitted, ClaimStatus.UnderReview),
            (ClaimStatus.UnderReview, ClaimStatus.Approved),
            (ClaimStatus.UnderReview, ClaimStatus.Rejected),
        };

        var data = new TheoryData<ClaimStatus, ClaimStatus, bool>();
        foreach (var from in Enum.GetValues<ClaimStatus>())
        {
            foreach (var to in Enum.GetValues<ClaimStatus>())
            {
                data.Add(from, to, allowed.Contains((from, to)));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllTransitions))]
    public void CanTransition_MatchesTheDocumentedWorkflow(ClaimStatus from, ClaimStatus to, bool expected) =>
        Assert.Equal(expected, ClaimWorkflow.CanTransition(from, to));

    [Theory]
    [InlineData(ClaimStatus.Approved)]
    [InlineData(ClaimStatus.Rejected)]
    public void FinalStatuses_HaveNoNextStatus(ClaimStatus status)
    {
        Assert.True(ClaimWorkflow.IsFinal(status));
        Assert.Empty(ClaimWorkflow.NextStatuses(status));
    }

    [Fact]
    public void UnderReview_CanBeApprovedOrRejected() =>
        Assert.Equal([ClaimStatus.Approved, ClaimStatus.Rejected], ClaimWorkflow.NextStatuses(ClaimStatus.UnderReview));

    [Theory]
    [InlineData(ClaimStatus.Rejected, true)]
    [InlineData(ClaimStatus.Approved, false)]
    [InlineData(ClaimStatus.UnderReview, false)]
    public void OnlyRejectionRequiresAReason(ClaimStatus target, bool expected) =>
        Assert.Equal(expected, ClaimWorkflow.RequiresReason(target));
}
