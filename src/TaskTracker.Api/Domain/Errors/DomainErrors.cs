using TaskTracker.Api.Common.Results;

namespace TaskTracker.Api.Domain.Errors;

/// <summary>
/// Domain-level error definitions mapped to stable machine-readable codes.
/// </summary>
public static class DomainErrors
{
    public static class Projects
    {
        public static readonly Error NotFound = Error.NotFound(
            "project.not_found", "The specified project was not found.");

        public static readonly Error AlreadyArchived = Error.Conflict(
            "project.already_archived", "The project is already archived.");

        public static readonly Error NotArchived = Error.Conflict(
            "project.not_archived", "The project is not archived.");

        public static readonly Error ArchivedReadOnly = Error.Conflict(
            "project.archived_readonly", "Mutations are not permitted on archived projects.");

        public static readonly Error NameEmpty = Error.Validation(
            "project.name_empty", "Project name cannot be empty.");

        public static readonly Error NameTooLong = Error.Validation(
            "project.name_too_long", "Project name must not exceed 100 characters.");

        public static readonly Error DescriptionTooLong = Error.Validation(
            "project.description_too_long", "Project description must not exceed 1000 characters.");
    }

    public static class Members
    {
        public static readonly Error NotFound = Error.NotFound(
            "member.not_found", "The specified project member was not found.");

        public static readonly Error AlreadyMember = Error.Conflict(
            "member.already_exists", "The user is already a member of this project.");

        public static readonly Error LastOwnerCannotBeRemoved = Error.Conflict(
            "member.last_owner", "The project must always keep at least one owner.");

        public static readonly Error LastOwnerCannotBeDemoted = Error.Conflict(
            "member.demote_last_owner", "The last owner of a project cannot be demoted.");

        public static readonly Error UserIdEmpty = Error.Validation(
            "member.user_id_empty", "Member user identifier cannot be empty.");
    }

    public static class Tasks
    {
        public static readonly Error NotFound = Error.NotFound(
            "task.not_found", "The specified task was not found.");

        public static readonly Error InvalidStatusTransition = Error.Conflict(
            "task.invalid_status_transition", "The requested task status transition is invalid.");

        public static readonly Error AssigneeNotMember = Error.Validation(
            "task.assignee_not_member", "The assigned user must be an active member of the project.");

        public static readonly Error TitleEmpty = Error.Validation(
            "task.title_empty", "Task title cannot be empty.");

        public static readonly Error TitleTooLong = Error.Validation(
            "task.title_too_long", "Task title must not exceed 200 characters.");

        public static readonly Error DescriptionTooLong = Error.Validation(
            "task.description_too_long", "Task description must not exceed 4000 characters.");

        public static readonly Error TooManyLabels = Error.Validation(
            "task.too_many_labels", "A task cannot have more than 10 labels.");

        public static readonly Error InvalidLabelFormat = Error.Validation(
            "task.invalid_label_format", "Labels must be lowercase alphanumeric slugs of 1 to 30 characters.");
    }

    public static class Comments
    {
        public static readonly Error NotFound = Error.NotFound(
            "comment.not_found", "The specified comment was not found.");

        public static readonly Error BodyEmpty = Error.Validation(
            "comment.body_empty", "Comment body cannot be empty.");

        public static readonly Error BodyTooLong = Error.Validation(
            "comment.body_too_long", "Comment body must not exceed 2000 characters.");

        public static readonly Error DeleteForbidden = Error.Forbidden(
            "comment.delete_forbidden", "Only the comment author or a project owner can delete this comment.");
    }

    public static class Concurrency
    {
        public static readonly Error Conflict = Error.Conflict(
            "concurrency.conflict", "The resource was modified by another operation. Please reload and retry.");

        public static readonly Error PreconditionRequired = Error.PreconditionRequired(
            "concurrency.precondition_required", "The If-Match request header is required for updates.");

        public static readonly Error PreconditionFailed = Error.PreconditionFailed(
            "concurrency.precondition_failed", "The resource version does not match the provided If-Match header.");
    }

    public static class Security
    {
        public static readonly Error Forbidden = Error.Forbidden(
            "auth.forbidden", "You do not have permission to perform this action.");

        public static readonly Error Unauthorized = Error.Unauthorized(
            "auth.unauthorized", "Authentication is required to access this resource.");
    }

    public static class Idempotency
    {
        public static readonly Error Mismatch = Error.Validation(
            "idempotency.mismatch", "The provided Idempotency-Key was already used with a different request payload.");

        public static readonly Error InProgress = Error.Conflict(
            "idempotency.in_progress", "A request with this Idempotency-Key is currently being processed.");
    }
}
