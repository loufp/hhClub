#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
COMPOSE_FILE="$PROJECT_ROOT/Ci_Cd/docker-compose.integration.yml"
SONAR_URL="http://localhost:9000"

echo "=== SonarQube Integration Test ==="
echo "Project root: $PROJECT_ROOT"

# Start SonarQube
echo "[1/5] Starting SonarQube..."
docker compose -f "$COMPOSE_FILE" up -d sonarqube

# Wait for SonarQube to be ready
echo "[2/5] Waiting for SonarQube to be ready..."
MAX_WAIT=120
ELAPSED=0
while [ $ELAPSED -lt $MAX_WAIT ]; do
    if curl -sf "$SONAR_URL/api/system/status" > /dev/null 2>&1; then
        STATUS=$(curl -s "$SONAR_URL/api/system/status" | jq -r '.status')
        if [ "$STATUS" = "UP" ]; then
            echo "✓ SonarQube is ready!"
            break
        fi
    fi
    sleep 5
    ELAPSED=$((ELAPSED + 5))
    echo "  Waiting... ($ELAPSED/$MAX_WAIT seconds)"
done

if [ $ELAPSED -ge $MAX_WAIT ]; then
    echo "✗ Timeout waiting for SonarQube"
    docker compose -f "$COMPOSE_FILE" logs sonarqube | tail -50
    exit 1
fi

# Change default admin password (required for new installations)
echo "[3/5] Configuring SonarQube..."
curl -s -u admin:admin -X POST "$SONAR_URL/api/users/change_password?login=admin&previousPassword=admin&password=admin123" > /dev/null 2>&1 || true

# Create test project
echo "[4/5] Creating test project..."
PROJECT_KEY="ci-cd-test-$(date +%s)"
curl -s -u admin:admin123 -X POST "$SONAR_URL/api/projects/create?project=$PROJECT_KEY&name=CI-CD%20Test%20Project" || true

# Generate token
echo "[5/5] Generating analysis token..."
TOKEN_RESPONSE=$(curl -s -u admin:admin123 -X POST "$SONAR_URL/api/user_tokens/generate?name=test-token-$(date +%s)")
TOKEN=$(echo "$TOKEN_RESPONSE" | jq -r '.token' 2>/dev/null || echo "")

if [ -n "$TOKEN" ] && [ "$TOKEN" != "null" ]; then
    echo "✓ Token generated: ${TOKEN:0:10}..."
    
    # Export for tests
    export SONAR_TOKEN="$TOKEN"
    export SONAR_HOST_URL="$SONAR_URL"
    
    echo
    echo "SonarQube is ready for testing!"
    echo "URL: $SONAR_URL"
    echo "Username: admin"
    echo "Password: admin123"
    echo "Token: ${TOKEN:0:20}..."
    echo
else
    echo "⚠ Using default admin credentials"
    export SONAR_TOKEN="admin"
    export SONAR_HOST_URL="$SONAR_URL"
fi

# Run integration tests
echo "=== Running integration tests ==="
cd "$PROJECT_ROOT"
dotnet test tests/Ci_Cd.Tests/Ci_Cd.Tests.csproj \
    --filter "FullyQualifiedName~SonarQubeIntegrationTests" \
    -v normal

echo
echo "=== Integration tests completed ==="
echo "SonarQube Dashboard: $SONAR_URL"
echo
echo "To stop SonarQube: docker compose -f $COMPOSE_FILE down"

