#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
COMPOSE_FILE="$PROJECT_ROOT/Ci_Cd/docker-compose.integration.yml"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo -e "${BLUE}=== Enhanced E2E Tests for All Artifact Uploaders ===${NC}"
echo

# Function to check service health
check_service_health() {
    local service_name=$1
    local health_url=$2
    local expected_status=${3:-200}
    
    echo -n "Checking $service_name... "
    
    if curl -sf "$health_url" > /dev/null 2>&1; then
        echo -e "${GREEN}✓ Healthy${NC}"
        return 0
    else
        echo -e "${RED}✗ Not available${NC}"
        return 1
    fi
}

# Function to run test category
run_test_category() {
    local category=$1
    local filter=$2
    local timeout=${3:-600000}
    
    echo -e "\n${BLUE}Running $category Tests${NC}"
    echo "=================================================="
    
    cd "$PROJECT_ROOT"
    
    dotnet test tests/Ci_Cd.Tests/Ci_Cd.Tests.csproj \
        --filter "$filter" \
        --logger "console;verbosity=detailed" \
        --logger "trx;LogFileName=${category,,}_e2e_results.trx" \
        -- RunConfiguration.TestSessionTimeout=$timeout
    
    return $?
}

# Check if services are running
echo -e "${YELLOW}Checking Service Availability...${NC}"
NEXUS_OK=0
ARTIFACTORY_OK=0
REGISTRY_OK=0
SONAR_OK=0

check_service_health "Nexus" "http://localhost:8081" || NEXUS_OK=1
check_service_health "Artifactory" "http://localhost:8082/artifactory/api/system/ping" || ARTIFACTORY_OK=1
check_service_health "Docker Registry" "http://localhost:5000/v2/" || REGISTRY_OK=1
check_service_health "SonarQube" "http://localhost:9000/api/system/status" || SONAR_OK=1

SERVICES_DOWN=$((NEXUS_OK + ARTIFACTORY_OK + REGISTRY_OK))

if [ $SERVICES_DOWN -gt 0 ]; then
    echo -e "\n${YELLOW}Some services are not available. Starting integration services...${NC}"
    echo "This may take 3-5 minutes..."
    
    if ! "$SCRIPT_DIR/start-integration-services.sh"; then
        echo -e "${RED}Failed to start integration services${NC}"
        exit 1
    fi
    
    echo -e "${GREEN}✓ Integration services started successfully${NC}"
fi

# Check GitHub environment
echo -e "\n${YELLOW}Checking GitHub Environment...${NC}"
GITHUB_OK=0
if [[ -z "${GITHUB_TOKEN:-}" ]]; then
    echo -e "${YELLOW}⚠️  GITHUB_TOKEN not set - GitHub tests will be skipped${NC}"
    GITHUB_OK=1
