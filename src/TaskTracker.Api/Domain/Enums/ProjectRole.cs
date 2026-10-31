namespace TaskTracker.Api.Domain.Enums;

/// <summary>
/// Membership roles within a project.
/// Viewer: Read-only access to project, tasks, and comments.
/// Editor: Creates and modifies tasks and comments, updates status and assignments.
/// Owner: Full control including project settings, member management, archiving, and deletion.
/// </summary>
public enum ProjectRole
{
    Viewer,
    Editor,
    Owner
}
