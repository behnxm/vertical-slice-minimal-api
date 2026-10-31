using System.Text.RegularExpressions;
using TaskTracker.Api.Common.Results;
using TaskTracker.Api.Domain.Enums;
using TaskTracker.Api.Domain.Errors;

namespace TaskTracker.Api.Domain.Entities;

/// <summary>
/// Task entity representing a work item within a project.
/// Enforces status transition rules and label invariants directly on the entity.
/// </summary>
public sealed partial class TaskItem
{
    private static readonly Regex LabelSlugRegex = new(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);
    private readonly List<TaskComment> _comments = [];

    // Private constructor for EF Core
    private TaskItem() { }

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public TaskItemStatus Status { get; private set; }
    public TaskPriority Priority { get; private set; }
    public DateTimeOffset? DueDate { get; private set; }
    public string? AssigneeUserId { get; private set; }
    public string[] Labels { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public uint RowVersion { get; private set; } = 1;

    public Project? Project { get; private set; }
    public IReadOnlyCollection<TaskComment> Comments => _comments.AsReadOnly();

    public bool IsDeleted => DeletedAt.HasValue;

    public static Result<TaskItem> Create(
        Guid projectId,
        string title,
        string? description,
        TaskPriority priority,
        DateTimeOffset? dueDate,
        string? assigneeUserId,
        string[]? labels,
        string createdBy,
        TimeProvider timeProvider)
    {
        if (projectId == Guid.Empty)
        {
            return DomainErrors.Projects.NotFound;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return DomainErrors.Tasks.TitleEmpty;
        }

        var trimmedTitle = title.Trim();
        if (trimmedTitle.Length > 200)
        {
            return DomainErrors.Tasks.TitleTooLong;
        }

        var trimmedDescription = description?.Trim();
        if (trimmedDescription?.Length > 4000)
        {
            return DomainErrors.Tasks.DescriptionTooLong;
        }

        var validatedLabels = Array.Empty<string>();
        if (labels is { Length: > 0 })
        {
            var labelResult = ValidateAndNormalizeLabels(labels);
            if (labelResult.IsFailure)
            {
                return labelResult.Error;
            }
            validatedLabels = labelResult.Value;
        }

        var task = new TaskItem
        {
            Id = Guid.CreateVersion7(),
            ProjectId = projectId,
            Title = trimmedTitle,
            Description = string.IsNullOrEmpty(trimmedDescription) ? null : trimmedDescription,
            Status = TaskItemStatus.Todo,
            Priority = priority,
            DueDate = dueDate?.ToUniversalTime(),
            AssigneeUserId = string.IsNullOrWhiteSpace(assigneeUserId) ? null : assigneeUserId.Trim(),
            Labels = validatedLabels,
            CreatedBy = createdBy,
            CreatedAt = timeProvider.GetUtcNow()
        };

        return task;
    }

    public Result UpdateDetails(
        string title,
        string? description,
        TaskPriority priority,
        DateTimeOffset? dueDate,
        string[]? labels,
        TimeProvider timeProvider)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return DomainErrors.Tasks.TitleEmpty;
        }

        var trimmedTitle = title.Trim();
        if (trimmedTitle.Length > 200)
        {
            return DomainErrors.Tasks.TitleTooLong;
        }

        var trimmedDescription = description?.Trim();
        if (trimmedDescription?.Length > 4000)
        {
            return DomainErrors.Tasks.DescriptionTooLong;
        }

        var validatedLabels = Array.Empty<string>();
        if (labels is { Length: > 0 })
        {
            var labelResult = ValidateAndNormalizeLabels(labels);
            if (labelResult.IsFailure)
            {
                return labelResult.Error;
            }
            validatedLabels = labelResult.Value;
        }

        Title = trimmedTitle;
        Description = string.IsNullOrEmpty(trimmedDescription) ? null : trimmedDescription;
        Priority = priority;
        DueDate = dueDate?.ToUniversalTime();
        Labels = validatedLabels;
        UpdatedAt = timeProvider.GetUtcNow();

        return Result.Success();
    }

    /// <summary>
    /// Executes the task status state machine:
    /// Todo -> InProgress
    /// InProgress -> Todo
    /// InProgress -> Done (sets CompletedAt)
    /// Done -> InProgress (clears CompletedAt)
    /// Any other transition returns a Conflict error.
    /// </summary>
    public Result TransitionStatus(TaskItemStatus newStatus, TimeProvider timeProvider)
    {
        if (Status == newStatus)
        {
            return Result.Success();
        }

        var isValidTransition = (Status, newStatus) switch
        {
            (TaskItemStatus.Todo, TaskItemStatus.InProgress) => true,
            (TaskItemStatus.InProgress, TaskItemStatus.Todo) => true,
            (TaskItemStatus.InProgress, TaskItemStatus.Done) => true,
            (TaskItemStatus.Done, TaskItemStatus.InProgress) => true,
            _ => false
        };

        if (!isValidTransition)
        {
            return DomainErrors.Tasks.InvalidStatusTransition;
        }

        Status = newStatus;
        var now = timeProvider.GetUtcNow();
        UpdatedAt = now;

        if (newStatus == TaskItemStatus.Done)
        {
            CompletedAt = now;
        }
        else
        {
            CompletedAt = null;
        }

        return Result.Success();
    }

    public void Assign(string? assigneeUserId, TimeProvider timeProvider)
    {
        AssigneeUserId = string.IsNullOrWhiteSpace(assigneeUserId) ? null : assigneeUserId.Trim();
        UpdatedAt = timeProvider.GetUtcNow();
    }

    public void SoftDelete(TimeProvider timeProvider)
    {
        var now = timeProvider.GetUtcNow();
        DeletedAt = now;
        UpdatedAt = now;
    }

    public void Restore()
    {
        DeletedAt = null;
    }

    private static Result<string[]> ValidateAndNormalizeLabels(string[] labels)
    {
        if (labels.Length > 10)
        {
            return DomainErrors.Tasks.TooManyLabels;
        }

        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in labels)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return DomainErrors.Tasks.InvalidLabelFormat;
            }

            var slug = raw.Trim().ToLowerInvariant();
            if (slug.Length is < 1 or > 30 || !LabelSlugRegex.IsMatch(slug))
            {
                return DomainErrors.Tasks.InvalidLabelFormat;
            }

            unique.Add(slug);
        }

        return unique.ToArray();
    }
}
