using ClaimFlow.Api.Data;
using ClaimFlow.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ClaimFlow.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real API in memory with its real middleware, controllers, services and EF Core migrations.
/// Each factory gets its own empty database, created by the app's own startup migration:
/// <list type="bullet">
/// <item>SQLite (default): an in-memory database, fast, no dependencies.</item>
/// <item>SQL Server (<c>CLAIMFLOW_TEST_PROVIDER=SqlServer</c>, used in CI): a fresh database on a real SQL Server container.</item>
/// </list>
/// </summary>
public class ClaimFlowApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Tests run on a fixed "today" so date rules (no future incidents, policy periods) are deterministic.</summary>
    public static readonly DateTimeOffset TestNow = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly string _databaseName = $"claimflow_test_{Guid.NewGuid():N}";
    private SqliteConnection? _keepAlive;
    private string _connectionString = string.Empty;

    public FakeTimeProvider Clock { get; } = new(TestNow);

    public DatabaseProvider Provider { get; } = TestDatabase.Provider;

    /// <summary>Extra configuration a derived factory wants (e.g. lower limits).</summary>
    protected virtual IDictionary<string, string?> ExtraSettings => new Dictionary<string, string?>();

    /// <summary>Extra service changes a derived factory wants (e.g. removing a hosted service).</summary>
    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
    }

    public async ValueTask InitializeAsync()
    {
        if (Provider == DatabaseProvider.SqlServer)
        {
            _connectionString = SqlServerContainer.CreateDatabaseConnectionString(_databaseName);
        }
        else
        {
            // A named in-memory SQLite database lives as long as one connection to it stays open.
            _connectionString = $"Data Source={_databaseName};Mode=Memory;Cache=Shared";
            _keepAlive = new SqliteConnection(_connectionString);
            await _keepAlive.OpenAsync();
        }

        // Starting the server runs Program.cs, which applies migrations and seeds the demo data.
        StartServer();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Provider", Provider.ToString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("ConnectionStrings:ClaimFlow", _connectionString);
        builder.UseSetting("Demo:AutoResetEnabled", "false");
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
        builder.UseSetting("RateLimiting:RequestsPerMinute", "100000");
        builder.UseSetting("RateLimiting:WritesPerMinute", "100000");
        builder.UseSetting("RateLimiting:GlobalWritesPerMinute", "100000");
        foreach (var (key, value) in ExtraSettings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            ConfigureTestServices(services);
        });
    }

    /// <summary>Restores the seed data so each test starts from the same known state.</summary>
    public async Task ResetDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DemoDataService>().ResetAsync(CancellationToken.None);
    }

    /// <summary>Runs <paramref name="action"/> with a fresh DbContext from the app's own container.</summary>
    public async Task<T> WithDbAsync<T>(Func<ClaimFlowDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ClaimFlowDbContext>());
    }

    public async Task WithDbAsync(Func<ClaimFlowDbContext, Task> action) =>
        await WithDbAsync(async db =>
        {
            await action(db);
            return true;
        });

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_keepAlive is not null)
        {
            await _keepAlive.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}
