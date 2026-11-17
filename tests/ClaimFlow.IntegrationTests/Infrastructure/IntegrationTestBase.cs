using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaimFlow.Api.Contracts;

namespace ClaimFlow.IntegrationTests.Infrastructure;

/// <summary>
/// Shared setup: every test starts from freshly reset seed data, and talks to the API over HTTP
/// exactly like the React frontend does.
/// </summary>
public abstract class IntegrationTestBase<TFactory>(TFactory factory) : IClassFixture<TFactory>, IAsyncLifetime
    where TFactory : ClaimFlowApiFactory
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    protected TFactory Factory { get; } = factory;

    protected HttpClient Client { get; } = factory.CreateClient();

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await Factory.ResetDatabaseAsync();

    public ValueTask DisposeAsync()
    {
        Client.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    protected static CreateClaimRequest ValidCreateRequest() => new()
    {
        PolicyNumber = "DEMO-POL-1001",
        Category = Api.Domain.ClaimCategory.Motor,
        IncidentDate = new DateOnly(2026, 11, 10),
        ClaimedAmount = 12_500.50m,
        Description = "Scratched door in a car park; workshop quote attached.",
    };

    protected async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == expected, $"Expected {(int)expected} but got {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }

    protected async Task<ProblemResponse> ReadProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await ReadAsync<ProblemResponse>(response, expected);
        Assert.Equal((int)expected, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.TraceId));
        return problem;
    }

    protected Task<HttpResponseMessage> PostJsonAsync<T>(string url, T body) => Client.PostAsJsonAsync(url, body, Json, Ct);
}

/// <summary>The parts of an RFC 9457 problem response that the tests inspect.</summary>
public sealed record ProblemResponse(
    string? Title,
    int Status,
    string? Detail,
    string? TraceId,
    Dictionary<string, string[]>? Errors);
