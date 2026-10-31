using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;
using TaskTracker.Api.Domain.Entities;
using TaskTracker.Api.Domain.Enums;

namespace TaskTracker.Api.Persistence.Configurations;

public sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable("task_items", t =>
        {
            t.HasCheckConstraint("ck_task_items_status", "status IN ('Todo', 'InProgress', 'Done')");
            t.HasCheckConstraint("ck_task_items_priority", "priority IN ('Low', 'Medium', 'High', 'Urgent')");
        });

        builder.HasKey(t => t.Id);

        builder.Property(t => t.ProjectId)
            .IsRequired();

        builder.Property(t => t.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.Description)
            .HasMaxLength(4000);

        builder.Property(t => t.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(t => t.Priority)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(t => t.DueDate);

        builder.Property(t => t.AssigneeUserId)
            .HasMaxLength(128);

        // Native PostgreSQL text[] array mapping
        builder.Property(t => t.Labels)
            .IsRequired()
            .HasColumnType("text[]");

        builder.Property(t => t.CreatedBy)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(t => t.CreatedAt)
            .IsRequired();

        builder.Property(t => t.UpdatedAt);
        builder.Property(t => t.CompletedAt);
        builder.Property(t => t.DeletedAt);

        // Application-managed optimistic concurrency token (ADR 0011)
        builder.Property(t => t.RowVersion)
            .IsConcurrencyToken()
            .HasDefaultValue(1u)
            .ValueGeneratedNever();

        // Generated stored tsvector column for full-text search
        builder.Property<NpgsqlTsVector>("SearchVector")
            .HasComputedColumnSql("to_tsvector('english', coalesce(title, '') || ' ' || coalesce(description, ''))", stored: true);

        // GIN index for full-text search
        builder.HasIndex("SearchVector")
            .HasMethod("gin")
            .HasDatabaseName("ix_task_items_search_vector");

        // GIN index on labels array
        builder.HasIndex(t => t.Labels)
            .HasMethod("gin")
            .HasDatabaseName("ix_task_items_labels");

        // Hot list query index: project tasks sorted by creation date
        builder.HasIndex(t => new { t.ProjectId, t.CreatedAt, t.Id })
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ix_task_items_project_created_active");

        // Filtered status / due date list index
        builder.HasIndex(t => new { t.ProjectId, t.Status, t.DueDate, t.Id })
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ix_task_items_project_status_due_active");

        builder.HasMany(t => t.Comments)
            .WithOne(c => c.Task)
            .HasForeignKey(c => c.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
