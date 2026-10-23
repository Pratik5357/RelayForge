using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RelayForge.Domain.Entities;

namespace RelayForge.Infrastructure.Configurations;

public class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("Jobs");
        builder.HasKey(j => j.Id);

        builder.Property(j => j.Name).HasMaxLength(200);
        builder.Property(j => j.ScenarioKey).HasMaxLength(100);
        builder.Property(j => j.State).HasConversion<string>().HasMaxLength(30);
        builder.Property(j => j.TraceParent).HasMaxLength(100);
        builder.Property(j => j.TenantId).HasMaxLength(100);

        // The scheduler's safety net: a definition creates at most one job per scheduled time,
        // even if two schedulers race.
        builder.HasIndex(j => new { j.DefinitionId, j.ScheduledFor })
            .IsUnique()
            .HasFilter("\"DefinitionId\" IS NOT NULL");
        builder.HasOne<JobDefinition>().WithMany().HasForeignKey(j => j.DefinitionId).OnDelete(DeleteBehavior.SetNull);

        // Retention scans finished jobs by age.
        builder.HasIndex(j => j.CompletedAt);

        builder.HasMany(j => j.Tasks)
            .WithOne(t => t.Job)
            .HasForeignKey(t => t.JobId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
