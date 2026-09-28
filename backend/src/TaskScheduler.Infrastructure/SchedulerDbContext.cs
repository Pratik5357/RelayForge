using Microsoft.EntityFrameworkCore;
using TaskScheduler.Domain;

namespace TaskScheduler.Infrastructure;

public sealed class SchedulerDbContext : DbContext
{
    public SchedulerDbContext(DbContextOptions<SchedulerDbContext> options) : base(options)
    {
    }

    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobTask> JobTasks => Set<JobTask>();
    public DbSet<TaskDependency> TaskDependencies => Set<TaskDependency>();
    public DbSet<DeadLetteredTask> DeadLetters => Set<DeadLetteredTask>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<WorkerHeartbeat> WorkerHeartbeats => Set<WorkerHeartbeat>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Job>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(32);
            e.HasMany(x => x.Tasks).WithOne(x => x.Job).HasForeignKey(x => x.JobId);
        });

        modelBuilder.Entity<JobTask>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).HasMaxLength(100).IsRequired();
            e.Property(x => x.HandlerType).HasMaxLength(64).IsRequired();
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.JobId, x.Key }).IsUnique();
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.HasIndex(x => x.State);
            e.HasIndex(x => x.LeaseUntil);
        });

        modelBuilder.Entity<TaskDependency>(e =>
        {
            e.HasKey(x => new { x.JobTaskId, x.DependsOnTaskId });
            e.HasOne(x => x.JobTask)
                .WithMany(x => x.Dependencies)
                .HasForeignKey(x => x.JobTaskId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.DependsOnTask)
                .WithMany(x => x.Dependents)
                .HasForeignKey(x => x.DependsOnTaskId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DeadLetteredTask>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.JobTaskId);
        });

        modelBuilder.Entity<IdempotencyRecord>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(200);
        });

        modelBuilder.Entity<WorkerHeartbeat>(e =>
        {
            e.HasKey(x => x.WorkerId);
            e.Property(x => x.WorkerId).HasMaxLength(200);
        });
    }
}
