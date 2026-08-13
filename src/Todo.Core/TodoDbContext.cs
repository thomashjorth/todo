using Microsoft.EntityFrameworkCore;

namespace Todo.Core;

public class TodoDbContext(DbContextOptions<TodoDbContext> options) : DbContext(options)
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<SubTask> SubTasks => Set<SubTask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var task = modelBuilder.Entity<TaskItem>();
        task.Property(t => t.Title).IsRequired().HasMaxLength(500);
        task.Property(t => t.SourceId).IsRequired().HasMaxLength(50);
        task.Property(t => t.Status).HasConversion<string>();
        task.HasIndex(t => t.Deadline);

        task.HasMany(t => t.SubTasks)
            .WithOne()
            .HasForeignKey(s => s.TaskItemId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SubTask>()
            .Property(s => s.Title).IsRequired().HasMaxLength(500);
    }
}
