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

        builder.HasMany(j => j.Tasks)
            .WithOne(t => t.Job)
            .HasForeignKey(t => t.JobId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
