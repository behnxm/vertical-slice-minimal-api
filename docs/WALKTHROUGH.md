# End-to-End User Manual & Step-by-Step Testing Walkthrough

This step-by-step user manual walks you through running, testing, and interacting with the Task Tracker API from scratch.

---

## Prerequisites Check

Before starting, ensure you have the following installed:
1. **.NET 10 SDK (`net10.0`)**: Verify by running `dotnet --version` (should be `10.0.100` or higher).
2. **Docker Desktop**: Verify by running `docker compose version` and `docker ps`.

---

## Step 1: Initialize Local Development Keys

In this repository, Ground Rule 6 mandates **zero secrets in git**. Security keys for signing local development JWT tokens are generated dynamically on your machine.

Open your terminal in the repository root and run:

```powershell
# On Windows (PowerShell):
./scripts/init.ps1
```

```bash
# On Linux / macOS:
./scripts/init.sh
```

**What this does:**
- Generates an ECDSA P-256 private key in `./dev-keys/dev-token-es256.key` (ignored by git).
- Copies `.env.example` to `.env` if `.env` does not already exist.

---

## Step 2: Start Infrastructure & Apply Database Migrations

You have two convenient ways to run the system:

### Option A: The Full Docker Compose Stack (Recommended)
This starts PostgreSQL 17, Valkey 8.0, runs the EF Core database migrations as a one-shot job, and starts the API in a hardened container:

```bash
docker compose up -d
```

Verify that all containers are healthy:
```bash
docker compose ps
```

You should see:
- `tasktracker-postgres`: Up (healthy)
- `tasktracker-valkey`: Up (healthy)
- `tasktracker-migrate`: Exited (0) (Migration applied successfully)
- `tasktracker-api`: Up (healthy) on port `8080`

---

### Option B: Run the API Locally via .NET CLI (For Active Debugging)
If you want to debug code in Visual Studio, VS Code, or JetBrains Rider:

1. Start only PostgreSQL and Valkey:
   ```bash
   docker compose up -d postgres valkey
   ```
2. Apply database migrations to PostgreSQL using the API's built-in migration command:
   ```bash
   dotnet run --project src/TaskTracker.Api -- --migrate
   ```
3. Optionally seed sample demo data (projects, members, tasks, comments):
   ```bash
   dotnet run --project src/TaskTracker.Api -- --seed-demo
   ```
4. Start the API web server:
   ```bash
   dotnet run --project src/TaskTracker.Api
   ```
   The API will listen on `http://localhost:8080`.

---

## Step 3: Verify Application Health

Once the API is running, verify that all dependencies are healthy.

### 1. Liveness Check (Kubernetes / Container Probe)
```bash
curl -i http://localhost:8080/health/live
```
**Expected Response:** `HTTP/1.1 200 OK`
```json
{"status":"Healthy"}
```

### 2. Readiness Check (Database & Valkey Cache)
```bash
curl -i http://localhost:8080/health/ready
```
**Expected Response:** `HTTP/1.1 200 OK`
```json
{
  "status": "Healthy",
  "entries": {
    "database": { "status": "Healthy" },
    "valkey_cache": { "status": "Healthy", "description": "L2 Valkey cache is responsive." }
  }
}
```

---

## Step 4: Explore Interactive API Docs (Scalar UI)

