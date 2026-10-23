using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RelayForge.Domain.Entities;

namespace RelayForge.Infrastructure.Configurations;

public class SubmitKeyConfiguration : IEntityTypeConfiguration<SubmitKey>
{
    public void Configure(EntityTypeBuilder<SubmitKey> builder)
    {
        builder.ToTable("SubmitKeys");
        builder.HasKey(k => k.Key);
        builder.Property(k => k.Key).HasMaxLength(200);
        builder.Property(k => k.RequestHash).HasMaxLength(64).IsRequired();
        builder.HasOne<Job>().WithMany().HasForeignKey(k => k.JobId).OnDelete(DeleteBehavior.Cascade);
    }
}
