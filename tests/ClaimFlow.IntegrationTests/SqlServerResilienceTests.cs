using ClaimFlow.Api.Data;
using ClaimFlow.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.IntegrationTests;

/// <summary>How the app copes with Azure SQL serverless pausing and resuming (SQL Server only).</summary>
public class SqlServerResilienceTests(ClaimFlowApiFactory factory) : IntegrationTestBase<ClaimFlowApiFactory>(factory)
{
    [Fact]
    public async Task TransientFailuresAreRetried_AndEachRetryIsLoggedAsAWarning()
    {
        Assert.SkipUnless(Factory.Provider == DatabaseProvider.SqlServer, "The retrying execution strategy is SQL Server only.");

        await Factory.WithDbAsync(db =>
        {
            Assert.True(db.Database.CreateExecutionStrategy().RetriesOnFailure);

            // Production logs EF Core at Warning, so a retry logged at its default level (Information) would be invisible.
            var warnings = db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.WarningsConfiguration;
            Assert.Equal(LogLevel.Warning, warnings.GetLevel(CoreEventId.ExecutionStrategyRetrying));
            return Task.CompletedTask;
        });
    }
}
