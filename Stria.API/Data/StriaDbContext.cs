using Microsoft.EntityFrameworkCore;
using Stria.Domain;

namespace Stria.Data;

public class StriaDbContext : DbContext
{
    /// <summary>Partial unique index guaranteeing at most one Active experiment per target.</summary>
    public const string ActiveTargetIndexName = "ux_chaos_experiments_active_target";

    public StriaDbContext(DbContextOptions<StriaDbContext> options) : base(options)
    {
    }

    public DbSet<ChaosExperiment> ChaosExperiments => Set<ChaosExperiment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChaosExperiment>(e =>
        {
            e.ToTable("ChaosExperiments", t =>
            {
                // A CHECK passes when it evaluates to NULL (SQL's three-valued logic), so every
                // "required when X" branch asserts IS NOT NULL explicitly.
                t.HasCheckConstraint(
                    "ck_chaos_experiments_latency",
                    "(\"FaultType\" = 'Latency' AND \"LatencyMs\" IS NOT NULL AND \"LatencyMs\" BETWEEN 1 AND 60000) " +
                    "OR (\"FaultType\" <> 'Latency' AND \"LatencyMs\" IS NULL)");

                t.HasCheckConstraint(
                    "ck_chaos_experiments_status_code",
                    "(\"FaultType\" = 'HttpFailure' AND \"StatusCode\" IS NOT NULL AND \"StatusCode\" BETWEEN 500 AND 599) " +
                    "OR (\"FaultType\" <> 'HttpFailure' AND \"StatusCode\" IS NULL)");

                t.HasCheckConstraint("ck_chaos_experiments_percent", "\"Percent\" BETWEEN 1 AND 100");

                // A lease must end after it begins.
                t.HasCheckConstraint("ck_chaos_experiments_lease", "\"ExpiresAt\" > \"CreatedAt\"");

                // Active rows have no end; Cleared rows always record when and by whom.
                t.HasCheckConstraint(
                    "ck_chaos_experiments_cleared",
                    "(\"Status\" = 'Active' AND \"ClearedAt\" IS NULL AND \"ClearedBy\" IS NULL) " +
                    "OR (\"Status\" = 'Cleared' AND \"ClearedAt\" IS NOT NULL AND \"ClearedBy\" IS NOT NULL)");
            });

            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();

            e.Property(x => x.TargetService).IsRequired().HasMaxLength(64);
            e.Property(x => x.FaultType).IsRequired().HasMaxLength(32);
            e.Property(x => x.Percent).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            e.Property(x => x.TriggeredBy).IsRequired().HasMaxLength(ChaosRules.MaxTriggeredByLength);
            e.Property(x => x.ClearedBy).HasMaxLength(ChaosRules.MaxTriggeredByLength);
            e.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
            e.Property(x => x.ExpiresAt).HasColumnType("timestamp with time zone").IsRequired();
            e.Property(x => x.ClearedAt).HasColumnType("timestamp with time zone");

            e.HasIndex(x => x.TargetService)
                .IsUnique()
                .HasFilter("\"Status\" = 'Active'")
                .HasDatabaseName(ActiveTargetIndexName);

            // Supports the retention sweep (Status = Cleared AND ClearedAt < cutoff).
            e.HasIndex(x => x.ClearedAt);
        });
    }
}