using System.Net;
using ClaimFlow.Api.Contracts;
using ClaimFlow.IntegrationTests.Infrastructure;

namespace ClaimFlow.IntegrationTests;

/// <summary>A factory with a tiny visitor-claim cap, so the cap can be reached quickly.</summary>
public sealed class SmallDemoLimitFactory : ClaimFlowApiFactory
{
    protected override IDictionary<string, string?> ExtraSettings => new Dictionary<string, string?> { ["Demo:MaxVisitorClaims"] = "2" };
}

/// <summary>A factory with production-like rate limits but tiny numbers, so they can be hit in a test.</summary>
public sealed class StrictRateLimitFactory : ClaimFlowApiFactory
{
    protected override IDictionary<string, string?> ExtraSettings => new Dictionary<string, string?>
    {
        ["RateLimiting:RequestsPerMinute"] = "1000",
        ["RateLimiting:WritesPerMinute"] = "3",
        ["RateLimiting:GlobalWritesPerMinute"] = "1000",
    };
}

public class DemoLimitTests(SmallDemoLimitFactory factory) : IntegrationTestBase<SmallDemoLimitFactory>(factory)
{
    [Fact]
    public async Task VisitorClaimCap_RefusesFurtherClaims_UntilTheNextReset()
    {
        await ReadAsync<ClaimDetailDto>(await PostJsonAsync("/api/claims", ValidCreateRequest()), HttpStatusCode.Created);
        await ReadAsync<ClaimDetailDto>(await PostJsonAsync("/api/claims", ValidCreateRequest()), HttpStatusCode.Created);

        var problem = await ReadProblemAsync(await PostJsonAsync("/api/claims", ValidCreateRequest()), HttpStatusCode.TooManyRequests);
        Assert.Equal("Demo limit reached", problem.Title);

        await Factory.ResetDatabaseAsync();
        await ReadAsync<ClaimDetailDto>(await PostJsonAsync("/api/claims", ValidCreateRequest()), HttpStatusCode.Created);
    }
}

public class RateLimitTests(StrictRateLimitFactory factory) : IntegrationTestBase<StrictRateLimitFactory>(factory)
{
    [Fact]
    public async Task Writes_AreLimitedPerClient_With429ProblemAndRetryAfter()
    {
        for (var i = 0; i < 3; i++)
        {
            var ok = await PostJsonAsync("/api/claims", ValidCreateRequest());
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        }

        var limited = await PostJsonAsync("/api/claims", ValidCreateRequest());
        var problem = await ReadProblemAsync(limited, HttpStatusCode.TooManyRequests);

        Assert.Equal("Too many requests", problem.Title);
        Assert.NotNull(limited.Headers.RetryAfter);

        // Reads are not affected by the write limit.
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/claims", Ct)).StatusCode);
    }
}

/// <summary>
/// Runs the app on real Kestrel instead of the in-memory TestServer, because the request body size limit
/// is enforced by Kestrel itself.
/// </summary>
public sealed class KestrelApiFactory : ClaimFlowApiFactory
{
    public KestrelApiFactory() => UseKestrel(0);
}

public class KestrelLimitTests(KestrelApiFactory factory) : IntegrationTestBase<KestrelApiFactory>(factory)
{
    [Fact]
    public async Task OversizedBody_Returns413Problem()
    {
        using var content = new StringContent(
            $$"""{"description":"{{new string('x', 20_000)}}"}""", System.Text.Encoding.UTF8, "application/json");
        var problem = await ReadProblemAsync(await Client.PostAsync("/api/claims", content, Ct), HttpStatusCode.RequestEntityTooLarge);
        Assert.Equal("Payload Too Large", problem.Title);
    }
}

public class HttpPipelineTests(ClaimFlowApiFactory factory) : IntegrationTestBase<ClaimFlowApiFactory>(factory)
{
    [Fact]
    public async Task UnknownApiRoute_Returns404Problem_NotTheSpa()
    {
        var problem = await ReadProblemAsync(await Client.GetAsync("/api/does-not-exist", Ct), HttpStatusCode.NotFound);
        Assert.Equal("Endpoint not found", problem.Title);
    }

    [Fact]
    public async Task Responses_CarrySecurityHeaders()
    {
        var response = await Client.GetAsync("/api/policies", Ct);

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("default-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenApiDocument_DescribesTheClaimsEndpoints()
    {
        var document = await Client.GetStringAsync("/openapi/v1.json", Ct);

        Assert.Contains("\"/api/claims\"", document, StringComparison.Ordinal);
        Assert.Contains("\"/api/claims/{reference}/transitions\"", document, StringComparison.Ordinal);
        Assert.Contains("not an official If Forsikring product", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HealthEndpoint_ReportsHealthy()
    {
        var response = await Client.GetAsync("/health", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(Ct));
    }
}
