# 16. Strict JSON Deserialization and RFC 9457 Problem Details

Date: 2026-10-31
Status: Accepted

## Context

Many REST APIs silently accept malformed or unintended request payloads:
- Unknown properties sent by clients are discarded silently, masking client bugs or enabling mass assignment vulnerabilities (OWASP API 6:2023).
- Strings formatted as integers or invalid enum strings are converted unpredictably.
- Numeric overflow or precision loss occurs when deserializing unconstrained numbers.
- When errors occur, APIs often return disparate formats (custom JSON blobs, stack traces, plain text), frustrating client developers.

## Decision

I chose **strict JSON deserialization policies paired with RFC 9457 Problem Details error modeling**.

1. **Strict JSON Middleware (`StrictJsonMiddleware`):**
   - Validates that mutating requests declare `Content-Type: application/json`.
   - Rejects payloads exceeding maximum size limits (1 MB).
   - Rejects unknown properties or invalid string enums using strict `JsonSerializerOptions` (`AllowOutOfRangeFloatingPointOrIntegerValues = false`, `JsonStringEnumConverter(allowIntegerValues: false)`).
2. **RFC 9457 Problem Details:**
   - All errors are formatted as RFC 9457 compliant Problem Details schemas:
     - `type`: Opaque URI referencing error taxonomy (e.g. `https://errors.tasktracker.example.com/tasks.not_found`).
     - `title`: Short human-readable summary.
     - `status`: Standard HTTP status code (400, 401, 403, 404, 409, 412, 422, 429).
     - `detail`: Specific explanation of the failure.
     - `instance`: Request trace URI.
     - `errors`: Dictionary of field-level validation failures when status is 422 or 400.
3. Architecture tests verify that every endpoint maps failures through `Result.ToHttpResult()`.

## Consequences

### Positive
- Total protection against mass assignment and parameter pollution.
- Standardized, predictable error envelopes for client SDK generators and frontend applications.
- Zero leakage of raw internal exceptions or database error details.

### Negative
- Clients must adhere strictly to JSON contracts (e.g. string enums must match exact camelCase casing).

### Alternatives Considered
- **Default System.Text.Json Permissive Behavior:** Rejected as unsafe for production security postures.
