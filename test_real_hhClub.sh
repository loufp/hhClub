#!/bin/bash

echo "======================================"
echo "Testing CI/CD Analyzer - hhClub Repo"
echo "======================================"

# Clean up
pkill -9 dotnet 2>/dev/null
sleep 2

# Start server
echo "Starting server..."
cd /Users/kirillkirill13let/RiderProjects/Ci_Cd/Ci_Cd
dotnet run --urls "http://localhost:5034" > /tmp/cicd_test.log 2>&1 &
SERVER_PID=$!
echo "Server PID: $SERVER_PID"

# Wait for startup
echo "Waiting for server startup..."
for i in {1..15}; do
    if curl -s http://localhost:5034/swagger/index.html > /dev/null 2>&1; then
        echo "Server is ready!"
        break
    fi
    echo "Waiting... ($i/15)"
    sleep 1
done

# Run test
echo ""
echo "Testing repository: https://github.com/loufp/hhClub"
echo "---"

curl -s -X POST "http://localhost:5034/api/Pipeline/generate" \
  -H "Content-Type: application/json" \
  -d '{"repoUrl":"https://github.com/loufp/hhClub","execute":false}' \
  -w "\nHTTP Status: %{http_code}\n" \
  -o /Users/kirillkirill13let/RiderProjects/Ci_Cd/REAL_TEST_RESULT.json

echo ""
echo "==== REAL TEST RESULT ===="
cat /Users/kirillkirill13let/RiderProjects/Ci_Cd/REAL_TEST_RESULT.json
echo ""
echo "==== FORMATTED ===="
cat /Users/kirillkirill13let/RiderProjects/Ci_Cd/REAL_TEST_RESULT.json | python3 -m json.tool 2>/dev/null || echo "JSON formatting failed"

# Cleanup
echo ""
echo "Stopping server..."
kill $SERVER_PID 2>/dev/null
sleep 1
pkill -9 dotnet 2>/dev/null

echo ""
echo "Test complete. Result saved to: REAL_TEST_RESULT.json"

