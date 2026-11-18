using ClaimFlow.Api.Data;
using ClaimFlow.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimFlow.IntegrationTests;

/// <summary>
/// An Azure SQL serverless database only auto-pauses when it has no sessions. These tests prove that the app
/// does not hold pooled connections open once it has been idle (SQL Server only; CI runs them on a real container).
/// </summary>
public class IdleConnectionTests(ClaimFlowApiFactory factory) : IntegrationTestBase<ClaimFlowApiFactory>(factory)
{
    [Fact]
    public async Task AfterTwoIdleMinutes_TheAppHoldsNoSessionsOpen()
    {
        Assert.SkipUnless(Factory.Provider == DatabaseProvider.SqlServer, "Connection pooling behaviour is specific to SQL Server.");
        var releaser = Factory.Services.GetRequiredService<IdleSqlConnectionReleaser>();

        Assert.Equal(System.Net.HttpStatusCode.OK, (await Client.GetAsync("/api/dashboard/summary", Ct)).StatusCode);
        Assert.True(await CountOtherSessionsAsync() >= 1, "The pooled connection should still be open right after a request.");

        Assert.False(releaser.ReleaseIfIdle()); // just used: keep the pool warm

        // Let three idle minutes pass. The releaser's own background timer (on the fake clock) does the rest;
        // nothing in the test calls it, so this proves the hosted service releases the pool by itself.
        Factory.Clock.Advance(TimeSpan.FromMinutes(3));

        Assert.Equal(0, await WaitForSessionsAsync(expected: 0));

        // And the app still works afterwards: the next request opens a fresh connection.
        Assert.Equal(System.Net.HttpStatusCode.OK, (await Client.GetAsync("/api/policies", Ct)).StatusCode);
    }

    [Fact]
    public void OnSqlite_NoReleaserIsRegistered()
    {
        Assert.SkipUnless(Factory.Provider == DatabaseProvider.Sqlite, "Only relevant for the SQLite provider.");
        Assert.Null(Factory.Services.GetService<IdleSqlConnectionReleaser>());
    }

    private async Task<int> WaitForSessionsAsync(int expected)
    {
        var count = -1;
        for (var attempt = 0; attempt < 20 && count != expected; attempt++)
        {
            count = await CountOtherSessionsAsync();
            if (count != expected)
            {
                await Task.Delay(250, Ct);
            }
        }

        return count;
    }

    /// <summary>User sessions connected to this test's database, excluding the session used to count them.</summary>
    private async Task<int> CountOtherSessionsAsync()
    {
        var builder = new SqlConnectionStringBuilder(Factory.ConnectionString) { Pooling = false };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE database_id = DB_ID() AND session_id <> @@SPID AND is_user_process = 1";
        return (int)(await command.ExecuteScalarAsync(Ct))!;
    }
}
