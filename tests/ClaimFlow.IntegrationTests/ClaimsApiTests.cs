using System.Net;
using System.Net.Http.Json;
using ClaimFlow.Api.Contracts;
using ClaimFlow.Api.Domain;
using ClaimFlow.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClaimFlow.IntegrationTests;

public class ClaimsApiTests(ClaimFlowApiFactory factory) : IntegrationTestBase<ClaimFlowApiFactory>(factory)
{
    // ---------- Retrieval and filtering ----------

    [Fact]
    public async Task List_ReturnsSeedClaims_NewestFirst_WithPaging()
    {
        var page = await ReadAsync<PagedResult<ClaimSummaryDto>>(await Client.GetAsync("/api/claims?pageSize=5", Ct), HttpStatusCode.OK);

        Assert.Equal(18, page.TotalCount);
        Assert.Equal(4, page.TotalPages);
        Assert.Equal(5, page.Items.Count);
        Assert.Equal("CLM-2026-D00018", page.Items[0].Reference);
        Assert.True(page.Items.Zip(page.Items.Skip(1)).All(pair => pair.First.CreatedAtUtc >= pair.Second.CreatedAtUtc));
    }

    [Theory]
    [InlineData("status=Approved", 6)]
    [InlineData("status=UnderReview", 4)]
    [InlineData("category=Motor", 5)]
    [InlineData("policyNumber=demo-pol-1002", 3)]
    [InlineData("incidentFrom=2026-11-01&incidentTo=2026-11-30", 5)]
    [InlineData("status=Approved&category=Motor", 3)]
    public async Task List_AppliesFilters(string query, int expectedCount)
    {
        var page = await ReadAsync<PagedResult<ClaimSummaryDto>>(await Client.GetAsync($"/api/claims?{query}&pageSize=100", Ct), HttpStatusCode.OK);
        Assert.Equal(expectedCount, page.TotalCount);
    }

    [Theory]
    [InlineData("CAR PARK", 1)]        // description, case-insensitive
    [InlineData("d00012", 1)]          // reference
    [InlineData("DEMO-POL-1005", 2)]   // policy number
    [InlineData("100%", 0)]            // % is matched literally, not as a wildcard
    [InlineData("_", 0)]               // _ is matched literally, not as a wildcard
    public async Task List_SearchesReferencePolicyAndDescription(string search, int expectedCount)
    {
        var page = await ReadAsync<PagedResult<ClaimSummaryDto>>(
            await Client.GetAsync($"/api/claims?search={Uri.EscapeDataString(search)}", Ct), HttpStatusCode.OK);
        Assert.Equal(expectedCount, page.TotalCount);
    }

    [Fact]
    public async Task List_SortByAmount_OrdersNumericallyOnEveryProvider()
    {
        var page = await ReadAsync<PagedResult<ClaimSummaryDto>>(
            await Client.GetAsync("/api/claims?sort=AmountHighest&pageSize=100", Ct), HttpStatusCode.OK);
        var amounts = page.Items.Select(c => c.ClaimedAmount).ToList();
        Assert.Equal(amounts.OrderByDescending(a => a), amounts);
    }

