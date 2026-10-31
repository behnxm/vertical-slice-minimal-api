using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TaskTracker.Api.Domain.Entities;
using TaskTracker.Api.Domain.Enums;
using TaskTracker.Api.Domain.Errors;
using Xunit;

namespace TaskTracker.UnitTests;

public class EntityRuleTests
{
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Project_Create_Enforces_Name_Requirements()
    {
        // Empty name
        var emptyResult = Project.Create("", null, "user-1", _timeProvider);
        emptyResult.IsFailure.ShouldBeTrue();
        emptyResult.Error.Code.ShouldBe(DomainErrors.Projects.NameEmpty.Code);

        // Name too long (> 100)
        var longName = new string('a', 101);
        var longResult = Project.Create(longName, null, "user-1", _timeProvider);
        longResult.IsFailure.ShouldBeTrue();
        longResult.Error.Code.ShouldBe(DomainErrors.Projects.NameTooLong.Code);

        // Valid name
        var validResult = Project.Create("Backend Architecture", "Production VSA implementation", "user-1", _timeProvider);
        validResult.IsSuccess.ShouldBeTrue();
        validResult.Value.Name.ShouldBe("Backend Architecture");
        validResult.Value.Description.ShouldBe("Production VSA implementation");
        validResult.Value.RowVersion.ShouldBe(1u);
        validResult.Value.IsArchived.ShouldBeFalse();
        validResult.Value.IsDeleted.ShouldBeFalse();
    }

    [Fact]
    public void Project_Archive_And_Unarchive_Lifecycle()
    {
        var project = Project.Create("Test Project", null, "user-1", _timeProvider).Value;

        // Archive active project
        var archiveResult = project.Archive(_timeProvider);
        archiveResult.IsSuccess.ShouldBeTrue();
        project.IsArchived.ShouldBeTrue();
        project.ArchivedAt.ShouldNotBeNull();

        // Archiving an already archived project fails with Conflict
        var secondArchiveResult = project.Archive(_timeProvider);
        secondArchiveResult.IsFailure.ShouldBeTrue();
        secondArchiveResult.Error.Code.ShouldBe(DomainErrors.Projects.AlreadyArchived.Code);

        // Mutating archived project fails with Conflict
        var updateResult = project.Update("New Name", null, _timeProvider);
        updateResult.IsFailure.ShouldBeTrue();
        updateResult.Error.Code.ShouldBe(DomainErrors.Projects.ArchivedReadOnly.Code);

        // Unarchive
        var unarchiveResult = project.Unarchive(_timeProvider);
        unarchiveResult.IsSuccess.ShouldBeTrue();
        project.IsArchived.ShouldBeFalse();
        project.ArchivedAt.ShouldBeNull();

        // Unarchiving an active project fails with Conflict
        var secondUnarchiveResult = project.Unarchive(_timeProvider);
        secondUnarchiveResult.IsFailure.ShouldBeTrue();
        secondUnarchiveResult.Error.Code.ShouldBe(DomainErrors.Projects.NotArchived.Code);
    }

    [Theory]
    [InlineData(TaskItemStatus.Todo, TaskItemStatus.InProgress, true)]
    [InlineData(TaskItemStatus.InProgress, TaskItemStatus.Todo, true)]
    [InlineData(TaskItemStatus.InProgress, TaskItemStatus.Done, true)]
    [InlineData(TaskItemStatus.Done, TaskItemStatus.InProgress, true)]
    [InlineData(TaskItemStatus.Todo, TaskItemStatus.Done, false)]
    [InlineData(TaskItemStatus.Done, TaskItemStatus.Todo, false)]
    public void TaskItem_Status_Machine_Enforces_Valid_Transitions(
        TaskItemStatus initial,
        TaskItemStatus target,
        bool shouldSucceed)
    {
        var task = TaskItem.Create(
            Guid.NewGuid(),
            "Task Title",
            "Task Description",
            TaskPriority.High,
            null,
            null,
            null,
            "creator",
            _timeProvider).Value;

        // Advance to initial state
        if (initial == TaskItemStatus.InProgress)
        {
            task.TransitionStatus(TaskItemStatus.InProgress, _timeProvider);
        }
        else if (initial == TaskItemStatus.Done)
        {
            task.TransitionStatus(TaskItemStatus.InProgress, _timeProvider);
            task.TransitionStatus(TaskItemStatus.Done, _timeProvider);
        }

        task.Status.ShouldBe(initial);

        // Attempt transition
        var result = task.TransitionStatus(target, _timeProvider);

        if (shouldSucceed)
        {
            result.IsSuccess.ShouldBeTrue();
            task.Status.ShouldBe(target);

            if (target == TaskItemStatus.Done)
            {
                task.CompletedAt.ShouldNotBeNull();
            }
            else
            {
                task.CompletedAt.ShouldBeNull();
            }
        }
        else
        {
            result.IsFailure.ShouldBeTrue();
            result.Error.Code.ShouldBe(DomainErrors.Tasks.InvalidStatusTransition.Code);
        }
    }

    [Fact]
    public void TaskItem_Enforces_Label_Slug_Invariants()
    {
        var projectId = Guid.NewGuid();

        // Valid slugs (normalized to lowercase)
        var validLabels = new[] { "backend", "dot-net", "csharp14" };
        var validTask = TaskItem.Create(projectId, "Valid", null, TaskPriority.Low, null, null, validLabels, "user", _timeProvider);
        validTask.IsSuccess.ShouldBeTrue();
        validTask.Value.Labels.ShouldBe(validLabels);

        // Invalid slug with uppercase or spaces or special chars
        var invalidSlugTask = TaskItem.Create(projectId, "Invalid", null, TaskPriority.Low, null, null, ["Invalid Slug!"], "user", _timeProvider);
        invalidSlugTask.IsFailure.ShouldBeTrue();
        invalidSlugTask.Error.Code.ShouldBe(DomainErrors.Tasks.InvalidLabelFormat.Code);

        // Too many labels (> 10)
        var elevenLabels = Enumerable.Range(1, 11).Select(i => $"label-{i}").ToArray();
        var tooManyLabelsTask = TaskItem.Create(projectId, "Too Many", null, TaskPriority.Low, null, null, elevenLabels, "user", _timeProvider);
        tooManyLabelsTask.IsFailure.ShouldBeTrue();
        tooManyLabelsTask.Error.Code.ShouldBe(DomainErrors.Tasks.TooManyLabels.Code);
    }
}
