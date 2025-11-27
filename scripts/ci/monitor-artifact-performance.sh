#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# Colors
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
BLUE='\033[0;34m'
NC='\033[0m'

echo -e "${BLUE}=== Artifact Uploader Performance & Reliability Monitor ===${NC}"
echo

# Configuration
ITERATIONS=${1:-5}
CONCURRENT_UPLOADS=${2:-3}
FILE_SIZES=(1024 10240 102400 1048576)  # 1KB, 10KB, 100KB, 1MB
SERVICES=("nexus:http://localhost:8081" "artifactory:http://localhost:8082/artifactory/api/system/ping" "registry:http://localhost:5000/v2/")

# Results tracking
declare -A upload_times
declare -A success_counts
declare -A error_counts

echo -e "${YELLOW}Configuration:${NC}"
echo "  Iterations per test: $ITERATIONS"
echo "  Concurrent uploads: $CONCURRENT_UPLOADS"
echo "  File sizes: ${FILE_SIZES[*]} bytes"
echo

# Function to create test file
create_test_file() {
    local size=$1
    local file_path="/tmp/perf_test_${size}b_$$.bin"
    
    # Create file with random content
    dd if=/dev/urandom of="$file_path" bs="$size" count=1 2>/dev/null
    echo "$file_path"
}

# Function to measure upload time
measure_upload_time() {
    local service=$1
    local file_path=$2
    local iteration=$3
    
    local start_time=$(date +%s.%N)
    
    case "$service" in
        "nexus")
            timeout 60s dotnet test tests/Ci_Cd.Tests/Ci_Cd.Tests.csproj \
                --filter "FullyQualifiedName~NexusIntegrationTests.Nexus_Upload_WithMetadataAndETag" \
                --logger "console;verbosity=quiet" > /dev/null 2>&1
            local exit_code=$?
            ;;
        "artifactory")
            timeout 60s dotnet test tests/Ci_Cd.Tests/Ci_Cd.Tests.csproj \
                --filter "FullyQualifiedName~ArtifactoryE2ETests.Artifactory_Upload_WithMetadataAndETag" \
                --logger "console;verbosity=quiet" > /dev/null 2>&1
            local exit_code=$?
            ;;
        "registry")
            timeout 60s dotnet test tests/Ci_Cd.Tests/Ci_Cd.Tests.csproj \
                --filter "FullyQualifiedName~DockerRegistryE2ETests.Registry_FullPushFlow_WithManifestVerification" \
                --logger "console;verbosity=quiet" > /dev/null 2>&1
            local exit_code=$?
            ;;
        *)
            echo "Unknown service: $service"
            return 1
            ;;
    esac
    
    local end_time=$(date +%s.%N)
    local duration=$(echo "$end_time - $start_time" | bc -l)
    
    if [ $exit_code -eq 0 ]; then
        success_counts["$service"]=$((${success_counts["$service"]:-0} + 1))
        echo "$duration"
    else
        error_counts["$service"]=$((${error_counts["$service"]:-0} + 1))
        echo "ERROR"
    fi
}

# Function to check service availability
check_service() {
    local service_info=$1
    local name=$(echo "$service_info" | cut -d: -f1)
    local url=$(echo "$service_info" | cut -d: -f2-)
    
    echo -n "Checking $name... "
    if curl -sf "$url" > /dev/null 2>&1; then
        echo -e "${GREEN}✓${NC}"
        return 0
    else
        echo -e "${RED}✗${NC}"
        return 1
    fi
}

# Check service availability
echo -e "${YELLOW}Service Health Check:${NC}"
available_services=()
for service_info in "${SERVICES[@]}"; do
    if check_service "$service_info"; then
        service_name=$(echo "$service_info" | cut -d: -f1)
        available_services+=("$service_name")
    fi
done

