using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RelayForge.Domain.Entities;

namespace RelayForge.Infrastructure.Configurations;

public class JobDefinitionConfiguration : IEntityTypeConfiguration<JobDefinition>
{
    public void Configure(EntityTypeBuilder<JobDefinition> builder)
    {
        builder.ToTable("JobDefinitions");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Name).HasMaxLength(200).IsRequired();
        builder.Property(d => d.Cron).HasMaxLength(100).IsRequired();
        builder.Property(d => d.TimeZone).HasMaxLength(100).IsRequired();
        builder.Property(d => d.Template).HasColumnType("jsonb").IsRequired();
        builder.Property(d => d.MisfirePolicy).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.ConcurrencyPolicy).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(d => d.Name).IsUnique();
        builder.HasIndex(d => d.NextFireAt).HasFilter("\"Enabled\"");
    }
}
