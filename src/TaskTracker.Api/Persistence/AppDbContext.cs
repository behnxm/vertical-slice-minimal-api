using Microsoft.EntityFrameworkCore;
using TaskTracker.Api.Common.Security;
using TaskTracker.Api.Domain.Entities;

namespace TaskTracker.Api.Persistence;

/// <summary>
/// Application database context managing entity persistence, schema configurations,
/// and EF Core 10 named query filters providing defence-in-depth isolation.
/// </summary>
public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options,
    ICurrentUser? currentUser = null) : DbContext(options)
{
    public const string FilterSoftDelete = "SoftDelete";
    public const string FilterMembership = "Membership";

    public string? CurrentUserId => currentUser?.UserId;

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<TaskComment> Comments => Set<TaskComment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply entity configurations from assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // EF Core 10 Named Query Filters
        // Defence-in-depth rule:
        // 1. SoftDelete: Hides soft-deleted records unless explicitly bypassed (e.g. restore slice or background purge).
        // 2. Membership: Ensures callers only access projects and tasks they are active members of.
        // The Membership filter FAILS CLOSED: if CurrentUserId is null (unauthenticated / background context), matches zero rows.
        modelBuilder.Entity<Project>()
            .HasQueryFilter(FilterSoftDelete, p => p.DeletedAt == null)
            .HasQueryFilter(FilterMembership, p => CurrentUserId != null && p.Members.Any(m => m.UserId == CurrentUserId));

        modelBuilder.Entity<TaskItem>()
            .HasQueryFilter(FilterSoftDelete, t => t.DeletedAt == null)
            .HasQueryFilter(FilterMembership, t => CurrentUserId != null && t.Project != null && t.Project.Members.Any(m => m.UserId == CurrentUserId));
    }
}
