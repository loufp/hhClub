#!/usr/bin/env bash
set -euo pipefail

# Полностью автоматизированный скрипт настройки GitLab для CI/CD тестирования
# Создает токен, группу, проект, регистрирует runner, настраивает интеграцию с SonarQube/Nexus

GITLAB_URL=${GITLAB_URL:-http://localhost:9080}
GITLAB_ROOT_PASS=${GITLAB_ROOT_PASS:-ChangeMe123!}
GITLAB_API="$GITLAB_URL/api/v4"
SONAR_URL=${SONAR_URL:-http://sonarqube:9000}
NEXUS_URL=${NEXUS_URL:-http://nexus:8181}

echo "🚀 Starting GitLab CI/CD infrastructure setup..."

# Wait for GitLab to be fully ready
echo "⏳ Waiting for GitLab to be ready at $GITLAB_URL ..."
until curl -sSf "$GITLAB_URL/users/sign_in" > /dev/null 2>&1; do
  sleep 10
  echo -n '.'
done
echo -e "\n✅ GitLab web interface is up"

# Wait for GitLab to be fully initialized (check API health)
echo "⏳ Waiting for GitLab API to be ready..."
for i in {1..30}; do
  if curl -sSf "$GITLAB_API/version" > /dev/null 2>&1; then
    echo "✅ GitLab API is ready"
    break
  fi
  sleep 10
  echo -n '.'
done

# Create root access token automatically via Rails console
echo "🔑 Creating root access token via Rails console..."
ROOT_TOKEN=$(docker exec gitlab gitlab-rails runner "
  user = User.find_by(username: 'root')
  token = user.personal_access_tokens.create(
    name: 'ci-automation-token',
    scopes: ['api', 'read_user', 'read_repository', 'write_repository']
  )
  puts token.token
" 2>/dev/null | tail -1)

if [ -z "$ROOT_TOKEN" ] || [ "$ROOT_TOKEN" == "null" ]; then
  echo "❌ Failed to create root token automatically. Using fallback method..."
  # Try alternative method via session API
  ROOT_LOGIN_RESPONSE=$(curl -s -X POST "$GITLAB_API/session" -d "login=root&password=$GITLAB_ROOT_PASS" 2>/dev/null || echo "{}")
  ROOT_TOKEN=$(echo "$ROOT_LOGIN_RESPONSE" | jq -r '.private_token // empty' 2>/dev/null || echo "")
  
  if [ -z "$ROOT_TOKEN" ]; then
    echo "❌ Could not create token automatically. Please:"
    echo "1. Open $GITLAB_URL"
    echo "2. Login as root (password: $GITLAB_ROOT_PASS)"
    echo "3. Create Personal Access Token with 'api' scope"
    echo "4. Run: GITLAB_ROOT_TOKEN=<your-token> $0"
    exit 1
  fi
fi

echo "✅ Root token obtained"

# Create or find group
GROUP_NAME=${GROUP_NAME:-ci-cd-samples}
echo "🏗️ Creating group '$GROUP_NAME'..."
GROUP_RESPONSE=$(curl -s -X POST "$GITLAB_API/groups" -H "PRIVATE-TOKEN: $ROOT_TOKEN" \
  -d "name=$GROUP_NAME&path=$GROUP_NAME&description=Automated CI/CD samples group" 2>/dev/null || echo "{}")
GROUP_ID=$(echo "$GROUP_RESPONSE" | jq -r '.id // empty' 2>/dev/null || echo "")

if [ -z "$GROUP_ID" ]; then
  echo "📁 Group may exist, searching..."
  GROUP_ID=$(curl -s -H "PRIVATE-TOKEN: $ROOT_TOKEN" "$GITLAB_API/groups?search=$GROUP_NAME" | jq -r '.[0].id // empty' 2>/dev/null || echo "")
fi

if [ -z "$GROUP_ID" ]; then
  echo "❌ Failed to create or find group"; exit 1
fi

echo "✅ Group ID: $GROUP_ID"

# Wait for services to be ready
echo "⏳ Waiting for SonarQube and Nexus to be ready..."
for service in "sonarqube:9000" "nexus:8181"; do
  until docker exec gitlab wget -q --spider "http://$service" 2>/dev/null; do
    sleep 5
    echo -n '.'
  done
done
echo -e "\n✅ All services are ready"

# Configure CI variables for the group with comprehensive settings
echo "⚙️ Setting up CI/CD variables..."
variables=(
  "SONAR_HOST|$SONAR_URL"
  "SONAR_TOKEN|squ_example_token_replace_with_real"
  "NEXUS_URL|$NEXUS_URL"
  "NEXUS_USER|admin"
  "NEXUS_PASSWORD|admin123"
  "DOCKER_REGISTRY|localhost:5000"
  "DOCKER_TLS_CERTDIR|/certs"
  "DOCKER_HOST|tcp://docker-dind:2376"
)

for var in "${variables[@]}"; do
  key="${var%%|*}"
  value="${var##*|}"
  curl -s -X POST "$GITLAB_API/groups/$GROUP_ID/variables" \
    -H "PRIVATE-TOKEN: $ROOT_TOKEN" \
    -d "key=$key&value=$value&protected=false&masked=false" > /dev/null 2>&1 || true
done

# Create sample projects for each supported language
LANGUAGES=("java" "go" "nodejs" "python")
for lang in "${LANGUAGES[@]}"; do
  PROJECT_NAME="sample-$lang"
  echo "📦 Creating project '$PROJECT_NAME'..."
  
  PROJECT_RESPONSE=$(curl -s -X POST "$GITLAB_API/projects" -H "PRIVATE-TOKEN: $ROOT_TOKEN" \
    -d "name=$PROJECT_NAME&namespace_id=$GROUP_ID&initialize_with_readme=true&description=Sample $lang project for CI/CD testing" \
    2>/dev/null || echo "{}")
  
  PROJECT_ID=$(echo "$PROJECT_RESPONSE" | jq -r '.id // empty' 2>/dev/null || echo "")
  PROJECT_WEB_URL=$(echo "$PROJECT_RESPONSE" | jq -r '.web_url // empty' 2>/dev/null || echo "")
  
  if [ -n "$PROJECT_ID" ] && [ "$PROJECT_ID" != "null" ]; then
    echo "✅ Created project: $PROJECT_WEB_URL"
    
    # Add sample .gitlab-ci.yml to project
    GITLAB_CI_CONTENT=$(cat <<EOF
# Auto-generated GitLab CI for $lang project
stages:
  - build
  - test
  - sonar
  - docker
  - deploy

variables:
  MAVEN_OPTS: "-Dmaven.repo.local=\$CI_PROJECT_DIR/.m2/repository"
  
build_$lang:
  stage: build
  image: $([ "$lang" = "java" ] && echo "maven:3.9-eclipse-temurin-17" || echo "alpine:latest")
  script:
    - echo "Building $lang project..."
    - $([ "$lang" = "java" ] && echo "mvn clean compile" || echo "echo 'Build step for $lang'")
  cache:
    paths:
      - $([ "$lang" = "java" ] && echo ".m2/repository" || echo ".cache")

test_$lang:
  stage: test  
  image: $([ "$lang" = "java" ] && echo "maven:3.9-eclipse-temurin-17" || echo "alpine:latest")
  script:
    - echo "Testing $lang project..."
    - $([ "$lang" = "java" ] && echo "mvn test" || echo "echo 'Test step for $lang'")

sonarqube_$lang:
  stage: sonar
  image: $([ "$lang" = "java" ] && echo "maven:3.9-eclipse-temurin-17" || echo "sonarqube/sonar-scanner-cli")
  script:
    - echo "SonarQube analysis for $lang..."
    - echo "sonar.projectKey=$PROJECT_NAME" > sonar-project.properties
    - echo "sonar.sources=." >> sonar-project.properties
  rules:
    - if: \$SONAR_TOKEN
      
docker_build:
  stage: docker
  image: docker:20.10.16
  services:
    - docker:20.10.16-dind
  script:
    - echo "FROM alpine:latest" > Dockerfile
    - echo "RUN echo 'Sample $lang application'" >> Dockerfile
    - docker build -t \$CI_PROJECT_NAME:\$CI_COMMIT_SHORT_SHA .
  rules:
    - if: \$CI_COMMIT_BRANCH == "main"
EOF
    )
    
    # Create .gitlab-ci.yml file in project
    curl -s -X POST "$GITLAB_API/projects/$PROJECT_ID/repository/files/%2Egitlab%2Dci%2Eyml" \
      -H "PRIVATE-TOKEN: $ROOT_TOKEN" \
      -d "branch=main&content=$(echo "$GITLAB_CI_CONTENT" | base64 -w 0)&commit_message=Add GitLab CI configuration&encoding=base64" > /dev/null 2>&1 || true
  fi
done

# Register runner with Docker-in-Docker support
echo "🏃 Registering GitLab Runner..."
# Get group registration token (GitLab 15.6+ uses new runner registration)
REGISTRATION_TOKEN=$(curl -s -H "PRIVATE-TOKEN: $ROOT_TOKEN" "$GITLAB_API/groups/$GROUP_ID" | jq -r '.runners_token // empty' 2>/dev/null)

if [ -z "$REGISTRATION_TOKEN" ]; then
  echo "❌ Failed to get group registration token"; exit 1
fi

# Register runner with proper Docker-in-Docker configuration
docker exec -i gitlab-runner gitlab-runner register --non-interactive \
  --url "$GITLAB_URL/" \
  --registration-token "$REGISTRATION_TOKEN" \
  --executor "docker" \
  --docker-image "alpine:latest" \
  --docker-privileged="true" \
  --docker-volumes "/certs/client" \
  --docker-network-mode "ci_cd_default" \
  --description "ci-cd-docker-runner" \
  --tag-list "docker,dind,privileged" \
  --run-untagged="true" \
  --locked="false" || echo "⚠️ Runner registration may have failed, check manually"

echo -e "\n🎉 GitLab CI/CD infrastructure setup completed!"
echo "📊 Summary:"
echo "  - GitLab UI: $GITLAB_URL (root / $GITLAB_ROOT_PASS)"
echo "  - Group: $GROUP_NAME (ID: $GROUP_ID)"
echo "  - Projects created: ${LANGUAGES[*]/#/sample-}"
echo "  - SonarQube: $SONAR_URL"
echo "  - Nexus: $NEXUS_URL"
echo ""
echo "🔧 Next steps:"
echo "  1. Open $GITLAB_URL and explore the sample projects"
echo "  2. Push code to trigger CI/CD pipelines"
echo "  3. Configure SonarQube token in Group → Settings → CI/CD → Variables"
echo "  4. Check runners in Group → CI/CD → Runners"