else
    echo -e "${GREEN}✓ GITHUB_TOKEN is set${NC}"
    
    # Check rate limit
    echo -n "Checking GitHub rate limit... "
    RATE_LIMIT=$(curl -s -H "Authorization: token $GITHUB_TOKEN" https://api.github.com/rate_limit | jq -r '.resources.core.remaining // "unknown"')
    
    if [[ "$RATE_LIMIT" =~ ^[0-9]+$ ]] && [ "$RATE_LIMIT" -gt 10 ]; then
        echo -e "${GREEN}$RATE_LIMIT calls remaining${NC}"
    else
        echo -e "${RED}Rate limit low or invalid token${NC}"
        GITHUB_OK=1
    fi
fi

if [[ -z "${GITHUB_TEST_REPO:-}" ]]; then
    echo -e "${YELLOW}⚠️  GITHUB_TEST_REPO not set - using fallback${NC}"
else
    echo -e "${GREEN}✓ GITHUB_TEST_REPO: $GITHUB_TEST_REPO${NC}"
fi

# Test execution plan
echo -e "\n${BLUE}Test Execution Plan:${NC}"
echo "1. Nexus Integration Tests (against localhost:8081)"
echo "2. Artifactory E2E Tests (against localhost:8082)"  
echo "3. Docker Registry E2E Tests (against localhost:5000)"
if [ $GITHUB_OK -eq 0 ]; then
    echo "4. GitHub Releases E2E Tests (against api.github.com)"
else
    echo "4. GitHub Releases E2E Tests (SKIPPED - environment not configured)"
fi
echo

# Initialize results
NEXUS_RESULT=0
ARTIFACTORY_RESULT=0
REGISTRY_RESULT=0
GITHUB_RESULT=0
TOTAL_TESTS=0
PASSED_TESTS=0

# Run Nexus tests
if [ $NEXUS_OK -eq 0 ]; then
    if run_test_category "Nexus" "FullyQualifiedName~NexusIntegrationTests"; then
        echo -e "${GREEN}✅ Nexus tests - PASSED${NC}"
        PASSED_TESTS=$((PASSED_TESTS + 1))
    else
        echo -e "${RED}❌ Nexus tests - FAILED${NC}"
        NEXUS_RESULT=1
    fi
    TOTAL_TESTS=$((TOTAL_TESTS + 1))
else
    echo -e "${YELLOW}⏭️  Nexus tests - SKIPPED (service not available)${NC}"
fi

# Run Artifactory tests  
if [ $ARTIFACTORY_OK -eq 0 ]; then
    if run_test_category "Artifactory" "FullyQualifiedName~ArtifactoryE2ETests"; then
        echo -e "${GREEN}✅ Artifactory tests - PASSED${NC}"
        PASSED_TESTS=$((PASSED_TESTS + 1))
    else
        echo -e "${RED}❌ Artifactory tests - FAILED${NC}"
        ARTIFACTORY_RESULT=1
    fi
    TOTAL_TESTS=$((TOTAL_TESTS + 1))
else
    echo -e "${YELLOW}⏭️  Artifactory tests - SKIPPED (service not available)${NC}"
fi

# Run Registry tests
if [ $REGISTRY_OK -eq 0 ]; then
    if run_test_category "DockerRegistry" "FullyQualifiedName~DockerRegistryE2ETests"; then
        echo -e "${GREEN}✅ Docker Registry tests - PASSED${NC}"
        PASSED_TESTS=$((PASSED_TESTS + 1))
    else
        echo -e "${RED}❌ Docker Registry tests - FAILED${NC}"
        REGISTRY_RESULT=1
    fi
    TOTAL_TESTS=$((TOTAL_TESTS + 1))
else
    echo -e "${YELLOW}⏭️  Docker Registry tests - SKIPPED (service not available)${NC}"
fi

# Run GitHub tests
if [ $GITHUB_OK -eq 0 ]; then
    if run_test_category "GitHub" "FullyQualifiedName~GitHubReleasesE2ETests" 900000; then
        echo -e "${GREEN}✅ GitHub Releases tests - PASSED${NC}"
        PASSED_TESTS=$((PASSED_TESTS + 1))
    else
        echo -e "${RED}❌ GitHub Releases tests - FAILED${NC}"
        GITHUB_RESULT=1
    fi
    TOTAL_TESTS=$((TOTAL_TESTS + 1))
else
    echo -e "${YELLOW}⏭️  GitHub Releases tests - SKIPPED (environment not configured)${NC}"
fi

# Final results
echo -e "\n${BLUE}==================== FINAL RESULTS ====================${NC}"
echo -e "Total test categories: $TOTAL_TESTS"
echo -e "Passed: ${GREEN}$PASSED_TESTS${NC}"
echo -e "Failed: ${RED}$((TOTAL_TESTS - PASSED_TESTS))${NC}"

if [ $NEXUS_RESULT -eq 1 ]; then
    echo -e "${RED}❌ Nexus Integration Tests failed${NC}"
fi

if [ $ARTIFACTORY_RESULT -eq 1 ]; then
    echo -e "${RED}❌ Artifactory E2E Tests failed${NC}"
fi

if [ $REGISTRY_RESULT -eq 1 ]; then
    echo -e "${RED}❌ Docker Registry E2E Tests failed${NC}"
fi

if [ $GITHUB_RESULT -eq 1 ]; then
    echo -e "${RED}❌ GitHub Releases E2E Tests failed${NC}"
fi

echo
echo -e "${BLUE}Test Results Location:${NC}"
echo "  tests/Ci_Cd.Tests/TestResults/"
ls -la tests/Ci_Cd.Tests/TestResults/*.trx 2>/dev/null || echo "  (No result files found)"

echo
if [ $((NEXUS_RESULT + ARTIFACTORY_RESULT + REGISTRY_RESULT + GITHUB_RESULT)) -eq 0 ] && [ $TOTAL_TESTS -gt 0 ]; then
    echo -e "${GREEN}🎉 ALL TESTS PASSED! 🎉${NC}"
    echo -e "${GREEN}E2E artifact uploader validation complete.${NC}"
    exit 0
else
    echo -e "${RED}💥 SOME TESTS FAILED 💥${NC}"
    echo
    echo -e "${YELLOW}Common Troubleshooting:${NC}"
    echo "1. Service Issues:"
    echo "   docker compose -f $COMPOSE_FILE ps"
    echo "   docker compose -f $COMPOSE_FILE logs [service_name]"
    echo
    echo "2. Authentication:"
    echo "   - Nexus: admin/admin123 (may need first-time setup)"
    echo "   - Artifactory: admin/password"
    echo "   - GitHub: Check GITHUB_TOKEN scopes"
    echo
    echo "3. Network/Connectivity:"
    echo "   curl -v http://localhost:8081"
    echo "   curl -v http://localhost:8082/artifactory/api/system/ping" 
    echo "   curl -v http://localhost:5000/v2/"
    echo "   curl -v https://api.github.com/rate_limit"
    echo
    echo "4. Re-run specific category:"
    echo "   dotnet test --filter \"FullyQualifiedName~[TestCategory]\""
    
    exit 1
fi
