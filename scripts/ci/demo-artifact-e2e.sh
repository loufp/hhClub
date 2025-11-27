#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# Colors
GREEN='\033[0;32m'
YELLOW='\033[1;33m' 
RED='\033[0;31m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m'

cat << 'EOF'
╔══════════════════════════════════════════════════════════════════════════════╗
║                     🚀 ARTIFACT UPLOADER E2E DEMO 🚀                        ║
║                                                                              ║
║  Comprehensive demonstration of artifact uploader E2E testing capabilities   ║
║                                                                              ║
║  Services: Nexus • Artifactory • Docker Registry • GitHub Releases          ║
║  Features: Metadata • ETags • Status Codes • Performance • Monitoring       ║
╚══════════════════════════════════════════════════════════════════════════════╝
EOF

echo -e "\n${BLUE}Demo Configuration:${NC}"
echo "  Project Root: $PROJECT_ROOT"
echo "  Demo Mode: Interactive"
echo "  Services: All available services"
echo

# Function to show section header
show_section() {
    local title=$1
    echo -e "\n${CYAN}═══════════════════════════════════════════════════════════════════${NC}"
    echo -e "${CYAN} $title ${NC}"
    echo -e "${CYAN}═══════════════════════════════════════════════════════════════════${NC}"
}

# Function to wait for user input
wait_for_user() {
    local message=${1:-"Press Enter to continue..."}
    echo -e "\n${YELLOW}$message${NC}"
    read -r
}

# Function to run demo command with output
demo_command() {
    local description=$1
    local command=$2
    local show_output=${3:-true}
    
    echo -e "\n${BLUE}🔧 $description${NC}"
    echo -e "${YELLOW}Command: $command${NC}"
    
    if [ "$show_output" = "true" ]; then
        echo -e "${GREEN}Output:${NC}"
        eval "$command"
    else
        eval "$command" > /dev/null 2>&1
    fi
}

cd "$PROJECT_ROOT"

show_section "🏗️  INFRASTRUCTURE SETUP"
echo "This demo will set up a complete testing infrastructure with:"
echo "  • Nexus Repository Manager (Maven artifacts)"
echo "  • JFrog Artifactory (Universal repository)"
echo "  • Docker Registry (Container images)"
echo "  • SonarQube (Code quality - bonus)"
echo

wait_for_user "Ready to start services? (This may take 3-5 minutes)"

demo_command "Starting all integration services" \
    "./scripts/ci/start-integration-services.sh" false

echo -e "${GREEN}✅ All services are ready!${NC}"

show_section "🧪 BASIC E2E TESTS"
echo "Running core E2E tests for all artifact uploaders..."
echo "These tests validate:"
echo "  • Basic upload functionality"
echo "  • Metadata and ETag handling"  
echo "  • HTTP status codes (401, 404, etc.)"
echo "  • Authentication mechanisms"
echo

wait_for_user "Ready to run basic E2E tests?"

demo_command "Running Nexus integration tests" \
    "dotnet test tests/Ci_Cd.Tests/Ci_Cd.Tests.csproj --filter 'FullyQualifiedName~NexusIntegrationTests' --logger 'console;verbosity=normal'" false

echo -e "${GREEN}✅ Nexus tests completed!${NC}"

demo_command "Running Docker Registry E2E tests" \
    "dotnet test tests/Ci_Cd.Tests/Ci_Cd.Tests.csproj --filter 'FullyQualifiedName~DockerRegistryE2ETests' --logger 'console;verbosity=normal'" false

echo -e "${GREEN}✅ Docker Registry tests completed!${NC}"

show_section "🎯 STATUS CODE VALIDATION"
echo "Testing comprehensive HTTP status code handling..."
echo "This validates proper error handling and edge cases:"
echo "  • 401 Unauthorized (invalid credentials)"  
echo "  • 404 Not Found (missing resources)"
echo "  • Timeout handling"
echo "  • Concurrent upload scenarios"
echo

wait_for_user "Ready to run status code tests?"

demo_command "Running status code validation tests" \
    "dotnet test tests/Ci_Cd.Tests/Ci_Cd.Tests.csproj --filter 'FullyQualifiedName~ArtifactUploadersStatusCodeTests' --logger 'console;verbosity=normal'" false

echo -e "${GREEN}✅ Status code tests completed!${NC}"

