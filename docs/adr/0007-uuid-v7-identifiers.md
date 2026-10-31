# 7. Sequential UUIDv7 (Guid v7) Primary Keys

Date: 2026-10-31
Status: Accepted

## Context

Choosing primary keys for relational databases involves trade-offs:
- **Auto-incrementing integers (`BIGINT`):** Compact and index-friendly, but leak business volume information (guessable IDs) and cannot be generated on client or distributed nodes without database round-trips.
- **Random UUIDv4 (`Guid.NewGuid()`):** Globally unique and unguessable, but causes severe B-tree page fragmentation in PostgreSQL clustered/primary indexes because keys insert in random order.

.NET 9 and 10 introduce native support for RFC 9562 **UUIDv7** via `Guid.CreateVersion7()`.

## Decision

I chose **sequential UUIDv7 (`Guid.CreateVersion7()`)** for all primary keys and entity identifiers across the domain.

1. All entities (`Project`, `ProjectMember`, `TaskItem`, `Comment`) generate their `Id` using `Guid.CreateVersion7()` upon instantiation.
2. Identifiers embed a millisecond Unix timestamp in the most significant bits, guaranteeing monotonic ordering in PostgreSQL B-tree indexes.
3. IDs are opaque and unpredictable for external clients, mitigating ID enumeration attacks.

## Consequences

### Positive
- **High B-tree Index Performance:** New records insert at the right side of the B-tree index, preventing costly page splits and reducing table bloat.
- **Client/Node Side Generation:** IDs can be generated before database persistence, enabling deterministic relationships and unit testing without database round-trips.
- **Security:** Replaces sequential integer enumeration with cryptographic randomness in the least significant bits.

### Negative
- UUIDv7 takes 16 bytes compared to 8 bytes for `BIGINT`. (PostgreSQL handles `uuid` natively and efficiently).
- Millisecond timestamps are embedded in the ID, meaning entity creation time can technically be derived from the ID.

### Alternatives Considered
- **UUIDv4 (`Guid.NewGuid()`):** Rejected due to catastrophic index fragmentation under high insert volume.
- **Sequential Integers / Identity Columns:** Rejected due to insecure direct object reference (IDOR/BOLA) risks and inability to generate IDs offline.