Open your web browser and navigate to:
- **Interactive Scalar UI:** [http://localhost:8080/scalar/v1](http://localhost:8080/scalar/v1)
- **Raw OpenAPI JSON Spec:** [http://localhost:8080/openapi/v1.json](http://localhost:8080/openapi/v1.json)

The Scalar UI lets you explore all endpoints, inspect JSON schemas, and test requests interactively.

---

## Step 5: Complete User Flow via HTTP (Step-by-Step)

Here is a complete walkthrough using standard `curl` commands (or PowerShell `Invoke-RestMethod`).

### 1. Acquire a JWT Bearer Token
In local development, the API includes a built-in development token issuer at `POST /dev/token`:

```bash
curl -X POST http://localhost:8080/dev/token \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "alice",
    "scopes": ["tasks.read", "tasks.write", "tasks.admin"]
  }'
```

**Response:**
```json
{
  "tokenType": "Bearer",
  "accessToken": "eyJhbGciOiJFUzI1NiIs...",
  "expiresIn": 3600
}
```
*Copy the `accessToken` value for the subsequent requests.*

---

### 2. Create a New Project
```bash
curl -X POST http://localhost:8080/api/v1/projects \
  -H "Authorization: Bearer <YOUR_ACCESS_TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Apollo Mission",
    "description": "Lunar exploration and collaboration workspace"
  }'
```

**Response (`201 Created`):**
```json
{
  "id": "0192e5b0-1a2b-7000-8000-000000000001",
  "name": "Apollo Mission",
  "description": "Lunar exploration and collaboration workspace",
  "createdAt": "2026-11-01T00:00:00Z",
  "createdBy": "alice",
  "rowVersion": 1
}
```
*Note that the user `alice` is automatically assigned the `Owner` role in this project.*

---

### 3. Add a Member to the Project
Let's add user `bob` as an `Editor`:

```bash
curl -X POST http://localhost:8080/api/v1/projects/0192e5b0-1a2b-7000-8000-000000000001/members \
  -H "Authorization: Bearer <YOUR_ACCESS_TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "bob",
    "role": "Editor"
  }'
```

**Response (`201 Created`):**
```json
{
  "projectId": "0192e5b0-1a2b-7000-8000-000000000001",
  "userId": "bob",
  "role": "Editor",
  "addedAt": "2026-11-01T00:01:00Z",
  "addedBy": "alice"
}
```

---

### 4. Create a Task (With Idempotency Key)
Let's create a task. We supply an `Idempotency-Key` header so that retrying the exact same request never creates duplicate tasks:

```bash
curl -X POST http://localhost:8080/api/v1/projects/0192e5b0-1a2b-7000-8000-000000000001/tasks \
  -H "Authorization: Bearer <YOUR_ACCESS_TOKEN>" \
  -H "Idempotency-Key: task-create-req-12345" \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Assemble Lunar Module",
    "description": "Ensure propulsion systems and oxygen tanks are sealed.",
    "priority": "High",
    "assigneeUserId": "bob",
    "labels": ["hardware", "critical"]
  }'
```

**Response (`201 Created`):**
```json
{
  "id": "0192e5b0-3c4d-7000-8000-000000000002",
  "projectId": "0192e5b0-1a2b-7000-8000-000000000001",
  "title": "Assemble Lunar Module",
  "status": "Todo",
  "priority": "High",
  "assigneeUserId": "bob",
  "labels": ["hardware", "critical"],
  "rowVersion": 1
}
```

*Tip: Re-run the exact same `curl` command with the same `Idempotency-Key` header. Notice that you get `201 Created` with an `Idempotent-Replayed: true` header without creating a duplicate task!*

---

### 5. Read Task with Conditional Request (HTTP Weak ETag)
Fetch the task details:

```bash
curl -i http://localhost:8080/api/v1/tasks/0192e5b0-3c4d-7000-8000-000000000002 \
  -H "Authorization: Bearer <YOUR_ACCESS_TOKEN>"
```

**Notice the Response Headers:**
```http
HTTP/1.1 200 OK
ETag: W/"1"
Cache-Control: private, no-cache
```

Now, re-send the request with `If-None-Match: W/"1"`:
```bash
curl -i http://localhost:8080/api/v1/tasks/0192e5b0-3c4d-7000-8000-000000000002 \
  -H "Authorization: Bearer <YOUR_ACCESS_TOKEN>" \
  -H 'If-None-Match: W/"1"'
```

**Response:**
```http
HTTP/1.1 304 Not Modified
```
*Zero database queries were needed—the client receives an instantaneous 304 Not Modified.*

---

### 6. Update Task with Optimistic Concurrency (`If-Match`)
To update a task, you must supply the current ETag in the `If-Match` header to prevent overwriting someone else's changes:

```bash
curl -X PUT http://localhost:8080/api/v1/tasks/0192e5b0-3c4d-7000-8000-000000000002 \
  -H "Authorization: Bearer <YOUR_ACCESS_TOKEN>" \
  -H 'If-Match: W/"1"' \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Assemble Lunar Module & Test Thrusters",
    "description": "Thruster test completed successfully.",
    "priority": "Urgent",
    "labels": ["hardware", "critical", "tested"]
  }'
```

**Response (`200 OK`):**
```json
{
  "id": "0192e5b0-3c4d-7000-8000-000000000002",
  "title": "Assemble Lunar Module & Test Thrusters",
  "priority": "Urgent",
  "rowVersion": 2
}
```
*Notice that `rowVersion` is now incremented to `2`.*

---

### 7. Search Tasks Using PostgreSQL Full-Text Search
Query tasks matching full-text keywords using Google-like syntax:

```bash
curl "http://localhost:8080/api/v1/projects/0192e5b0-1a2b-7000-8000-000000000001/tasks?search=propulsion" \
  -H "Authorization: Bearer <YOUR_ACCESS_TOKEN>"
```

**Response:**
Returns the matching tasks instantly via the native PostgreSQL `tsvector` GIN index.

---

## Step 6: How to Run the Automated Test Suite

To run all 70 unit, architecture, and integration tests:

```bash
dotnet test
```

### What gets tested:
1. **`TaskTracker.ArchitectureTests` (10 tests):** Checks that no slice depends on another slice, no repositories exist, no mediator library is referenced, and handlers never touch `HttpContext`.
2. **`TaskTracker.UnitTests` (35 tests):** Tests the task status state machine, domain entities, Result monad, and FluentValidation rules in pure memory.
3. **`TaskTracker.IntegrationTests` (25 tests):** Automatically spins up real PostgreSQL 17 and Valkey 8.0 Docker containers using Testcontainers and tests:
   - Keyset cursor pagination and full-text search.
   - Valkey idempotency locks and replay logic.
   - Dual-instance cache invalidation across two distinct web factory instances.
   - OWASP BOLA security boundaries.
   - OpenAPI drift verification against `docs/openapi/tasktracker.v1.json`.

---

## Step 7: How to Run the Load & Performance Tool

To benchmark throughput and latency percentiles under concurrent load:

```bash
dotnet run --project tools/TaskTracker.LoadTool -- --url http://localhost:8080 --concurrency 5 --duration 10 --scenario mixed --pace-ms 20
```

### Options:
- `--url`: Target API URL (default: `http://localhost:8080`).
- `--concurrency`: Number of concurrent worker tasks (default: `5`).
- `--duration`: Benchmark duration in seconds (default: `10`).
- `--scenario`: Workload scenario:
  - `read`: 100% read operations (tasks and projects with ETags).
  - `write`: 100% task creation.
  - `search`: 100% full-text search and label filtering.
  - `mixed`: 60% Read, 20% Search, 20% Write.
- `--pace-ms`: Delay between requests per worker (default: `20` ms). Set to `0` to benchmark burst rate-limiting saturation.

---

## Step 8: Shutting Down

When you are done testing, tear down the Docker Compose stack and clean up storage volumes:

```bash
docker compose down -v
```
