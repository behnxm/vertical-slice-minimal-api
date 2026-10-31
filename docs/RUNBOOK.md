# Production Operations Runbook

This runbook provides actionable procedures for deploying, monitoring, operating, and troubleshooting the Task Tracker API in production environments.

---

## Architecture at a Glance

- **Web Runtime:** ASP.NET Core Minimal APIs (.NET 10 LTS), hosted on Ubuntu Noble Chiseled container.
- **Primary Database:** PostgreSQL 17 (relational persistence, GIN indexes, full-text search).
- **Distributed Cache & Locks:** Valkey 8.0 (Redis-compatible L2 cache and idempotency lock store).
- **Telemetry:** OpenTelemetry OTLP tracing and custom metrics (`TaskTracker.Api`).

---

## Deployment Procedures

### 1. Zero-Downtime Database Migration
Never run migrations on application startup. Execute the dedicated migration job prior to deploying new web replicas:

```bash
# Docker Compose environment:
docker compose run --rm migrate

# Kubernetes environment:
kubectl apply -f k8s/migration-job.yaml
kubectl wait --for=condition=complete job/tasktracker-migrate --timeout=120s
```

### 2. Rolling Web Pod Deployment
Once the migration job exits with status 0, update web replicas. ASP.NET Core Minimal APIs boot in < 1 second. Health checks gate traffic routing:
- `/health/live`: Probes container liveness.
- `/health/ready`: Probes PostgreSQL connectivity and Valkey responsiveness.

---

## Monitoring & Alerting Runbooks

### Alert: `TaskTrackerDegradedCache`
- **Symptom:** The `/health/ready` endpoint returns HTTP 200 with status `Degraded`, and `entries.valkey_cache.status == "Degraded"`.
- **Impact:** Low to Medium. HybridCache automatically fails open: all queries transparently query the primary PostgreSQL database. No end-user requests fail.
- **Action:**
  1. Check Valkey container status:
     ```bash
     docker compose ps valkey
     docker compose logs --tail=100 valkey
     ```
  2. Test Valkey responsiveness:
     ```bash
     docker exec -it tasktracker-valkey valkey-cli ping
     ```
  3. Verify memory usage. If memory is exhausted, check maxmemory configuration and eviction policy (`volatile-lru` or `allkeys-lru`).
  4. Restart Valkey if hung:
     ```bash
     docker compose restart valkey
     ```

### Alert: `TaskTrackerDatabaseUnhealthy`
- **Symptom:** `/health/ready` returns HTTP 503 Service Unavailable with `database.status == "Unhealthy"`.
- **Impact:** Critical. The API cannot serve requests.
- **Action:**
  1. Inspect PostgreSQL container status and disk utilization:
     ```bash
     docker compose logs --tail=100 postgres
     df -h
     ```
  2. Check PostgreSQL connection pool exhaustion:
     ```sql
     SELECT count(*), state FROM pg_stat_activity GROUP BY state;
     ```
  3. Verify database connectivity credentials in secret stores.

### Alert: `TaskTrackerHighRateLimiting`
- **Symptom:** Surge in HTTP 429 Too Many Requests responses.
- **Impact:** Potential client scraping, runaway bot, or brute force activity.
- **Action:**
  1. Inspect OpenTelemetry traces filtered by HTTP 429.
  2. Identify the offending `sub` (authenticated user) or client IP.
  3. If legitimate traffic is hitting limits, consider adjusting policy windows in `RateLimitingExtensions.cs` or granting specialized client tier quotas.

---

## Routine Maintenance Operations

### 1. Soft-Delete Task Purging
Soft-deleted tasks are retained for 30 days to support accidental deletion recovery. After 30 days, `SoftDeletePurgeService` runs automatically every 24 hours to permanently delete expired tasks in batches of 500:
- Purge query uses an explicit, allowlisted `IgnoreQueryFilters()` call.
- Associated comments are cascaded automatically via PostgreSQL foreign key constraints.

### 2. Manual Backup & Restore
```bash
# Backup PostgreSQL database
docker exec -t tasktracker-postgres pg_dump -U tasktracker -d tasktracker -F c -b -v -f /tmp/backup.dump

# Copy backup to host
docker cp tasktracker-postgres:/tmp/backup.dump ./backup-$(date +%Y%m%d).dump

# Restore database
docker exec -i tasktracker-postgres pg_restore -U tasktracker -d tasktracker -v --clean /tmp/backup.dump
```
