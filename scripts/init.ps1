# TaskTracker API - Local Development Initialization for PowerShell
$ErrorActionPreference = "Stop"

Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host " TaskTracker API - Local Development Initialization" -ForegroundColor Cyan
Write-Host "================================================================================" -ForegroundColor Cyan

# 1. Environment file setup
if (-not (Test-Path ".env")) {
    Write-Host "[Init] Creating .env from .env.example..." -ForegroundColor Green
    Copy-Item ".env.example" ".env"
} else {
    Write-Host "[Init] .env already exists. Preserving existing configuration." -ForegroundColor Yellow
}

# 2. Check prerequisites
Write-Host "[Init] Checking tooling prerequisites..." -ForegroundColor Green
if (-not (Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
    Write-Error "[Error] .NET CLI is required but not installed."
}

$dotnetVersion = dotnet --version
Write-Host "[Init] Found .NET SDK version: $dotnetVersion" -ForegroundColor Green

# 3. Create dev-keys directory
$keyDir = "src/TaskTracker.Api/dev-keys"
if (-not (Test-Path $keyDir)) {
    New-Item -ItemType Directory -Path $keyDir -Force | Out-Null
}

$keyFile = Join-Path $keyDir "dev-key.pem"
if (-not (Test-Path $keyFile)) {
    if (Get-Command "openssl" -ErrorAction SilentlyContinue) {
        Write-Host "[Init] Generating local ECDSA P-256 private key for development token issuer..." -ForegroundColor Green
        openssl ecparam -name prime256v1 -genkey -noout -out $keyFile
        Write-Host "[Init] Generated $keyFile" -ForegroundColor Green
    } else {
        Write-Host "[Init] OpenSSL not found. The API will generate its own ECDSA P-256 key automatically on first run." -ForegroundColor Yellow
    }
}

# 4. Restore dependencies
Write-Host "[Init] Restoring solution dependencies..." -ForegroundColor Green
dotnet restore

Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host " Initialization complete! You can now run:" -ForegroundColor Cyan
Write-Host "   docker compose up -d              # Launch PostgreSQL and Valkey" -ForegroundColor White
Write-Host "   dotnet run --project src/TaskTracker.Api" -ForegroundColor White
Write-Host "================================================================================" -ForegroundColor Cyan
