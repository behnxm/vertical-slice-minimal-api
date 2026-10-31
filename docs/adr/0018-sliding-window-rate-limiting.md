# 18. Multi-Tier Sliding Window Rate Limiting

Date: 2026-10-31
Status: Accepted

## Context

Web APIs are vulnerable to Denial of Service (DoS), brute-force credential stuffing, resource exhaustion (OWASP API 4:2023), and runaway scraping bots. Fixed-window rate limiters create traffic spikes at boundary resets ("thundering herd" at minute intervals).

## Decision

I chose **ASP.NET Core's native `SlidingWindowRateLimiter`** configured with fine-grained partition keys and endpoint policies.

1. **Partition Strategy:**
   - Authenticated callers are partitioned by their user ID (`sub` claim), ensuring limits apply to the authenticated actor regardless of IP hopping or proxies.
   - Unauthenticated callers are partitioned by client IP address (`RemoteIpAddress`).
2. **Endpoint-Specific Policies:**
   - **General Policy (`policy-general`):** 300 requests / 1-minute window (divided into 6 segments) for standard read operations.
   - **Mutation Policy (`policy-mutation`):** 120 requests / 1-minute window for state-changing endpoints (`POST`, `PUT`, `DELETE`).
   - **Search Policy (`policy-search`):** 60 requests / 1-minute window for CPU-intensive full-text search operations.
   - **Auth Policy (`policy-auth`):** 10 requests / 1-minute window for token issuance endpoints (`POST /dev/token`).
3. **Graceful Rejection:**
   - Requests exceeding limits are rejected with **HTTP 429 Too Many Requests**, including a `Retry-After` header indicating the number of seconds until window replenishment.

## Consequences

### Positive
- **Smooth Traffic Curves:** Sliding windows prevent burst spikes at boundary rollovers.
- **Fair Resource Allocation:** Malicious or runaway users cannot degrade service for other tenants.
- **Resource Protection:** Prevents CPU starvation from unindexed searches or mass insertions.

### Negative
- Local in-memory rate limiting applies per API instance. For multi-node autoscaling clusters, an external ingress rate limiter (e.g. Envoy or Cloudflare) or distributed token bucket can complement the host limiter.

### Alternatives Considered
- **Fixed Window Limiting:** Rejected due to boundary burst vulnerability.
- **Token Bucket:** Viable, but sliding window was natively supported in .NET 10 with granular segment tracking.
