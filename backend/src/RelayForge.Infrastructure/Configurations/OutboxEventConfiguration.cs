using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RelayForge.Domain.Entities;

namespace RelayForge.Infrastructure.Configurations;

public class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> builder)
    {
        builder.ToTable("OutboxEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).UseIdentityAlwaysColumn();
        builder.Property(e => e.Type).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Payload).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(e => new { e.JobId, e.Sequence }).IsUnique();

        // The dispatcher's query: only undelivered events are in it.
        builder.HasIndex(e => e.Id)
            .HasFilter("\"DispatchedAt\" IS NULL")
            .HasDatabaseName("IX_OutboxEvents_Undispatched");

        builder.HasOne<Job>().WithMany().HasForeignKey(e => e.JobId).OnDelete(DeleteBehavior.Cascade);
    }
}
