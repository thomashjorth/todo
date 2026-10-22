using Microsoft.EntityFrameworkCore;
using Todo.Core.Tags;

namespace Todo.Core.Persistence;

public class TodoDbContext(DbContextOptions<TodoDbContext> options) : DbContext(options)
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<SubTask> SubTasks => Set<SubTask>();

    public DbSet<UserAlias> Aliases => Set<UserAlias>();

    public DbSet<Setting> Settings => Set<Setting>();

    public DbSet<Tag> Tags => Set<Tag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var task = modelBuilder.Entity<TaskItem>();
        task.Property(t => t.Title).IsRequired().HasMaxLength(500);
        task.Property(t => t.SourceId).IsRequired().HasMaxLength(50);
        task.Property(t => t.Status).HasConversion<string>();
        task.HasIndex(t => t.Deadline);

        task.Property(t => t.WaitingOn).HasMaxLength(200);

        task.Property(t => t.ExternalKey).HasMaxLength(200);
        task.HasIndex(t => new { t.SourceId, t.ExternalKey });

        task.HasMany(t => t.SubTasks)
            .WithOne()
            .HasForeignKey(s => s.TaskItemId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SubTask>()
            .Property(s => s.Title).IsRequired().HasMaxLength(500);

        // NOCASE, so "Kunde" and "kunde" are one tag to the unique index as they are to a reader.
        var tag = modelBuilder.Entity<Tag>();
        tag.Property(t => t.Name).IsRequired().HasMaxLength(Tag.NameMaxLength).UseCollation("NOCASE");
        tag.HasIndex(t => t.Name).IsUnique();

        // A named join table with named columns rather than EF's "TaskItemTag", so the SQL a
        // migration test reads says what it is.
        task.HasMany(t => t.Tags)
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "TaskTags",
                join => join.HasOne<Tag>().WithMany().HasForeignKey("TagId").OnDelete(DeleteBehavior.Cascade),
                join => join.HasOne<TaskItem>().WithMany().HasForeignKey("TaskItemId").OnDelete(DeleteBehavior.Cascade));

        var alias = modelBuilder.Entity<UserAlias>();
        alias.Property(a => a.Value).IsRequired().HasMaxLength(200);
        alias.HasIndex(a => a.Value).IsUnique();

        var setting = modelBuilder.Entity<Setting>();
        setting.HasKey(s => s.Key);
        setting.Property(s => s.Key).HasMaxLength(100);
        setting.Property(s => s.Value).IsRequired().HasMaxLength(2000);
    }
}
