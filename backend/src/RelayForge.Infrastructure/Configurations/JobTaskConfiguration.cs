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

        builder.Property(t => t.Key).HasMaxLength(100).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.State).HasConversion<string>().HasMaxLength(30);
        builder.Property(t => t.Type).HasMaxLength(100).IsRequired();
        builder.Property(t => t.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(t => t.Output).HasColumnType("jsonb");
        builder.Property(t => t.Isolation).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.ErrorMessage).HasMaxLength(2000);

        // IdempotencyKey is set in memory at entity creation (JobTask.IdempotencyKey =
        // Guid.NewGuid() default), not DB-generated, so it's stable across the task's whole
        // retry history and visible immediately on submit.
        builder.Property(t => t.AttemptCount).HasDefaultValue(0);
        builder.Property(t => t.MaxAttempts).HasDefaultValue(4);
        builder.Property(t => t.IdempotencyKey).IsRequired();

        // Fencing: every tracked save of a task adds WHERE "LeaseOwnerId" = @original AND
        // "AttemptCount" = @original, so a write based on a lease that has since changed hands
        // fails with DbUpdateConcurrencyException instead of overwriting the newer attempt.
        builder.Property(t => t.LeaseOwnerId).IsConcurrencyToken();
        builder.Property(t => t.AttemptCount).IsConcurrencyToken();

        builder.HasIndex(t => t.JobId);
        builder.HasIndex(t => new { t.JobId, t.Key }).IsUnique();
        builder.HasIndex(t => new { t.State, t.LeaseExpiresAt });

        // Serves the claim query's ORDER BY directly, and stays small: only Ready rows are in
        // it, however many finished tasks the table holds.
        builder.HasIndex(t => t.ClaimOrder)
            .HasFilter("\"State\" = 'Ready'")
            .HasDatabaseName("IX_JobTasks_Ready");

        builder.HasMany(t => t.Attempts)
            .WithOne()
            .HasForeignKey(a => a.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