    [Theory]
    [InlineData("pageSize=0", "PageSize")]
    [InlineData("pageSize=101", "PageSize")]
    [InlineData("status=Lost", "status")]
    [InlineData("incidentFrom=2026-12-01&incidentTo=2026-01-01", "IncidentFrom")]
    public async Task List_InvalidQuery_Returns400WithFieldError(string query, string field)
    {
        var problem = await ReadProblemAsync(await Client.GetAsync($"/api/claims?{query}", Ct), HttpStatusCode.BadRequest);
        Assert.Contains(problem.Errors!.Keys, key => key.Equals(field, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Get_ReturnsDetailWithHistoryAndAllowedNextStatuses()
    {
        var claim = await ReadAsync<ClaimDetailDto>(await Client.GetAsync("/api/claims/clm-2026-d00007", Ct), HttpStatusCode.OK);

        Assert.Equal("CLM-2026-D00007", claim.Reference);
        Assert.Equal(ClaimStatus.UnderReview, claim.Status);
        Assert.Equal([ClaimStatus.Approved, ClaimStatus.Rejected], claim.AllowedNextStatuses);
        Assert.Equal([ClaimStatus.Submitted, ClaimStatus.UnderReview], claim.History.Select(h => h.ToStatus));
        Assert.Equal(DateTimeKind.Utc, claim.CreatedAtUtc.Kind);
    }

    [Fact]
    public async Task Get_UnknownClaim_Returns404Problem()
    {
        var problem = await ReadProblemAsync(await Client.GetAsync("/api/claims/CLM-2026-NOPE00", Ct), HttpStatusCode.NotFound);
        Assert.Equal("Claim not found", problem.Title);
    }

    [Fact]
    public async Task History_ReturnsEntriesOldestFirst()
    {
        var history = await ReadAsync<List<StatusChangeDto>>(await Client.GetAsync("/api/claims/CLM-2026-D00003/history", Ct), HttpStatusCode.OK);

        Assert.Equal([ClaimStatus.Submitted, ClaimStatus.UnderReview, ClaimStatus.Rejected], history.Select(h => h.ToStatus));
        Assert.Equal("Storage room not covered under this contents policy", history[^1].Reason);
    }

    // ---------- Creation and validation ----------

    [Fact]
    public async Task Create_ValidClaim_Returns201_PersistsIt_AndStartsTheAuditTrail()
    {
        var response = await PostJsonAsync("/api/claims", ValidCreateRequest() with { PolicyNumber = "demo-pol-1001" });
        var created = await ReadAsync<ClaimDetailDto>(response, HttpStatusCode.Created);

        Assert.Matches("^CLM-2026-[0-9A-Z]{6}$", created.Reference);
        Assert.Equal(ClaimStatus.Submitted, created.Status);
        Assert.False(created.IsDemoSeed);
        Assert.Equal(12_500.50m, created.ClaimedAmount);
        Assert.Equal([ClaimStatus.UnderReview], created.AllowedNextStatuses);
        Assert.EndsWith($"/api/claims/{created.Reference}", response.Headers.Location!.ToString());

        var fetched = await ReadAsync<ClaimDetailDto>(await Client.GetAsync(response.Headers.Location, Ct), HttpStatusCode.OK);
        Assert.Equal(created.Reference, fetched.Reference);
        var entry = Assert.Single(fetched.History);
        Assert.Null(entry.FromStatus);
        Assert.Equal(ClaimStatus.Submitted, entry.ToStatus);

        var stored = await Factory.WithDbAsync(db => db.Claims.Include(c => c.StatusHistory).SingleAsync(c => c.Reference == created.Reference, Ct));
        Assert.Equal(12_500.50m, stored.ClaimedAmount);
        Assert.Single(stored.StatusHistory);
    }

    [Fact]
    public async Task Create_EmptyBody_ReportsEveryMissingField()
    {
        var problem = await ReadProblemAsync(await PostJsonAsync("/api/claims", new { }), HttpStatusCode.BadRequest);
        Assert.Superset(
            new HashSet<string>(["PolicyNumber", "Category", "IncidentDate", "ClaimedAmount", "Description"], StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(problem.Errors!.Keys, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Create_UnknownPolicy_Returns400OnPolicyNumber()
    {
        var problem = await ReadProblemAsync(
            await PostJsonAsync("/api/claims", ValidCreateRequest() with { PolicyNumber = "DEMO-POL-0000" }), HttpStatusCode.BadRequest);
        Assert.True(problem.Errors!.ContainsKey("PolicyNumber"));
    }

    [Fact]
    public async Task Create_BreakingSeveralPolicyRules_ReportsEachOne()
    {
        // DEMO-POL-1008 is an expired travel policy with a 100 000 NOK limit.
        var request = ValidCreateRequest() with
        {
            PolicyNumber = "DEMO-POL-1008",
            Category = ClaimCategory.Pet,
            ClaimedAmount = 250_000m,
        };

        var problem = await ReadProblemAsync(await PostJsonAsync("/api/claims", request), HttpStatusCode.BadRequest);

        Assert.Contains("IncidentDate", problem.Errors!.Keys);
        Assert.Contains("Category", problem.Errors.Keys);
        Assert.Contains("ClaimedAmount", problem.Errors.Keys);
    }

    [Fact]
    public async Task Create_IncidentInTheFuture_IsRejected()
    {
        var tomorrow = DateOnly.FromDateTime(ClaimFlowApiFactory.TestNow.UtcDateTime).AddDays(1);
        var problem = await ReadProblemAsync(
            await PostJsonAsync("/api/claims", ValidCreateRequest() with { IncidentDate = tomorrow }), HttpStatusCode.BadRequest);
        Assert.Equal(["Incident date cannot be in the future."], problem.Errors!["IncidentDate"]);
    }

    [Fact]
    public async Task Create_UnknownCategory_Returns400WithoutLeakingTypeNames()
    {
        using var content = new StringContent(
            """{"policyNumber":"DEMO-POL-1001","category":"Boat","incidentDate":"2026-11-10","claimedAmount":10,"description":"Long enough text"}""",
            System.Text.Encoding.UTF8,
            "application/json");
        var response = await Client.PostAsync("/api/claims", content, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("System.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ClaimFlow.Api", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_WrongContentType_Returns415()
    {
        using var content = new StringContent("policyNumber=DEMO-POL-1001", System.Text.Encoding.UTF8, "text/plain");
        var response = await Client.PostAsync("/api/claims", content, Ct);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    // ---------- Workflow ----------

    [Fact]
    public async Task Transition_FullWorkflow_RecordsEveryStepInTheAuditTrail()
    {
        var created = await ReadAsync<ClaimDetailDto>(await PostJsonAsync("/api/claims", ValidCreateRequest()), HttpStatusCode.Created);
        var url = $"/api/claims/{created.Reference}/transitions";

        Factory.Clock.Advance(TimeSpan.FromHours(1));
        var reviewing = await ReadAsync<ClaimDetailDto>(
            await PostJsonAsync(url, new TransitionClaimRequest { TargetStatus = ClaimStatus.UnderReview }), HttpStatusCode.OK);
        Assert.Equal(ClaimStatus.UnderReview, reviewing.Status);

        Factory.Clock.Advance(TimeSpan.FromHours(1));
        var approved = await ReadAsync<ClaimDetailDto>(
            await PostJsonAsync(url, new TransitionClaimRequest { TargetStatus = ClaimStatus.Approved, Reason = "Quote verified" }), HttpStatusCode.OK);

        Assert.Equal(ClaimStatus.Approved, approved.Status);
        Assert.Empty(approved.AllowedNextStatuses);
        Assert.Equal(
            [(null, ClaimStatus.Submitted), (ClaimStatus.Submitted, ClaimStatus.UnderReview), (ClaimStatus.UnderReview, ClaimStatus.Approved)],
            approved.History.Select(h => (h.FromStatus, h.ToStatus)));
        Assert.Equal("Quote verified", approved.History[^1].Reason);
        Assert.True(approved.History[^1].ChangedAtUtc > approved.History[0].ChangedAtUtc);
    }

    [Fact]
    public async Task Transition_SkippingReview_Returns409_AndLeavesTheClaimUnchanged()
    {
        var problem = await ReadProblemAsync(
            await PostJsonAsync("/api/claims/CLM-2026-D00018/transitions", new TransitionClaimRequest { TargetStatus = ClaimStatus.Approved }),
            HttpStatusCode.Conflict);
        Assert.Equal("Status change not allowed", problem.Title);

        var claim = await ReadAsync<ClaimDetailDto>(await Client.GetAsync("/api/claims/CLM-2026-D00018", Ct), HttpStatusCode.OK);
        Assert.Equal(ClaimStatus.Submitted, claim.Status);
        Assert.Single(claim.History);
    }

    [Fact]
    public async Task Transition_FromFinalStatus_Returns409()
    {
        // D00001 is Approved in the seed data.
        await ReadProblemAsync(
            await PostJsonAsync("/api/claims/CLM-2026-D00001/transitions", new TransitionClaimRequest { TargetStatus = ClaimStatus.Rejected, Reason = "Second thoughts" }),
            HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Transition_RejectWithoutReason_Returns400()
    {
        var problem = await ReadProblemAsync(
            await PostJsonAsync("/api/claims/CLM-2026-D00007/transitions", new TransitionClaimRequest { TargetStatus = ClaimStatus.Rejected }),
            HttpStatusCode.BadRequest);
        Assert.True(problem.Errors!.ContainsKey("reason"));
    }

    [Fact]
    public async Task Transition_MissingTargetStatus_Returns400()
    {
        var problem = await ReadProblemAsync(await PostJsonAsync("/api/claims/CLM-2026-D00007/transitions", new { }), HttpStatusCode.BadRequest);
        Assert.True(problem.Errors!.ContainsKey("TargetStatus"));
    }

    [Fact]
    public async Task Transition_UnknownClaim_Returns404()
    {
        await ReadProblemAsync(
            await PostJsonAsync("/api/claims/CLM-2026-NOPE00/transitions", new TransitionClaimRequest { TargetStatus = ClaimStatus.UnderReview }),
            HttpStatusCode.NotFound);
    }

    // ---------- Policies and dashboard ----------

    [Fact]
    public async Task Policies_ListsTheFictionalPolicies_WithActiveFlag()
    {
        var policies = await Client.GetFromJsonAsync<List<PolicyDto>>("/api/policies", Json, Ct);

        Assert.Equal(8, policies!.Count);
        Assert.All(policies, p => Assert.StartsWith("DEMO-POL-", p.PolicyNumber));
        Assert.False(policies.Single(p => p.PolicyNumber == "DEMO-POL-1008").IsActive);
        Assert.True(policies.Single(p => p.PolicyNumber == "DEMO-POL-1001").IsActive);
    }

    [Fact]
    public async Task Dashboard_SummarisesTheData_AndUpdatesAfterChanges()
    {
        var before = await ReadAsync<DashboardSummaryDto>(await Client.GetAsync("/api/dashboard/summary", Ct), HttpStatusCode.OK);

        Assert.Equal(18, before.TotalClaims);
        Assert.Equal(5, before.CountByStatus[ClaimStatus.Submitted]);
        Assert.Equal(6, before.CountByStatus[ClaimStatus.Approved]);
        Assert.Equal(3, before.CountByStatus[ClaimStatus.Rejected]);
        Assert.Equal(9, before.OpenClaims);
        Assert.Equal(0.6667, before.ApprovalRate);
        Assert.Equal(248_800m, before.TotalApprovedAmount);
        Assert.Equal(0, before.Demo.VisitorClaims);
        Assert.Equal(5, before.RecentClaims.Count);

        await ReadAsync<ClaimDetailDto>(await PostJsonAsync("/api/claims", ValidCreateRequest()), HttpStatusCode.Created);
        var after = await ReadAsync<DashboardSummaryDto>(await Client.GetAsync("/api/dashboard/summary", Ct), HttpStatusCode.OK);

        Assert.Equal(19, after.TotalClaims);
        Assert.Equal(6, after.CountByStatus[ClaimStatus.Submitted]);
        Assert.Equal(before.TotalClaimedAmount + 12_500.50m, after.TotalClaimedAmount);
        Assert.Equal(1, after.Demo.VisitorClaims);
    }
}
