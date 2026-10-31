# Scaffold a new Vertical Slice for PowerShell
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Feature,

    [Parameter(Mandatory = $true, Position = 1)]
    [string]$UseCase
)

$ErrorActionPreference = "Stop"

$targetDir = "src/TaskTracker.Api/Features/$Feature"
$targetFile = Join-Path $targetDir "$UseCase.cs"
$templateFile = "docs/slice-template.cs.txt"

if (-not (Test-Path $templateFile)) {
    Write-Error "[Error] Slice template not found at $templateFile"
}

if (Test-Path $targetFile) {
    Write-Error "[Error] Slice file already exists at $targetFile"
}

if (-not (Test-Path $targetDir)) {
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
}

# Convert PascalCase to kebab-case
$routeSlug = ($UseCase -creplace '([a-z0-9])([A-Z])', '$1-$2').ToLowerInvariant()

$content = Get-Content $templateFile -Raw
$content = $content.Replace("__FEATURE_NAME__", $Feature)
$content = $content.Replace("__USE_CASE_NAME__", $UseCase)
$content = $content.Replace("__ROUTE_SLUG__", $routeSlug)

Set-Content -Path $targetFile -Value $content -Encoding utf8

Write-Host "[Success] Scaffolded new vertical slice at $targetFile" -ForegroundColor Green
Write-Host "Feature:   $Feature" -ForegroundColor Cyan
Write-Host "Use Case:  $UseCase" -ForegroundColor Cyan
Write-Host "Route:     /api/v1/projects/{projectId}/$routeSlug" -ForegroundColor Cyan
