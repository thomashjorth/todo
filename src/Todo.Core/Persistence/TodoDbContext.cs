using Microsoft.EntityFrameworkCore;

namespace Todo.Core.Persistence;

public class TodoDbContext(DbContextOptions<TodoDbContext> options) : DbContext(options)
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<SubTask> SubTasks => Set<SubTask>();

    public DbSet<UserAlias> Aliases => Set<UserAlias>();

    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var task = modelBuilder.Entity<TaskItem>();
        task.Property(t => t.Title).IsRequired().HasMaxLength(500);
        task.Property(t => t.SourceId).IsRequired().HasMaxLength(50);
        task.Property(t => t.Status).HasConversion<string>();
        task.HasIndex(t => t.Deadline);

        task.Property(t => t.ExternalKey).HasMaxLength(200);
        task.HasIndex(t => new { t.SourceId, t.ExternalKey });

        task.HasMany(t => t.SubTasks)
            .WithOne()
            .HasForeignKey(s => s.TaskItemId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SubTask>()
            .Property(s => s.Title).IsRequired().HasMaxLength(500);

        var alias = modelBuilder.Entity<UserAlias>();
        alias.Property(a => a.Value).IsRequired().HasMaxLength(200);
        alias.HasIndex(a => a.Value).IsUnique();

        var setting = modelBuilder.Entity<Setting>();
        setting.HasKey(s => s.Key);
        setting.Property(s => s.Key).HasMaxLength(100);
        setting.Property(s => s.Value).IsRequired().HasMaxLength(2000);
    }
}
