namespace TaskTracker.Api.Domain.Enums;

/// <summary>
/// Lifecycle status of a task item.
/// Transitions are strictly controlled by the TaskItem entity state machine.
/// </summary>
public enum TaskItemStatus
{
    Todo,
    InProgress,
    Done
}
