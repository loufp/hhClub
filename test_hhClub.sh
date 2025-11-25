#!/bin/bash

echo "Starting CI/CD Pipeline Test for hhClub repository"
echo "=================================================="

# Kill any existing dotnet processes
pkill -9 dotnet 2>/dev/null
sleep 2

# Start the server
cd /Users/kirillkirill13let/RiderProjects/Ci_Cd/Ci_Cd
echo "Starting server..."
dotnet run --urls "http://localhost:5034" > /tmp/cicd_server.log 2>&1 &
SERVER_PID=$!
echo "Server PID: $SERVER_PID"

# Wait for server to start
echo "Waiting for server to initialize..."
sleep 8

# Test the endpoint
echo "Sending test request for https://github.com/loufp/hhClub..."
curl -s -X POST "http://localhost:5034/api/Pipeline/generate" \
  -H "Content-Type: application/json" \
  -d '{"repoUrl":"https://github.com/loufp/hhClub","execute":false}' \
  > /Users/kirillkirill13let/RiderProjects/Ci_Cd/test_result_hhClub.json

echo ""
echo "Test completed. Results saved to: test_result_hhClub.json"
echo ""
echo "=== Test Results ==="
cat /Users/kirillkirill13let/RiderProjects/Ci_Cd/test_result_hhClub.json | python3 -m json.tool 2>/dev/null || cat /Users/kirillkirill13let/RiderProjects/Ci_Cd/test_result_hhClub.json

# Clean up
echo ""
echo "Stopping server..."
kill $SERVER_PID 2>/dev/null
pkill -9 dotnet 2>/dev/null

echo "Test complete!"

