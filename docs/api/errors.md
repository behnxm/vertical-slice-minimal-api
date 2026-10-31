# Error Catalog and Problem Details Reference

The Task Tracker API formats all error responses according to the **RFC 9457 Problem Details for HTTP APIs** specification. This guarantees that clients receive structured, predictable error envelopes across all failure modes.

---

## Standard Problem Details Envelope

```json
{
  "type": "https://errors.tasktracker.example.com/tasks.not_found",
  "title": "Task Not Found",
  "status": 404,
  "detail": "The requested task was not found or has been deleted.",
  "instance": "/api/v1/tasks/0192e4a8-6f10-7000-8000-000000000000",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "errors": {}
}
```

---

## Error Catalog by Domain

### 1. General & Infrastructure Errors
| Error Code | HTTP Status | Title | Description |
| :--- | :--- | :--- | :--- |
| `general.validation_error` | 422 | Validation Error | One or more request validation rules failed. |
| `general.rate_limit_exceeded`| 429 | Too Many Requests | Sliding window rate limit exceeded. |
| `general.unauthorized` | 401 | Unauthorized | Missing or invalid Bearer access token. |
| `general.forbidden` | 403 | Forbidden | Missing required OAuth scope for this endpoint. |
| `general.concurrency_conflict`| 412 / 409 | Precondition Failed / Conflict | Resource modified concurrently. Re-fetch and retry with updated ETag. |
| `general.idempotency_conflict`| 409 | Concurrent Request In Flight | An identical idempotency key is currently processing. |
| `general.idempotency_payload_mismatch` | 422 | Payload Mismatch | Idempotency key reused with a different request payload. |

### 2. Project Errors
| Error Code | HTTP Status | Title | Description |
| :--- | :--- | :--- | :--- |
| `projects.not_found` | 404 | Project Not Found | Project does not exist, or caller lacks membership access. |
| `projects.access_denied` | 404 | Access Denied | Caller does not possess sufficient role permissions in this project. |
| `projects.already_archived` | 409 | Project Already Archived | Attempted to archive an already archived project. |
| `projects.not_archived` | 409 | Project Not Archived | Attempted to unarchive an active project. |
| `projects.archived_immutable` | 409 | Project Is Archived | Mutating tasks or members is forbidden while project is archived. |

### 3. Member Errors
| Error Code | HTTP Status | Title | Description |
| :--- | :--- | :--- | :--- |
| `members.already_exists` | 409 | Member Already Exists | User is already a member of this project. |
| `members.not_found` | 404 | Member Not Found | User is not a member of this project. |
| `members.cannot_demote_last_owner` | 409 | Cannot Demote Last Owner | A project must retain at least one active Owner. |
| `members.cannot_remove_last_owner` | 409 | Cannot Remove Last Owner | The last project Owner cannot be removed. |

### 4. Task Errors
| Error Code | HTTP Status | Title | Description |
| :--- | :--- | :--- | :--- |
| `tasks.not_found` | 404 | Task Not Found | Task does not exist, is soft-deleted, or belongs to an unauthorized project. |
| `tasks.assignee_not_member`| 422 | Assignee Not Member | Assigned user is not an active member of the project. |
| `tasks.invalid_status_transition` | 422 | Invalid Status Transition | Requested task status change violates domain state machine rules. |
| `tasks.cannot_restore_active`| 409 | Task Is Active | Cannot restore a task that is not soft-deleted. |

### 5. Comment Errors
| Error Code | HTTP Status | Title | Description |
| :--- | :--- | :--- | :--- |
| `comments.not_found` | 404 | Comment Not Found | Comment does not exist or task is inaccessible. |
| `comments.forbidden` | 403 | Forbidden | Deleting a comment requires comment authorship or project Owner role. |
