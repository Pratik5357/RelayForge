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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RelayForgeDbContext).Assembly);
    }
}
