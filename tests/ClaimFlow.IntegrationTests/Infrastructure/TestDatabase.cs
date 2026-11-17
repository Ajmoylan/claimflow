using ClaimFlow.Api.Data;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(ClaimFlow.IntegrationTests.Infrastructure.SqlServerContainer))]

namespace ClaimFlow.IntegrationTests.Infrastructure;

/// <summary>Chooses the database provider for the integration tests from the CLAIMFLOW_TEST_PROVIDER environment variable.</summary>
public static class TestDatabase
{
    public static DatabaseProvider Provider { get; } =
        Enum.TryParse<DatabaseProvider>(Environment.GetEnvironmentVariable("CLAIMFLOW_TEST_PROVIDER"), ignoreCase: true, out var provider)
            ? provider
            : DatabaseProvider.Sqlite;
}

/// <summary>
/// One SQL Server container for the whole test run (started only when the provider is SqlServer).
/// Each test factory creates its own database on it, so test classes can run in parallel without sharing data.
/// The image is pinned so CI results are reproducible.
/// </summary>
public sealed class SqlServerContainer : IAsyncLifetime
{
    public const string Image = "mcr.microsoft.com/mssql/server:2022-CU28-ubuntu-22.04";

    private static MsSqlContainer? _container;

    public async ValueTask InitializeAsync()
    {
        if (TestDatabase.Provider != DatabaseProvider.SqlServer)
        {
            return;
        }

        _container = new MsSqlBuilder(Image).Build();
        await _container.StartAsync();
    }

    public static string CreateDatabaseConnectionString(string databaseName)
    {
        if (_container is null)
        {
            throw new InvalidOperationException("SQL Server container is not running. Is Docker available?");
        }

        // The app's startup migration creates the database itself; we only need to point the connection string at it.
        return new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = databaseName,
            TrustServerCertificate = true,
        }.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
