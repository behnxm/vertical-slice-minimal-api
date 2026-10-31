using TaskTracker.Api.Common.Results;
using TaskTracker.Api.Domain.Errors;

namespace TaskTracker.Api.Domain.Entities;

/// <summary>
/// Comment posted on a task item.
/// Immutable once created; deletion is restricted to the author and project owners.
/// </summary>
public sealed class TaskComment
{
    // Private constructor for EF Core
    private TaskComment() { }

    public Guid Id { get; private set; }
    public Guid TaskId { get; private set; }
    public string AuthorUserId { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public TaskItem? Task { get; private set; }

    public static Result<TaskComment> Create(
        Guid taskId,
        string authorUserId,
        string body,
        TimeProvider timeProvider)
    {
        if (taskId == Guid.Empty)
        {
            return DomainErrors.Tasks.NotFound;
        }

        if (string.IsNullOrWhiteSpace(authorUserId))
        {
            return DomainErrors.Security.Unauthorized;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return DomainErrors.Comments.BodyEmpty;
        }

        var trimmedBody = body.Trim();
        if (trimmedBody.Length > 2000)
        {
            return DomainErrors.Comments.BodyTooLong;
        }

        var comment = new TaskComment
        {
            Id = Guid.CreateVersion7(),
            TaskId = taskId,
            AuthorUserId = authorUserId.Trim(),
            Body = trimmedBody,
            CreatedAt = timeProvider.GetUtcNow()
        };

        return comment;
    }
}
