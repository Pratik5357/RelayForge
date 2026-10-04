using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RelayForge.Domain.Entities;

namespace RelayForge.Infrastructure.Configurations;

public class TaskDependencyConfiguration : IEntityTypeConfiguration<TaskDependency>
{
    public void Configure(EntityTypeBuilder<TaskDependency> builder)
    {
        builder.ToTable("TaskDependencies");
        builder.HasKey(d => d.Id);

        // Both FKs point at JobTask. SQL Server rejects the default cascade-delete
        // configuration here (multiple cascade paths from Job -> JobTask -> TaskDependency),
        // so both must be Restrict; only Job -> JobTask cascades.
        builder.HasOne(d => d.JobTask)
            .WithMany(t => t.Dependencies)
            .HasForeignKey(d => d.JobTaskId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.DependsOnJobTask)
            .WithMany(t => t.Dependents)
            .HasForeignKey(d => d.DependsOnJobTaskId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => new { d.JobTaskId, d.DependsOnJobTaskId }).IsUnique();
    }
}
