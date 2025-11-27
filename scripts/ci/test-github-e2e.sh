#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

echo "=== E2E Tests for GitHub Releases Uploader ==="
echo

# Check if GitHub token is set
if [[ -z "${GITHUB_TOKEN:-}" ]]; then
    echo "⚠️  GITHUB_TOKEN environment variable not set"
    echo "   GitHub Releases tests will be skipped"
    echo
    echo "   To run GitHub tests:"
    echo "   export GITHUB_TOKEN=\"your_github_token\""
    echo "   export GITHUB_TEST_REPO=\"owner/repository\""
    echo
else
    echo "✓ GITHUB_TOKEN is set"
fi

# Check test repository
if [[ -z "${GITHUB_TEST_REPO:-}" ]]; then
    echo "⚠️  GITHUB_TEST_REPO environment variable not set"
    echo "   Using default test repository (tests may fail)"
    echo
else
    echo "✓ GITHUB_TEST_REPO is set: $GITHUB_TEST_REPO"
fi

echo
echo "GitHub API Rate Limit Check:"
if [[ -n "${GITHUB_TOKEN:-}" ]]; then
    curl -s -H "Authorization: token $GITHUB_TOKEN" https://api.github.com/rate_limit | jq '.resources.core | {limit, remaining, reset}'
else
    curl -s https://api.github.com/rate_limit | jq '.resources.core | {limit, remaining, reset}'
fi

echo
echo "Running GitHub Releases E2E Tests..."
echo "========================================="

cd "$PROJECT_ROOT"

# Run GitHub Releases tests
dotnet test tests/Ci_Cd.Tests/Ci_Cd.Tests.csproj \
    --filter "FullyQualifiedName~GitHubReleasesE2ETests" \
    --logger "console;verbosity=detailed" \
    --logger "trx;LogFileName=github_e2e_results.trx" \
    -- RunConfiguration.TestSessionTimeout=900000

TEST_EXIT_CODE=$?

echo
if [ $TEST_EXIT_CODE -eq 0 ]; then
    echo "✅ GitHub Releases E2E Tests - PASSED"
else
    echo "❌ GitHub Releases E2E Tests - FAILED"
    echo
    echo "Common Issues:"
    echo "1. Rate limit exceeded - wait or use different token"
    echo "2. Invalid GITHUB_TOKEN - check token permissions"
    echo "3. Repository access denied - verify GITHUB_TEST_REPO permissions"
    echo "4. Network connectivity issues"
    echo
    echo "Debugging:"
    echo "  Check rate limit: curl -H \"Authorization: token \$GITHUB_TOKEN\" https://api.github.com/rate_limit"
    echo "  Test token:       curl -H \"Authorization: token \$GITHUB_TOKEN\" https://api.github.com/user"
    echo "  Test repo access: curl -H \"Authorization: token \$GITHUB_TOKEN\" https://api.github.com/repos/\$GITHUB_TEST_REPO"
fi

echo
echo "Test results saved to: tests/Ci_Cd.Tests/TestResults/github_e2e_results.trx"

exit $TEST_EXIT_CODE
