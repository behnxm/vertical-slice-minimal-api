using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskTracker.Api.Domain.Entities;
using TaskTracker.Api.Domain.Enums;

namespace TaskTracker.Api.Persistence.Configurations;

public sealed class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> builder)
    {
        builder.ToTable("project_members", t =>
        {
            t.HasCheckConstraint("ck_project_members_role", "role IN ('Viewer', 'Editor', 'Owner')");
        });

        builder.HasKey(m => new { m.ProjectId, m.UserId });

        builder.Property(m => m.UserId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(m => m.Role)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(m => m.AddedBy)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(m => m.AddedAt)
            .IsRequired();

        // Reverse lookup index: quickly list all projects where a user is a member
        builder.HasIndex(m => new { m.UserId, m.ProjectId })
            .HasDatabaseName("ix_project_members_user_id_project_id");
    }
}
