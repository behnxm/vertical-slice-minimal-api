# 12. RFC-Style Idempotency Key Handling with Distributed Valkey Locks

Date: 2026-10-31
Status: Accepted

## Context

In distributed systems, network partitions and client retries can cause identical mutating HTTP requests (`POST`) to be dispatched multiple times. Without idempotency handling, duplicate orders, double charges, or duplicate tasks are created.

Naively checking entity existence in the database is insufficient because:
1. Two concurrent requests arriving at the same millisecond will both pass the existence check before either commits.
2. If a request is currently executing ("in flight"), a concurrent retry should receive `409 Conflict`, not execute in parallel.
3. If a client sends an identical idempotency key with a *different* request payload, it represents a client error or security tamper attempt and must be rejected with `422 Unprocessable Content`.

## Decision

I chose **endpoint-filtered idempotency handling backed by Valkey (Redis-compatible) distributed locking**.

1. Mutation endpoints (`CreateProject`, `CreateTask`, `CreateComment`) register an `IdempotencyFilter`.
2. Clients supply an optional or required `Idempotency-Key` header (UUID or string up to 128 characters).
3. The request stream is buffered (`context.Request.EnableBuffering()`) to compute a SHA-256 hash of the request body.
4. An atomic `SETNX` lock is acquired in Valkey with a 1-minute expiration:
   - If the key exists and its state is `in_progress`, the filter immediately terminates the pipeline and returns RFC 9457 **`409 Conflict`**.
   - If the key exists with state `completed`, the filter compares the incoming payload SHA-256 hash with the stored hash:
     - If the hashes match, the stored HTTP status code, headers, and cached response payload are replayed directly (**`200 OK` / `201 Created`** with an `Idempotent-Replayed: true` header).
     - If the hashes mismatch, the filter returns RFC 9457 **`422 Unprocessable Content`**.
5. Once the inner handler succeeds, the key status is transitioned to `completed` with a 24-hour replay retention window.

## Consequences

### Positive
- **Complete Replay Safety:** Clients can safely retry network-dropped mutation requests without creating duplicate resources.
- **Race Condition Prevention:** The Valkey atomic lock guarantees that only one execution runs at a time.
- **Payload Integrity:** Mismatched payloads with recycled keys are rejected deterministically.

### Negative
- Requires a distributed cache (Valkey) to be reachable for mutation endpoints using idempotency keys. (An in-memory fallback is provided for isolated unit test environments).

### Alternatives Considered
- **Database Table Locks:** Storing idempotency records in PostgreSQL adds write contention and table bloat for ephemeral 24-hour tokens.
