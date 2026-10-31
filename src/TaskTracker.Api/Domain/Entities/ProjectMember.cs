using TaskTracker.Api.Common.Results;
using TaskTracker.Api.Domain.Enums;
using TaskTracker.Api.Domain.Errors;

namespace TaskTracker.Api.Domain.Entities;

/// <summary>
/// Membership entity defining a user's role and capabilities within a project.
/// </summary>
public sealed class ProjectMember
{
    // Private constructor for EF Core
    private ProjectMember() { }

    public Guid ProjectId { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public ProjectRole Role { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }
    public string AddedBy { get; private set; } = string.Empty;

    public Project? Project { get; private set; }

    public static Result<ProjectMember> Create(
        Guid projectId,
        string userId,
        ProjectRole role,
        string addedBy,
        TimeProvider timeProvider)
    {
        if (projectId == Guid.Empty)
        {
            return DomainErrors.Projects.NotFound;
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return DomainErrors.Members.UserIdEmpty;
        }

        var trimmedUserId = userId.Trim();
        if (trimmedUserId.Length > 128)
        {
            return Error.Validation("member.user_id_too_long", "User identifier must not exceed 128 characters.");
        }

        var member = new ProjectMember
        {
            ProjectId = projectId,
            UserId = trimmedUserId,
            Role = role,
            AddedBy = addedBy,
            AddedAt = timeProvider.GetUtcNow()
        };

        return member;
    }

    public void UpdateRole(ProjectRole newRole)
    {
        Role = newRole;
    }
}