show_section "📊 PERFORMANCE MONITORING"
echo "Measuring upload performance and reliability..."
echo "Metrics collected:"
echo "  • Upload duration per service"
echo "  • Success/failure rates"
echo "  • Concurrent upload handling" 
echo "  • Resource usage (CPU, memory)"
echo

wait_for_user "Ready to run performance monitoring?"

demo_command "Running performance analysis (3 iterations, 2 concurrent)" \
    "./scripts/ci/monitor-artifact-performance.sh 3 2" true

show_section "🌐 GITHUB RELEASES DEMO"
echo "GitHub Releases testing requires additional setup:"
echo "  • GITHUB_TOKEN environment variable"
echo "  • GITHUB_TEST_REPO repository access"
echo

if [[ -n "${GITHUB_TOKEN:-}" ]]; then
    echo -e "${GREEN}✅ GITHUB_TOKEN is configured${NC}"
    
    wait_for_user "Ready to test GitHub Releases API?"
    
    demo_command "Checking GitHub rate limit" \
        "curl -s -H 'Authorization: token $GITHUB_TOKEN' https://api.github.com/rate_limit | jq '.resources.core | {limit, remaining, reset}'" true
    
    demo_command "Running GitHub Releases E2E tests" \
        "./scripts/ci/test-github-e2e.sh" false
        
    echo -e "${GREEN}✅ GitHub Releases tests completed!${NC}"
else
    echo -e "${YELLOW}⚠️  GITHUB_TOKEN not configured - skipping GitHub tests${NC}"
    echo "To enable GitHub testing:"
    echo "  export GITHUB_TOKEN='your_github_token'"
    echo "  export GITHUB_TEST_REPO='owner/repository'"
fi

show_section "🔍 COMPREHENSIVE TEST SUITE"
echo "Running the complete E2E test suite..."

wait_for_user "Ready to run all tests together?"

demo_command "Running comprehensive E2E test suite" \
    "./scripts/ci/test-all-artifacts-e2e.sh" true

show_section "📈 SERVICE HEALTH DASHBOARD"
echo "Final service health check and resource usage:"

demo_command "Service health status" \
    "docker compose -f Ci_Cd/docker-compose.integration.yml ps" true

demo_command "Docker resource usage" \
    "docker stats --no-stream --format 'table {{.Container}}\t{{.CPUPerc}}\t{{.MemUsage}}\t{{.MemPerc}}'" true

demo_command "Test results summary" \
    "ls -la tests/Ci_Cd.Tests/TestResults/ 2>/dev/null || echo 'No test results found'" true

show_section "🎉 DEMO COMPLETE"
echo -e "${GREEN}Congratulations! You have successfully demonstrated:${NC}"
echo
echo "✅ Multi-service artifact upload testing"
echo "✅ Comprehensive HTTP status code validation"
echo "✅ Metadata and ETag handling"
echo "✅ Performance monitoring and analysis"
echo "✅ Real-world integration scenarios"
echo
echo -e "${BLUE}Key Features Demonstrated:${NC}"
echo "  • Nexus Repository Manager integration"
echo "  • Docker Registry V2 API compliance"
echo "  • Artifactory REST API testing"
echo "  • GitHub Releases API integration"
echo "  • Automated service orchestration"
echo "  • Performance benchmarking"
echo "  • Reliability monitoring"
echo
echo -e "${YELLOW}Next Steps:${NC}"
echo "  1. Review test results in tests/Ci_Cd.Tests/TestResults/"
echo "  2. Integrate tests into your CI/CD pipeline"
echo "  3. Customize for your specific artifact workflows"
echo "  4. Extend with additional artifact repositories"
echo
echo -e "${CYAN}Documentation:${NC}"
echo "  • Full documentation: docs/ARTIFACT_E2E_TESTS.md"
echo "  • Quick start guide: README_ARTIFACT_E2E.md"
echo "  • CI/CD examples included in documentation"

wait_for_user "Demo complete! Press Enter to cleanup services (or Ctrl+C to keep running)"

show_section "🧹 CLEANUP"
demo_command "Stopping all integration services" \
    "docker compose -f Ci_Cd/docker-compose.integration.yml down -v" true

echo -e "\n${GREEN}🎉 Thank you for exploring the Artifact Uploader E2E Testing Suite! 🎉${NC}"
echo -e "${BLUE}Visit our documentation for advanced configuration and CI/CD integration examples.${NC}\n"
