using System.Net;
using System.Net.Http.Json;
using ClaimFlow.Api.Contracts;
using ClaimFlow.Api.Data;
using ClaimFlow.Api.Domain;
using ClaimFlow.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace ClaimFlow.IntegrationTests;

/// <summary>
/// In Azure the app's managed identity may only read and write data (db_datareader + db_datawriter);
/// the SQL admin applies migrations. These tests prove the app needs nothing more, and that it refuses to
/// start against a schema that has not been migrated.
/// </summary>
public class DatabasePermissionTests(ClaimFlowApiFactory factory) : IntegrationTestBase<ClaimFlowApiFactory>(factory)
{
    [Fact]
    public async Task WithMigrationsOff_AnUnmigratedDatabase_StopsStartup()
    {
        await using var app = new ClaimFlowApiFactory { Settings = { ["Database:MigrateOnStartup"] = "false" } };

        var error = await Assert.ThrowsAnyAsync<Exception>(async () => await app.InitializeAsync());

        Assert.Contains(Flatten(error), e => e is InvalidOperationException && e.Message.Contains("schema is out of date", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnIdentityWithoutSchemaRights_RunsTheWholeDemo()
    {
        Assert.SkipUnless(Factory.Provider == DatabaseProvider.SqlServer, "Database permissions are a SQL Server feature.");

        // The class fixture has already migrated and seeded the database as an administrator,
        // which is what the SQL admin's migration script does in Azure.
        var login = $"app_{Guid.NewGuid():N}";
        var password = $"Aa1!{Guid.NewGuid():N}";
        await ExecuteAsync(AdminConnection("master"), $"CREATE LOGIN [{login}] WITH PASSWORD = '{password}', CHECK_POLICY = OFF;");
        await ExecuteAsync(AdminConnection(), $"""
            CREATE USER [{login}] FOR LOGIN [{login}];
            ALTER ROLE db_datareader ADD MEMBER [{login}];
            ALTER ROLE db_datawriter ADD MEMBER [{login}];
            """);
        var restricted = new SqlConnectionStringBuilder(Factory.ConnectionString) { UserID = login, Password = password }.ConnectionString;

        // The identity really cannot change the schema...
        var denied = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(restricted, "CREATE TABLE dbo.ShouldNotExist (Id int);"));
        Assert.Equal(262, denied.Number); // "CREATE TABLE permission denied"

        // ...and still runs everything the demo does: startup checks, reads, creates, the workflow, audit history
        // and the nightly reset (DELETE + INSERT only).
        await using var app = new ClaimFlowApiFactory
        {
            ExistingConnectionString = restricted,
            Settings = { ["Database:MigrateOnStartup"] = "false" },
        };
        await app.InitializeAsync();
        using var client = app.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/dashboard/summary", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Ct)).StatusCode);

        var created = await ReadAsync<ClaimDetailDto>(
            await client.PostAsJsonAsync("/api/claims", ValidCreateRequest(), Json, Ct), HttpStatusCode.Created);
        var transitions = $"/api/claims/{created.Reference}/transitions";
        await ReadAsync<ClaimDetailDto>(
            await client.PostAsJsonAsync(transitions, new TransitionClaimRequest { TargetStatus = ClaimStatus.UnderReview }, Json, Ct),
            HttpStatusCode.OK);
        var approved = await ReadAsync<ClaimDetailDto>(
            await client.PostAsJsonAsync(transitions, new TransitionClaimRequest { TargetStatus = ClaimStatus.Approved }, Json, Ct),
            HttpStatusCode.OK);
        Assert.Equal(ClaimStatus.Approved, approved.Status);

        var history = await ReadAsync<List<StatusChangeDto>>(
            await client.GetAsync($"/api/claims/{created.Reference}/history", Ct), HttpStatusCode.OK);
        Assert.Equal([ClaimStatus.Submitted, ClaimStatus.UnderReview, ClaimStatus.Approved], history.Select(h => h.ToStatus));

        await app.ResetDatabaseAsync();
        var afterReset = await ReadAsync<PagedResult<ClaimSummaryDto>>(await client.GetAsync("/api/claims", Ct), HttpStatusCode.OK);
        Assert.Equal(18, afterReset.TotalCount);
        Assert.DoesNotContain(afterReset.Items, c => c.Reference == created.Reference);
    }

    private string AdminConnection(string? database = null) =>
        database is null
            ? Factory.ConnectionString
            : new SqlConnectionStringBuilder(Factory.ConnectionString) { InitialCatalog = database }.ConnectionString;

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        var builder = new SqlConnectionStringBuilder(connectionString) { Pooling = false };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static IEnumerable<Exception> Flatten(Exception error)
    {
        for (var e = error; e is not null; e = e.InnerException)
        {
            yield return e;
            if (e is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions.SelectMany(Flatten))
                {
                    yield return inner;
                }
            }
        }
    }
}
