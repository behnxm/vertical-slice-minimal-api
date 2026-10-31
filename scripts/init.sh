#!/usr/bin/env bash
set -euo pipefail

echo "================================================================================"
echo " TaskTracker API - Local Development Initialization"
echo "================================================================================"

# 1. Environment file setup
if [ ! -f ".env" ]; then
    echo "[Init] Creating .env from .env.example..."
    cp .env.example .env
else
    echo "[Init] .env already exists. Preserving existing configuration."
fi

# 2. Prerequisites verification
echo "[Init] Checking tooling prerequisites..."
command -v dotnet >/dev/null 2>&1 || { echo >&2 "[Error] 'dotnet' CLI is required but not installed."; exit 1; }
command -v docker >/dev/null 2>&1 || { echo >&2 "[Warning] 'docker' is not running or not in PATH. Testcontainers and compose require Docker."; }

DOTNET_VERSION=$(dotnet --version)
echo "[Init] Found .NET SDK version: ${DOTNET_VERSION}"

# 3. Create development keys directory
KEY_DIR="src/TaskTracker.Api/dev-keys"
mkdir -p "${KEY_DIR}"

if [ ! -f "${KEY_DIR}/dev-key.pem" ]; then
    if command -v openssl >/dev/null 2>&1; then
        echo "[Init] Generating local ECDSA P-256 private key for development token issuer..."
        openssl ecparam -name prime256v1 -genkey -noout -out "${KEY_DIR}/dev-key.pem"
        chmod 600 "${KEY_DIR}/dev-key.pem"
        echo "[Init] Generated ${KEY_DIR}/dev-key.pem"
    else
        echo "[Init] OpenSSL not found. The API will generate its own ECDSA P-256 key automatically on first run."
    fi
fi

# 4. Restore NuGet packages
echo "[Init] Restoring solution dependencies..."
dotnet restore

echo "================================================================================"
echo " Initialization complete! You can now run:"
echo "   docker compose up -d              # Launch PostgreSQL and Valkey"
echo "   dotnet run --project src/TaskTracker.Api"
echo "================================================================================"