if [ ${#available_services[@]} -eq 0 ]; then
    echo -e "${RED}No services available. Please start integration services first.${NC}"
    echo "./scripts/ci/start-integration-services.sh"
    exit 1
fi

echo -e "\nAvailable services: ${available_services[*]}"

# Performance testing
echo -e "\n${BLUE}Starting Performance Tests...${NC}"

cd "$PROJECT_ROOT"

for service in "${available_services[@]}"; do
    echo -e "\n${YELLOW}Testing $service${NC}"
    echo "=============================================="
    
    # Initialize counters
    success_counts["$service"]=0
    error_counts["$service"]=0
    upload_times["$service"]=""
    
    # Run iterations
    total_time=0
    successful_uploads=0
    
    for ((i=1; i<=ITERATIONS; i++)); do
        echo -n "  Iteration $i/$ITERATIONS... "
        
        result=$(measure_upload_time "$service" "" "$i")
        
        if [ "$result" != "ERROR" ]; then
            echo -e "${GREEN}✓ ${result}s${NC}"
            total_time=$(echo "$total_time + $result" | bc -l)
            successful_uploads=$((successful_uploads + 1))
        else
            echo -e "${RED}✗ Failed${NC}"
        fi
        
        # Small delay between tests
        sleep 1
    done
    
    # Calculate statistics
    if [ $successful_uploads -gt 0 ]; then
        avg_time=$(echo "scale=3; $total_time / $successful_uploads" | bc -l)
        success_rate=$(echo "scale=1; $successful_uploads * 100 / $ITERATIONS" | bc -l)
        
        echo "  Results:"
        echo "    Success Rate: ${success_rate}%"
        echo "    Average Time: ${avg_time}s"
        echo "    Total Time:   ${total_time}s"
    else
        echo -e "  ${RED}All uploads failed${NC}"
    fi
done

# Concurrent upload test
if [ ${#available_services[@]} -gt 0 ]; then
    echo -e "\n${BLUE}Concurrent Upload Test${NC}"
    echo "=============================================="
    
    service="${available_services[0]}"  # Use first available service
    echo "Testing concurrent uploads to $service..."
    
    # Create test files for concurrent upload
    test_files=()
    for ((i=1; i<=CONCURRENT_UPLOADS; i++)); do
        test_file=$(create_test_file 10240)  # 10KB files
        test_files+=("$test_file")
    done
    
    # Start concurrent uploads
    start_time=$(date +%s.%N)
    pids=()
    
    for test_file in "${test_files[@]}"; do
        (
            case "$service" in
                "nexus")
                    dotnet test tests/Ci_Cd.Tests/Ci_Cd.Tests.csproj \
                        --filter "FullyQualifiedName~NexusIntegrationTests.Nexus_Upload_WithMetadataAndETag" \
                        --logger "console;verbosity=quiet" > /dev/null 2>&1
                    ;;
                "artifactory")
                    dotnet test tests/Ci_Cd.Tests/Ci_Cd.Tests.csproj \
                        --filter "FullyQualifiedName~ArtifactoryE2ETests.Artifactory_Upload_WithMetadataAndETag" \
                        --logger "console;verbosity=quiet" > /dev/null 2>&1
                    ;;
                "registry")
                    dotnet test tests/Ci_Cd.Tests/Ci_Cd.Tests.csproj \
                        --filter "FullyQualifiedName~DockerRegistryE2ETests.Registry_FullPushFlow_WithManifestVerification" \
                        --logger "console;verbosity=quiet" > /dev/null 2>&1
                    ;;
            esac
        ) &
        pids+=($!)
    done
    
    # Wait for all uploads to complete
    concurrent_success=0
    for pid in "${pids[@]}"; do
        if wait "$pid"; then
            concurrent_success=$((concurrent_success + 1))
        fi
    done
    
    end_time=$(date +%s.%N)
    concurrent_duration=$(echo "$end_time - $start_time" | bc -l)
    concurrent_rate=$(echo "scale=1; $concurrent_success * 100 / $CONCURRENT_UPLOADS" | bc -l)
    
    echo "  Concurrent Results:"
    echo "    Successful: $concurrent_success/$CONCURRENT_UPLOADS"
    echo "    Success Rate: ${concurrent_rate}%"
    echo "    Total Duration: ${concurrent_duration}s"
    
    # Cleanup test files
    for test_file in "${test_files[@]}"; do
        rm -f "$test_file"
    done
fi

# Resource usage check
echo -e "\n${BLUE}Resource Usage Check${NC}"
echo "=============================================="

# Check Docker container resource usage
if command -v docker &> /dev/null; then
    echo "Docker Container Stats:"
    docker stats --no-stream --format "table {{.Container}}\t{{.CPUPerc}}\t{{.MemUsage}}\t{{.MemPerc}}" 2>/dev/null | head -10
fi

# Check disk usage
echo -e "\nDisk Usage:"
df -h /tmp | tail -1

# Check memory
if command -v free &> /dev/null; then
    echo -e "\nMemory Usage:"
    free -h | head -2
elif [[ "$OSTYPE" == "darwin"* ]]; then
    echo -e "\nMemory Usage:"
    vm_stat | grep -E 'Pages (free|active|inactive|speculative|wired)'
fi

# Summary report
echo -e "\n${BLUE}==================== SUMMARY REPORT ====================${NC}"

total_tests=0
total_successes=0

for service in "${available_services[@]}"; do
    successes=${success_counts["$service"]:-0}
    errors=${error_counts["$service"]:-0}
    total=$((successes + errors))
    
    total_tests=$((total_tests + total))
    total_successes=$((total_successes + successes))
    
    if [ $total -gt 0 ]; then
        rate=$(echo "scale=1; $successes * 100 / $total" | bc -l)
        echo -e "$service: ${GREEN}$successes${NC}/$total (${rate}%)"
    else
        echo -e "$service: No tests run"
    fi
done

if [ $total_tests -gt 0 ]; then
    overall_rate=$(echo "scale=1; $total_successes * 100 / $total_tests" | bc -l)
    echo -e "\nOverall Success Rate: ${GREEN}${overall_rate}%${NC}"
    
    if (( $(echo "$overall_rate >= 90" | bc -l) )); then
        echo -e "${GREEN}🎉 Excellent reliability!${NC}"
    elif (( $(echo "$overall_rate >= 75" | bc -l) )); then
        echo -e "${YELLOW}⚠️  Good reliability, some issues detected${NC}"
    else
        echo -e "${RED}💥 Poor reliability, investigation needed${NC}"
    fi
else
    echo -e "${RED}No tests completed successfully${NC}"
fi

echo
echo "Performance data saved to: /tmp/artifact_uploader_perf_$(date +%Y%m%d_%H%M%S).log"
