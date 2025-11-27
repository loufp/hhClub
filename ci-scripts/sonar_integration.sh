#!/usr/bin/env bash
set -euo pipefail

# Дополнительный скрипт для настройки интеграции SonarQube с GitLab
# Автоматически создает SonarQube токен и настраивает webhook

SONAR_URL=${SONAR_URL:-http://localhost:9000}
SONAR_USER=${SONAR_USER:-admin}  
SONAR_PASS=${SONAR_PASS:-admin}
GITLAB_URL=${GITLAB_URL:-http://localhost:9080}

echo "🔧 Setting up SonarQube → GitLab integration..."

# Wait for SonarQube to be ready
echo "⏳ Waiting for SonarQube..."
until curl -sSf "$SONAR_URL/api/system/status" | grep -q "UP" 2>/dev/null; do
  sleep 5
  echo -n '.'
done
echo -e "\n✅ SonarQube is ready"

# Create SonarQube user token
echo "🔑 Creating SonarQube user token..."
SONAR_TOKEN_RESPONSE=$(curl -s -u "$SONAR_USER:$SONAR_PASS" -X POST \
  "$SONAR_URL/api/user_tokens/generate" \
  -d "name=gitlab-ci-token" || echo "{}")

SONAR_TOKEN=$(echo "$SONAR_TOKEN_RESPONSE" | jq -r '.token // empty' 2>/dev/null)

if [ -n "$SONAR_TOKEN" ]; then
  echo "✅ SonarQube token created: $SONAR_TOKEN"
  echo ""
  echo "🔧 To complete integration:"
  echo "  1. Open GitLab: $GITLAB_URL"
  echo "  2. Go to Group 'ci-cd-samples' → Settings → CI/CD → Variables"  
  echo "  3. Update SONAR_TOKEN variable with: $SONAR_TOKEN"
  echo "  4. Pipelines will now include SonarQube analysis"
else
  echo "⚠️ Could not create SonarQube token automatically"
  echo "   Please create token manually in SonarQube UI"
fi

# Create webhook (if GitLab project exists)
if command -v jq >/dev/null && [ -n "${GITLAB_ROOT_TOKEN:-}" ]; then
  echo "🔗 Setting up SonarQube webhook to GitLab..."
  curl -s -u "$SONAR_USER:$SONAR_PASS" -X POST \
    "$SONAR_URL/api/webhooks/create" \
    -d "name=GitLab" \
    -d "url=$GITLAB_URL/api/v4/projects" > /dev/null 2>&1 || true
fi

echo "✅ SonarQube integration setup completed!"
