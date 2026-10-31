using Microsoft.EntityFrameworkCore;
using TaskTracker.Api.Common.Results;
using TaskTracker.Api.Common.Security;
using TaskTracker.Api.Domain.Entities;
using TaskTracker.Api.Domain.Enums;
using TaskTracker.Api.Domain.Errors;
using TaskTracker.Api.Persistence;

namespace TaskTracker.Api.Common.Authorization;

/// <summary>
/// Verifies caller project membership and role capabilities directly against the database on every request.
/// Authorization decisions are never cached.
/// </summary>
public interface IProjectAccessService
{
    Task<Result<ProjectMember>> EnsureAccessAsync(
        Guid projectId,
        ProjectRole minimumRole,
        CancellationToken cancellationToken = default);
}

public sealed class ProjectAccessService(
    AppDbContext dbContext,
    ICurrentUser currentUser) : IProjectAccessService
{
    public async Task<Result<ProjectMember>> EnsureAccessAsync(
        Guid projectId,
        ProjectRole minimumRole,
        CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            return DomainErrors.Security.Unauthorized;
        }

        var userId = currentUser.UserId;

        // Query the database directly. Project membership queries are never cached.
        var member = await dbContext.ProjectMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.ProjectId == projectId && m.UserId == userId,
                cancellationToken);

        // Security rule: If the user is not a member, return 404 NotFound.
        // This prevents private project existence discovery (enumeration attacks).
        if (member is null)
        {
            return DomainErrors.Projects.NotFound;
        }

        // Verify role sufficiency: Viewer (0) <= Editor (1) <= Owner (2)
        if (member.Role < minimumRole)
        {
            return DomainErrors.Security.Forbidden;
        }

        return member;
    }
}
