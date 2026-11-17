using System.Net;
using ClaimFlow.Api.Contracts;
using ClaimFlow.Api.Domain;
using ClaimFlow.Api.Services;
using ClaimFlow.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClaimFlow.IntegrationTests;

/// <summary>
/// Simulates App Service unloading the app overnight: automatic reset is on, but the background service
/// is removed completely, so the only thing that can perform the reset is the request-time gate.
/// </summary>
public sealed class NoBackgroundServiceFactory : ClaimFlowApiFactory
{
    protected override IDictionary<string, string?> ExtraSettings => new Dictionary<string, string?>
    {
        ["Demo:AutoResetEnabled"] = "true",
    };

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        var background = services.Single(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(DemoResetBackgroundService));
        services.Remove(background);
    }
}

public class NightlyResetTests(NoBackgroundServiceFactory factory) : IntegrationTestBase<NoBackgroundServiceFactory>(factory)
{
    [Fact]
    public async Task TheFirstRequestAfterTheScheduledTime_ResetsTheData_WithoutAnyBackgroundService()
    {
        Assert.DoesNotContain(Factory.Services.GetServices<IHostedService>(), s => s is DemoResetBackgroundService);

        var visitorClaim = await ReadAsync<ClaimDetailDto>(await PostJsonAsync("/api/claims", ValidCreateRequest()), HttpStatusCode.Created);
        await ReadAsync<ClaimDetailDto>(
            await PostJsonAsync("/api/claims/CLM-2026-D00018/transitions", new TransitionClaimRequest { TargetStatus = ClaimStatus.UnderReview }),
            HttpStatusCode.OK);

        // "Sleep" past 03:00 Oslo time. Nothing runs in the meantime.
        Factory.Clock.Advance(TimeSpan.FromDays(1));

        // The very first request after waking up already sees the restored data.
        var claims = await ReadAsync<PagedResult<ClaimSummaryDto>>(await Client.GetAsync("/api/claims?pageSize=100", Ct), HttpStatusCode.OK);
        Assert.Equal(18, claims.TotalCount);
        Assert.DoesNotContain(claims.Items, c => c.Reference == visitorClaim.Reference);
        Assert.Equal(ClaimStatus.Submitted, claims.Items.Single(c => c.Reference == "CLM-2026-D00018").Status);
    }

    [Fact]
    public async Task BeforeTheScheduledTime_VisitorChangesAreKept()
    {
        var visitorClaim = await ReadAsync<ClaimDetailDto>(await PostJsonAsync("/api/claims", ValidCreateRequest()), HttpStatusCode.Created);

        Factory.Clock.Advance(TimeSpan.FromHours(1)); // still the same demo day

        var fetched = await Client.GetAsync($"/api/claims/{visitorClaim.Reference}", Ct);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
    }
}
