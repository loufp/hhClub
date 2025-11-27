#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

echo "=== Artifact Uploaders E2E Tests ==="
echo

# Start services
echo "[1/3] Starting integration services..."
"$SCRIPT_DIR/start-integration-services.sh"

echo
echo "[2/3] Running E2E tests..."
cd "$PROJECT_ROOT"

# Run all artifact integration tests
dotnet test tests/Ci_Cd.Tests/Ci_Cd.Tests.csproj \
    --filter "FullyQualifiedName~NexusIntegrationTests|FullyQualifiedName~DockerRegistryE2ETests|FullyQualifiedName~ArtifactoryE2ETests" \
    --logger "console;verbosity=detailed" \
    -- RunConfiguration.TestSessionTimeout=600000

TEST_RESULT=$?

echo
echo "[3/3] Test Results"
echo "===================="

if [ $TEST_RESULT -eq 0 ]; then
    echo "✅ All artifact e2e tests PASSED"
else
    echo "❌ Some tests FAILED (exit code: $TEST_RESULT)"
    echo
    echo "Troubleshooting:"
    echo "1. Check services are running:"
    echo "   docker compose -f Ci_Cd/docker-compose.integration.yml ps"
    echo
    echo "2. Check Nexus logs:"
    echo "   docker compose -f Ci_Cd/docker-compose.integration.yml logs nexus"
    echo
    echo "3. Check Artifactory logs:"
    echo "   docker compose -f Ci_Cd/docker-compose.integration.yml logs artifactory"
    echo
    echo "4. Check Registry logs:"
    echo "   docker compose -f Ci_Cd/docker-compose.integration.yml logs registry"
    echo
    echo "5. Nexus first-time setup:"
    echo "   Open http://localhost:8081 and complete setup wizard"
    echo "   Set admin password to 'admin123'"
fi

echo
echo "Service URLs:"
echo "  Nexus:       http://localhost:8081 (admin/admin123)"
echo "  Artifactory: http://localhost:8082 (admin/password)"
echo "  Registry:    http://localhost:5000"
echo
echo "To stop services: docker compose -f Ci_Cd/docker-compose.integration.yml down"

exit $TEST_RESULT

