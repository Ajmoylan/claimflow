using ClaimFlow.Api.Data;
using ClaimFlow.Api.Domain;
using ClaimFlow.Api.Services;
using ClaimFlow.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimFlow.IntegrationTests;

/// <summary>
/// Checks the database itself: migrations, constraints, cascades, concurrency and the demo reset.
/// These run against SQLite locally and against a real SQL Server in CI, which is where provider differences would show up.
/// </summary>
public class DatabaseTests(ClaimFlowApiFactory factory) : IntegrationTestBase<ClaimFlowApiFactory>(factory)
{
    private static readonly DateTime Now = ClaimFlowApiFactory.TestNow.UtcDateTime;

    [Fact]
    public async Task Migrations_AreAllApplied_ForTheConfiguredProvider()
    {
        var (pending, providerName) = await Factory.WithDbAsync(async db =>
            ((await db.Database.GetPendingMigrationsAsync(Ct)).ToList(), db.Database.ProviderName));

        Assert.Empty(pending);
        Assert.Contains(Factory.Provider == DatabaseProvider.SqlServer ? "SqlServer" : "Sqlite", providerName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClaimReference_MustBeUnique()
    {
        await Factory.WithDbAsync(async db =>
        {
            var policy = await db.Policies.FirstAsync(p => p.PolicyNumber == "DEMO-POL-1001", Ct);
            db.Claims.Add(Claim.Submit("CLM-2026-D00001", policy, ClaimCategory.Motor, new(2026, 11, 1), 100m, "Duplicate reference test", Now));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        });
    }

    [Fact]
    public async Task Claim_MustReferenceAnExistingPolicy()
    {
        await Factory.WithDbAsync(async db =>
        {
            var orphan = Claim.Submit(
                "CLM-2026-ORPHAN",
                new Policy { Id = 99_999, PolicyNumber = "X", HolderName = "X" },
                ClaimCategory.Motor,
                new(2026, 11, 1),
                100m,
                "Claim pointing at a policy that does not exist",
                Now);
            orphan.Policy = null; // keep only the foreign key value
            db.Claims.Add(orphan);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        });
    }

    [Fact]
    public async Task Policy_WithClaims_CannotBeDeleted()
    {
        await Factory.WithDbAsync(async db =>
        {
            var deleting = db.Policies.Where(p => p.PolicyNumber == "DEMO-POL-1001").ExecuteDeleteAsync(Ct);
            await Assert.ThrowsAnyAsync<Exception>(() => deleting);
        });
    }

    [Fact]
    public async Task ClaimedAmount_MustBePositive_EvenIfTheApiIsBypassed()
    {
        await Factory.WithDbAsync(async db =>
        {
            var policy = await db.Policies.FirstAsync(p => p.PolicyNumber == "DEMO-POL-1001", Ct);
            db.Claims.Add(Claim.Submit("CLM-2026-NEGATV", policy, ClaimCategory.Motor, new(2026, 11, 1), -1m, "Negative amount test", Now));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        });
    }

    [Fact]
    public async Task DeletingAClaim_RemovesItsHistory()
    {
        await Factory.WithDbAsync(async db =>
        {
            var claim = await db.Claims.SingleAsync(c => c.Reference == "CLM-2026-D00001", Ct);
            db.Claims.Remove(claim);
            await db.SaveChangesAsync(Ct);

            Assert.False(await db.ClaimStatusChanges.AnyAsync(h => h.ClaimId == claim.Id, Ct));
        });
    }

    [Fact]
    public async Task ConcurrentUpdates_TheSecondWriterGetsAConcurrencyError()
    {
        await using var scopeA = Factory.Services.CreateAsyncScope();
        await using var scopeB = Factory.Services.CreateAsyncScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<ClaimFlowDbContext>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<ClaimFlowDbContext>();

        var claimA = await dbA.Claims.SingleAsync(c => c.Reference == "CLM-2026-D00018", Ct);
        var claimB = await dbB.Claims.SingleAsync(c => c.Reference == "CLM-2026-D00018", Ct);

        claimA.TransitionTo(ClaimStatus.UnderReview, "Handler A", Now);
        await dbA.SaveChangesAsync(Ct);

        claimB.TransitionTo(ClaimStatus.UnderReview, "Handler B", Now);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbB.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Reset_RestoresTheSeedData_AndRemovesVisitorClaims()
    {
        await ReadAsync<Api.Contracts.ClaimDetailDto>(await PostJsonAsync("/api/claims", ValidCreateRequest()), System.Net.HttpStatusCode.Created);
        await PostJsonAsync("/api/claims/CLM-2026-D00018/transitions", new Api.Contracts.TransitionClaimRequest { TargetStatus = ClaimStatus.UnderReview });

        await Factory.ResetDatabaseAsync();

        await Factory.WithDbAsync(async db =>
        {
            Assert.Equal(18, await db.Claims.CountAsync(Ct));
            Assert.False(await db.Claims.AnyAsync(c => !c.IsDemoSeed, Ct));
            Assert.Equal(ClaimStatus.Submitted, (await db.Claims.SingleAsync(c => c.Reference == "CLM-2026-D00018", Ct)).Status);
            Assert.Equal(8, await db.Policies.CountAsync(Ct));
        });
    }

    [Fact]
    public async Task ResetIfDue_OnlyResetsAfterTheScheduledTimeHasPassed()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var demo = scope.ServiceProvider.GetRequiredService<DemoDataService>();

        // The fixture's reset just happened, so nothing is due yet.
        Assert.False(await demo.ResetIfDueAsync(Ct));

        Factory.Clock.Advance(TimeSpan.FromDays(1));
        Assert.True(await demo.ResetIfDueAsync(Ct));
        Assert.False(await demo.ResetIfDueAsync(Ct));
    }
}
