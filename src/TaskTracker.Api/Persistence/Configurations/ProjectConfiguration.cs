using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskTracker.Api.Domain.Entities;

namespace TaskTracker.Api.Persistence.Configurations;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.Description)
            .HasMaxLength(1000);

        builder.Property(p => p.CreatedBy)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.Property(p => p.UpdatedAt);
        builder.Property(p => p.ArchivedAt);
        builder.Property(p => p.DeletedAt);

        // Application-managed optimistic concurrency token (ADR 0011)
        // Avoids provider-specific DDL collisions with PostgreSQL system columns
        builder.Property(p => p.RowVersion)
            .IsConcurrencyToken()
            .HasDefaultValue(1u)
            .ValueGeneratedNever();

        // Hot list query index: active projects sorted by creation date
        builder.HasIndex(p => new { p.CreatedAt, p.Id })
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ix_projects_created_at_id_active");

        builder.HasMany(p => p.Members)
            .WithOne(m => m.Project)
            .HasForeignKey(m => m.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Tasks)
            .WithOne(t => t.Project)
            .HasForeignKey(t => t.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
