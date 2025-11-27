#!/usr/bin/env bash
set -euo pipefail
STACK_FILE="$(dirname "$0")/../../Ci_Cd/docker-compose.integration.yml"
LOG_DIR="/tmp/cicd_integration_logs"
OUT_DIR="/tmp/cicd_integration_out"
mkdir -p "$LOG_DIR" "$OUT_DIR"

# bring up stack
if ! docker compose -f "$STACK_FILE" up -d; then
  echo "[ERR] docker compose up failed" >&2
  exit 1
fi

echo "[INFO] waiting services health..."
sleep 15

# nexus URL
NEXUS_URL="http://localhost:8081"
REGISTRY_URL="http://localhost:5000"

# run dotnet tests (integration)
pushd "$(dirname "$0")/../../"
  dotnet test tests/Ci_Cd.Tests/Ci_Cd.Tests.csproj --filter FullyQualifiedName~AdaptersIntegrationTests -v minimal || true
popd

# collect logs
docker compose -f "$STACK_FILE" logs --no-color > "$LOG_DIR/stack.log" || true

echo "[INFO] logs at: $LOG_DIR"; echo "[INFO] out at: $OUT_DIR"

