#!/bin/bash
set -e

echo "=== Testing FIXED Analyzer ==="
echo ""

pkill -9 dotnet 2>/dev/null || true
sleep 2

cd /Users/kirillkirill13let/RiderProjects/Ci_Cd/Ci_Cd

echo "Starting server..."
dotnet run --urls "http://localhost:5034" > /tmp/test_server.log 2>&1 &
SERVER_PID=$!
echo "Server PID: $SERVER_PID"

echo "Waiting for server to start..."
sleep 15

echo ""
echo "=== Test 1: golang/go (should detect Go, not Node.js) ==="
curl -s -X POST "http://localhost:5034/api/Pipeline/generate" \
  -H "Content-Type: application/json" \
  -d '{"repoUrl":"https://github.com/golang/go","execute":false}' \
  > /tmp/test_golang.json

echo "Language detected: $(cat /tmp/test_golang.json | python3 -c 'import sys,json; print(json.load(sys.stdin)["analysis"]["language"])')"
echo "Framework detected: $(cat /tmp/test_golang.json | python3 -c 'import sys,json; print(json.load(sys.stdin)["analysis"]["framework"])')"

echo ""
echo "=== Test 2: hhClub (should detect DotNet) ==="
curl -s -X POST "http://localhost:5034/api/Pipeline/generate" \
  -H "Content-Type: application/json" \
  -d '{"repoUrl":"https://github.com/loufp/hhClub","execute":false}' \
  > /tmp/test_hhclub.json

echo "Language detected: $(cat /tmp/test_hhclub.json | python3 -c 'import sys,json; print(json.load(sys.stdin)["analysis"]["language"])')"
echo "Framework detected: $(cat /tmp/test_hhclub.json | python3 -c 'import sys,json; print(json.load(sys.stdin)["analysis"]["framework"])')"

echo ""
echo "=== Full golang/go result ==="
cat /tmp/test_golang.json | python3 -m json.tool

echo ""
kill $SERVER_PID 2>/dev/null || true
pkill -9 dotnet 2>/dev/null || true

echo ""
echo "=== Test Complete ==="

