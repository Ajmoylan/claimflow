using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClaimFlow.Api.Data;

/// <summary>SQLite context: local development and fast local tests. Migrations live in Data/Migrations/Sqlite.</summary>
public sealed class SqliteClaimFlowDbContext(DbContextOptions<SqliteClaimFlowDbContext> options)
    : ClaimFlowDbContext(options);

/// <summary>SQL Server context: Azure SQL in production and SQL Server in CI. Migrations live in Data/Migrations/SqlServer.</summary>
public sealed class SqlServerClaimFlowDbContext(DbContextOptions<SqlServerClaimFlowDbContext> options)
    : ClaimFlowDbContext(options);

/// <summary>
/// Used only by <c>dotnet ef</c> at design time, so migrations can be generated without starting the app.
/// The connection string is never opened by <c>migrations add</c>.
/// </summary>
internal sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteClaimFlowDbContext>
{
    public SqliteClaimFlowDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqliteClaimFlowDbContext>()
            .UseSqlite("Data Source=claimflow.design.db")
            .Options);
}

/// <inheritdoc cref="SqliteDesignTimeFactory"/>
internal sealed class SqlServerDesignTimeFactory : IDesignTimeDbContextFactory<SqlServerClaimFlowDbContext>
{
    public SqlServerClaimFlowDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqlServerClaimFlowDbContext>()
            .UseSqlServer("Server=localhost;Database=claimflow_design;Integrated Security=false;TrustServerCertificate=true")
            .Options);
}
