using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RelayForge.Domain.Entities;

namespace RelayForge.Infrastructure.Configurations;

public class TaskLogLineConfiguration : IEntityTypeConfiguration<TaskLogLine>
{
    public void Configure(EntityTypeBuilder<TaskLogLine> builder)
    {
        builder.ToTable("TaskLogs");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).UseIdentityAlwaysColumn();
        builder.Property(l => l.Stream).HasMaxLength(10).IsRequired();
        builder.Property(l => l.Line).HasMaxLength(4000).IsRequired();

        builder.HasIndex(l => new { l.TaskId, l.Attempt, l.Sequence }).IsUnique();
        builder.HasOne<JobTask>().WithMany().HasForeignKey(l => l.TaskId).OnDelete(DeleteBehavior.Cascade);
    }
}
