using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RelayForge.Domain.Entities;

namespace RelayForge.Infrastructure.Configurations;

public class JobTaskConfiguration : IEntityTypeConfiguration<JobTask>
{
    public void Configure(EntityTypeBuilder<JobTask> builder)
    {
        builder.ToTable("JobTasks");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.State).HasConversion<string>().HasMaxLength(30);
        builder.Property(t => t.ErrorMessage).HasMaxLength(2000);

        // Reliability (Phase 2). IdempotencyKey is set in memory at entity creation
        // (JobTask.IdempotencyKey = Guid.NewGuid() default), not DB-generated, so it's
        // stable across the task's whole retry history and visible immediately on submit.
        builder.Property(t => t.AttemptCount).HasDefaultValue(0);
        builder.Property(t => t.MaxAttempts).HasDefaultValue(4);
        builder.Property(t => t.IdempotencyKey).IsRequired();

        builder.HasIndex(t => t.JobId);
        builder.HasIndex(t => new { t.State, t.NextAttemptAt });
        builder.HasIndex(t => new { t.State, t.LeaseExpiresAt });
    }
}
