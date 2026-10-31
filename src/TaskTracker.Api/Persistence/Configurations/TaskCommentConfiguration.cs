using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskTracker.Api.Domain.Entities;

namespace TaskTracker.Api.Persistence.Configurations;

public sealed class TaskCommentConfiguration : IEntityTypeConfiguration<TaskComment>
{
    public void Configure(EntityTypeBuilder<TaskComment> builder)
    {
        builder.ToTable("task_comments");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.TaskId)
            .IsRequired();

        builder.Property(c => c.AuthorUserId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(c => c.Body)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        builder.HasIndex(c => new { c.TaskId, c.CreatedAt })
            .HasDatabaseName("ix_task_comments_task_id_created_at");
    }
}
