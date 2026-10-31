#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -lt 2 ]; then
    echo "Usage: ./scripts/new-slice.sh <FeatureName> <UseCaseName>"
    echo "Example: ./scripts/new-slice.sh Tasks ExportTasks"
    exit 1
fi

FEATURE="$1"
USE_CASE="$2"
TARGET_DIR="src/TaskTracker.Api/Features/${FEATURE}"
TARGET_FILE="${TARGET_DIR}/${USE_CASE}.cs"
TEMPLATE_FILE="docs/slice-template.cs.txt"

if [ ! -f "${TEMPLATE_FILE}" ]; then
    echo >&2 "[Error] Slice template not found at ${TEMPLATE_FILE}"
    exit 1
fi

if [ -f "${TARGET_FILE}" ]; then
    echo >&2 "[Error] Slice file already exists at ${TARGET_FILE}"
    exit 1
fi

mkdir -p "${TARGET_DIR}"

# Convert PascalCase UseCaseName to kebab-case route slug
ROUTE_SLUG=$(echo "${USE_CASE}" | sed -r 's/([a-z0-9])([A-Z])/\1-\2/g' | tr '[:upper:]' '[:lower:]')

sed -e "s/__FEATURE_NAME__/${FEATURE}/g" \
    -e "s/__USE_CASE_NAME__/${USE_CASE}/g" \
    -e "s/__ROUTE_SLUG__/${ROUTE_SLUG}/g" \
    "${TEMPLATE_FILE}" > "${TARGET_FILE}"

echo "[Success] Scaffolded new vertical slice at ${TARGET_FILE}"
echo "Feature:   ${FEATURE}"
echo "Use Case:  ${USE_CASE}"
echo "Route:     /api/v1/projects/{projectId}/${ROUTE_SLUG}"
