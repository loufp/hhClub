#!/usr/bin/env bash
set -euo pipefail

# Полный скрипт запуска инфраструктуры CI/CD
# Поднимает все сервисы и настраивает интеграции

echo "🚀 Starting complete CI/CD infrastructure..."

# Check prerequisites
command -v docker >/dev/null 2>&1 || { echo "❌ Docker is required"; exit 1; }
command -v docker-compose >/dev/null 2>&1 || { echo "❌ docker-compose is required"; exit 1; }
command -v jq >/dev/null 2>&1 || { echo "❌ jq is required (brew install jq)"; exit 1; }

# Create gitlab-runner config directory
mkdir -p ./gitlab-runner/config

echo "📦 Starting all services..."
docker-compose up -d

echo "⏳ Waiting for services to be healthy..."
echo "  - This may take 5-10 minutes on first run"
echo "  - GitLab needs time to initialize database and configure"

# Wait for basic connectivity
services=("jenkins:8080" "sonarqube:9000" "nexus:8181" "gitlab:9080")
for service in "${services[@]}"; do
  host="${service%:*}"
  port="${service#*:}"
  echo -n "⏳ Waiting for $host:$port ... "
  
  for i in {1..60}; do
    if docker exec gitlab wget -q --spider "http://$service" 2>/dev/null; then
      echo "✅"
      break
    fi
    sleep 10
    echo -n "."
  done
done

echo ""
echo "🔧 Running GitLab setup..."
./ci-scripts/gitlab_setup.sh

echo ""
echo "🔧 Setting up SonarQube integration..."
./ci-scripts/sonar_integration.sh

echo ""
echo "🎉 Complete CI/CD infrastructure is ready!"
echo ""
echo "📊 Service URLs:"
echo "  🦊 GitLab:    http://localhost:9080 (root / ChangeMe123!)"
echo "  📊 SonarQube: http://localhost:9000 (admin / admin)"  
echo "  📦 Nexus:     http://localhost:8181 (admin / admin123)"
echo "  🔨 Jenkins:   http://localhost:8080"
echo ""
echo "🧪 Test the setup:"
echo "  1. Open GitLab and go to 'ci-cd-samples' group"
echo "  2. Open any sample project (sample-java, sample-go, etc.)"  
echo "  3. Edit .gitlab-ci.yml and commit → pipeline should start"
echo "  4. Check CI/CD → Pipelines to see running jobs"
echo ""
echo "📝 Log files: docker-compose logs -f [service-name]"
