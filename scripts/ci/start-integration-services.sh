#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
COMPOSE_FILE="$PROJECT_ROOT/Ci_Cd/docker-compose.integration.yml"

NEXUS_URL="http://localhost:8081"
REGISTRY_URL="http://localhost:5000"
ARTIFACTORY_URL="http://localhost:8082"
SONAR_URL="http://localhost:9000"

echo "=== Starting All Integration Services ==="

# Start all services
echo "[1/5] Starting services..."
docker compose -f "$COMPOSE_FILE" up -d

# Wait for Nexus
echo "[2/5] Waiting for Nexus (this may take 2-3 minutes)..."
MAX_WAIT=180
ELAPSED=0
while [ $ELAPSED -lt $MAX_WAIT ]; do
    if curl -sf "$NEXUS_URL" > /dev/null 2>&1; then
        echo "✓ Nexus is ready!"
        break
    fi
    sleep 10
    ELAPSED=$((ELAPSED + 10))
    echo "  Waiting for Nexus... ($ELAPSED/$MAX_WAIT seconds)"
done

if [ $ELAPSED -ge $MAX_WAIT ]; then
    echo "✗ Timeout waiting for Nexus"
    docker compose -f "$COMPOSE_FILE" logs nexus | tail -50
    exit 1
fi

# Wait for Registry
echo "[3/5] Waiting for Docker Registry..."
MAX_WAIT=60
ELAPSED=0
while [ $ELAPSED -lt $MAX_WAIT ]; do
    if curl -sf "$REGISTRY_URL/v2/" > /dev/null 2>&1; then
        echo "✓ Registry is ready!"
        break
    fi
    sleep 5
    ELAPSED=$((ELAPSED + 5))
    echo "  Waiting for Registry... ($ELAPSED/$MAX_WAIT seconds)"
done

if [ $ELAPSED -ge $MAX_WAIT ]; then
    echo "✗ Timeout waiting for Registry"
    exit 1
fi

# Wait for Artifactory
echo "[4/5] Waiting for Artifactory (this may take 2-3 minutes)..."
MAX_WAIT=180
ELAPSED=0
while [ $ELAPSED -lt $MAX_WAIT ]; do
    if curl -sf "$ARTIFACTORY_URL/artifactory/api/system/ping" > /dev/null 2>&1; then
        echo "✓ Artifactory is ready!"
        break
    fi
    sleep 10
    ELAPSED=$((ELAPSED + 10))
    echo "  Waiting for Artifactory... ($ELAPSED/$MAX_WAIT seconds)"
done

if [ $ELAPSED -ge $MAX_WAIT ]; then
    echo "⚠ Artifactory not ready, but continuing..."
fi

# Wait for SonarQube
echo "[5/5] Waiting for SonarQube..."
MAX_WAIT=120
ELAPSED=0
while [ $ELAPSED -lt $MAX_WAIT ]; do
    if curl -sf "$SONAR_URL/api/system/status" > /dev/null 2>&1; then
        STATUS=$(curl -s "$SONAR_URL/api/system/status" | jq -r '.status' 2>/dev/null || echo "")
        if [ "$STATUS" = "UP" ]; then
            echo "✓ SonarQube is ready!"
            break
        fi
    fi
    sleep 10
    ELAPSED=$((ELAPSED + 10))
    echo "  Waiting for SonarQube... ($ELAPSED/$MAX_WAIT seconds)"
done

if [ $ELAPSED -ge $MAX_WAIT ]; then
    echo "⚠ SonarQube not ready, but continuing..."
fi

echo
echo "=== All Services Ready ==="
echo "Nexus:       $NEXUS_URL"
echo "Registry:    $REGISTRY_URL"
echo "Artifactory: $ARTIFACTORY_URL"
echo "SonarQube:   $SONAR_URL"
echo
echo "Default Nexus credentials: admin / admin123"
echo "(Note: First-time setup may require password change via UI)"
echo
echo "To run integration tests:"
echo "  cd $PROJECT_ROOT"
echo "  dotnet test tests/Ci_Cd.Tests/Ci_Cd.Tests.csproj --filter \"Category=Integration\""
echo
echo "To stop all services:"
echo "  docker compose -f $COMPOSE_FILE down"

