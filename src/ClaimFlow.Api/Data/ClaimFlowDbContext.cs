using ClaimFlow.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ClaimFlow.Api.Data;

/// <summary>
/// The EF Core model, shared by both database providers.
/// <para>
/// Application code depends only on this base class. Each provider has a small derived context
/// (<see cref="SqliteClaimFlowDbContext"/>, <see cref="SqlServerClaimFlowDbContext"/>) so that each
/// gets its own migrations folder. This is EF Core's documented "multiple context types" approach
/// to migrations with more than one provider.
/// </para>
/// </summary>
public abstract class ClaimFlowDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Policy> Policies => Set<Policy>();

    public DbSet<Claim> Claims => Set<Claim>();

    public DbSet<ClaimStatusChange> ClaimStatusChanges => Set<ClaimStatusChange>();

    public DbSet<DemoState> DemoState => Set<DemoState>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // All timestamps are stored in UTC. Neither SQLite (TEXT) nor SQL Server (datetime2) keeps DateTime.Kind,
        // so mark values as UTC on the way out; JSON then carries the "Z" suffix and browsers show local time correctly.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Policy>(policy =>
        {
            policy.ToTable("Policies");
            policy.HasKey(p => p.Id);
            policy.Property(p => p.PolicyNumber).HasMaxLength(Policy.PolicyNumberMaxLength).IsRequired();
            policy.HasIndex(p => p.PolicyNumber).IsUnique();
            policy.Property(p => p.HolderName).HasMaxLength(Policy.HolderNameMaxLength).IsRequired();
            policy.Property(p => p.CoverageCategory).HasConversion<string>().HasMaxLength(20);
            policy.Property(p => p.CoverageLimit).HasPrecision(18, 2);
            policy.ToTable(t => t.HasCheckConstraint("CK_Policies_ValidPeriod", "\"ValidTo\" >= \"ValidFrom\""));
        });

        modelBuilder.Entity<Claim>(claim =>
        {
            claim.ToTable("Claims", t =>
            {
                t.HasCheckConstraint("CK_Claims_ClaimedAmount_Positive", "\"ClaimedAmount\" > 0");
            });
            claim.HasKey(c => c.Id);
            claim.Property(c => c.Reference).HasMaxLength(Claim.ReferenceMaxLength).IsRequired();
            claim.HasIndex(c => c.Reference).IsUnique();
            claim.Property(c => c.Category).HasConversion<string>().HasMaxLength(20);
            claim.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
            claim.Property(c => c.ClaimedAmount).HasPrecision(18, 2);
            claim.Property(c => c.Description).HasMaxLength(Claim.DescriptionMaxLength).IsRequired();
            claim.Property(c => c.Version).IsConcurrencyToken();

            // A policy with claims can never be deleted by accident.
            claim.HasOne(c => c.Policy)
                .WithMany(p => p.Claims)
                .HasForeignKey(c => c.PolicyId)
                .OnDelete(DeleteBehavior.Restrict);

            claim.HasMany(c => c.StatusHistory)
                .WithOne(h => h.Claim)
                .HasForeignKey(h => h.ClaimId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes behind the dashboard and the list filters.
            claim.HasIndex(c => c.Status);
            claim.HasIndex(c => c.CreatedAtUtc);
            claim.HasIndex(c => c.IsDemoSeed);
        });

        modelBuilder.Entity<ClaimStatusChange>(change =>
        {
            change.ToTable("ClaimStatusChanges");
            change.HasKey(h => h.Id);
            change.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(20);
            change.Property(h => h.ToStatus).HasConversion<string>().HasMaxLength(20);
            change.Property(h => h.Reason).HasMaxLength(ClaimStatusChange.ReasonMaxLength);
            change.HasIndex(h => new { h.ClaimId, h.ChangedAtUtc });
        });

        modelBuilder.Entity<DemoState>(state =>
        {
            state.ToTable("DemoState");
            state.HasKey(s => s.Id);
            state.Property(s => s.Id).ValueGeneratedNever();
        });
    }
}

internal sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    value => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value,
    value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

/// <summary>Single-row table that remembers when the demo data was last reset.</summary>
public class DemoState
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public DateTime LastResetAtUtc { get; set; }
}
