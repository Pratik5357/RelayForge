using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RelayForge.Domain.Entities;

namespace RelayForge.Infrastructure.Configurations;

public class TaskAttemptConfiguration : IEntityTypeConfiguration<TaskAttempt>
{
    public void Configure(EntityTypeBuilder<TaskAttempt> builder)
    {
        builder.ToTable("TaskAttempts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).UseIdentityAlwaysColumn();
        builder.Property(a => a.Outcome).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Error).HasMaxLength(2000);
        builder.Property(a => a.RejectedLateResult).HasMaxLength(2000);

        // AttemptCount only goes up, so (task, attempt) is unique.
        builder.HasIndex(a => new { a.TaskId, a.Attempt }).IsUnique();
        builder.HasIndex(a => a.JobId);
    }
}
