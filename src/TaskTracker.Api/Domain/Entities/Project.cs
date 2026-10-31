using TaskTracker.Api.Common.Results;
using TaskTracker.Api.Domain.Errors;

namespace TaskTracker.Api.Domain.Entities;

/// <summary>
/// Collaborative workspace entity containing members and tasks.
/// Enforces business invariants and state transitions directly on the entity.
/// </summary>
public sealed class Project
{
    private readonly List<ProjectMember> _members = [];
    private readonly List<TaskItem> _tasks = [];

    // Private constructor for EF Core materialization
    private Project() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public uint RowVersion { get; private set; } = 1;

    public IReadOnlyCollection<ProjectMember> Members => _members.AsReadOnly();
    public IReadOnlyCollection<TaskItem> Tasks => _tasks.AsReadOnly();

    public bool IsArchived => ArchivedAt.HasValue;
    public bool IsDeleted => DeletedAt.HasValue;

    public static Result<Project> Create(
        string name,
        string? description,
        string createdBy,
        TimeProvider timeProvider)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return DomainErrors.Projects.NameEmpty;
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length > 100)
        {
            return DomainErrors.Projects.NameTooLong;
        }

        var trimmedDescription = description?.Trim();
        if (trimmedDescription?.Length > 1000)
        {
            return DomainErrors.Projects.DescriptionTooLong;
        }

        var project = new Project
        {
            Id = Guid.CreateVersion7(),
            Name = trimmedName,
            Description = string.IsNullOrEmpty(trimmedDescription) ? null : trimmedDescription,
            CreatedBy = createdBy,
            CreatedAt = timeProvider.GetUtcNow()
        };

        return project;
    }

    public Result Update(string name, string? description, TimeProvider timeProvider)
    {
        if (IsArchived)
        {
            return DomainErrors.Projects.ArchivedReadOnly;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return DomainErrors.Projects.NameEmpty;
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length > 100)
        {
            return DomainErrors.Projects.NameTooLong;
        }

        var trimmedDescription = description?.Trim();
        if (trimmedDescription?.Length > 1000)
        {
            return DomainErrors.Projects.DescriptionTooLong;
        }

        Name = trimmedName;
        Description = string.IsNullOrEmpty(trimmedDescription) ? null : trimmedDescription;
        UpdatedAt = timeProvider.GetUtcNow();

        return Result.Success();
    }

    public Result Archive(TimeProvider timeProvider)
    {
        if (IsArchived)
        {
            return DomainErrors.Projects.AlreadyArchived;
        }

        ArchivedAt = timeProvider.GetUtcNow();
        UpdatedAt = ArchivedAt;
        return Result.Success();
    }

    public Result Unarchive(TimeProvider timeProvider)
    {
        if (!IsArchived)
        {
            return DomainErrors.Projects.NotArchived;
        }

        ArchivedAt = null;
        UpdatedAt = timeProvider.GetUtcNow();
        return Result.Success();
    }

    public void SoftDelete(TimeProvider timeProvider)
    {
        var now = timeProvider.GetUtcNow();
        DeletedAt = now;
        UpdatedAt = now;
    }
}
