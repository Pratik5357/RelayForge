using Microsoft.EntityFrameworkCore;
using RelayForge.Domain.Entities;

namespace RelayForge.Infrastructure;

public class RelayForgeDbContext : DbContext
{
    public RelayForgeDbContext(DbContextOptions<RelayForgeDbContext> options)
        : base(options)
    {
    }

    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobTask> JobTasks => Set<JobTask>();
    public DbSet<TaskDependency> TaskDependencies => Set<TaskDependency>();
    public DbSet<TaskAttempt> TaskAttempts => Set<TaskAttempt>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<TaskLogLine> TaskLogs => Set<TaskLogLine>();
    public DbSet<SubmitKey> SubmitKeys => Set<SubmitKey>();
    public DbSet<JobDefinition> JobDefinitions => Set<JobDefinition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RelayForgeDbContext).Assembly);
    }
}
