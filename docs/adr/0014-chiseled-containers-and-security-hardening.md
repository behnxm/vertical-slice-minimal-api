# 14. Ubuntu Noble Chiseled Container Images and Security Hardening

Date: 2026-10-31
Status: Accepted

## Context

Standard container images typically include Linux shells (`/bin/sh`, `/bin/bash`), package managers (`apt`, `apk`), core utilities (`curl`, `wget`), and run as the `root` user (UID 0). In production environments, if a Remote Code Execution (RCE) or container breakout vulnerability occurs, the presence of shells, root permissions, and package managers allows attackers to pivot, install malicious payloads, and compromise host infrastructure.

## Decision

I chose **Microsoft's Ubuntu Noble Chiseled ASP.NET runtime image (`mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`)** with a multi-stage Dockerfile and strict runtime constraints.

1. **Distroless/Chiseled Architecture:** The final image contains *only* the .NET runtime, standard C libraries, and the compiled application binaries. There is no package manager, no shell (`/bin/sh`), and no common utilities.
2. **Non-Root Execution:** The container runs under the built-in unprivileged user `$APP_UID` (UID 1654).
3. **Disabled Diagnostics:** `DOTNET_EnableDiagnostics=0` disables the .NET diagnostic IPC pipe (`/tmp/dotnet-diagnostic-*`), preventing unauthorized memory dumps or profiling attacks.
4. **Self-Contained Healthcheck:** Because no curl or shell exists in chiseled images, container health checks invoke the API binary directly:
   ```dockerfile
   HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
     CMD ["dotnet", "TaskTracker.Api.dll", "--healthcheck"]
   ```

## Consequences

### Positive
- **Near-Zero CVE Attack Surface:** Eliminates 95%+ of vulnerabilities reported by container scanners (Trivy, Docker Scout, Clair).
- **Hardened Containment:** An attacker cannot invoke a shell, run scripts, or download secondary payloads inside the container.
- **Minimal Image Size:** Highly compressed, fast image pulls and startups.

### Negative
- Traditional debugging via `docker exec -it <container> sh` is impossible (must use `docker debug` or ephemeral debug containers).

### Alternatives Considered
- **Alpine Linux Images:** Alpine uses `musl` libc instead of `glibc`, which can occasionally cause subtle compatibility or performance issues with native libraries (e.g. Npgsql and OpenTelemetry). Chiseled Ubuntu provides `glibc` with zero bloat.
