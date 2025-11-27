#!/usr/bin/env bash
set -euo pipefail

COMPOSE_FILE="Ci_Cd/Ci_Cd/docker-compose.ci.yml"
UP=true
TIMEOUT=300

function wait_for() {
  local url=$1; local name=$2; local timeout=${3:-120}
  echo "Waiting for $name at $url";
  local start=$(date +%s)
  while true; do
    if curl -sSf "$url" >/dev/null 2>&1; then
      echo "$name is up"; break
    fi
    if [ $(( $(date +%s) - start )) -gt $timeout ]; then
      echo "$name did not become ready in $timeout seconds" >&2; return 1
    fi
    sleep 3
  done
}

if [ "$UP" = true ]; then
  echo "Starting local CI stack..."
  docker compose -f "$COMPOSE_FILE" up -d
  echo "Waiting services..."
  wait_for "http://localhost:8080" "Jenkins" 120 || true
  wait_for "http://localhost:9000" "SonarQube" 120 || true
  wait_for "http://localhost:8081" "Nexus" 120 || true
fi

# Generate pipelines for a sample repo
OUT_DIR="/tmp/cicd_e2e_out"
rm -rf "$OUT_DIR" || true; mkdir -p "$OUT_DIR"

echo "Running generator for sample repo..."
dotnet run --project Ci_Cd/Ci_Cd.csproj -- --repo https://github.com/loufp/hhClub --output "$OUT_DIR" --format dir || true

echo "Generated files:"; ls -la "$OUT_DIR" || true

# If Jenkins credentials provided, create job and trigger build
if [ -n "${JENKINS_URL:-}" ] && [ -n "${JENKINS_USER:-}" ] && [ -n "${JENKINS_TOKEN:-}" ]; then
  echo "Creating Jenkins job and triggering build..."
  JOB_NAME="cicd-e2e-test"
  JENKINS_CRUMB=""

  # get crumb
  CRUMB_JSON=$(curl -s -u "$JENKINS_USER:$JENKINS_TOKEN" "$JENKINS_URL/crumbIssuer/api/json" || true)
  if [ -n "$CRUMB_JSON" ]; then
    JENKINS_CRUMB=$(echo "$CRUMB_JSON" | sed -n 's/.*"crumb"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')
  fi

  JF="$OUT_DIR/Jenkinsfile"
  if [ -f "$JF" ]; then
    # read Jenkinsfile content
    JF_CONTENT=$(sed 's/&/&amp;/g; s/</\&lt;/g; s/>/\&gt;/g' "$JF")
    # build config.xml for pipeline job using script
    read -r -d '' CONFIG_XML <<EOF || true
<flow-definition plugin="workflow-job@2.40">
  <description>Auto-generated E2E job</description>
  <keepDependencies>false</keepDependencies>
  <properties/>
  <definition class="org.jenkinsci.plugins.workflow.cps.CpsFlowDefinition" plugin="workflow-cps@2.93">
    <script><![CDATA[
$JF_CONTENT
    ]]></script>
    <sandbox>true</sandbox>
  </definition>
  <triggers/>
  <disabled>false</disabled>
</flow-definition>
EOF

    # create job
    echo "Creating job $JOB_NAME on $JENKINS_URL"
    # if job exists, delete it first to ensure fresh config
    EXISTING=$(curl -s -o /dev/null -w "%{http_code}" -u "$JENKINS_USER:$JENKINS_TOKEN" "$JENKINS_URL/job/$JOB_NAME/" || true)
    if [ "$EXISTING" = "200" ]; then
      echo "Job $JOB_NAME exists — deleting..."
      if [ -n "$JENKINS_CRUMB" ]; then
        curl -s -u "$JENKINS_USER:$JENKINS_TOKEN" -X POST -H "Jenkins-Crumb: $JENKINS_CRUMB" "$JENKINS_URL/job/$JOB_NAME/doDelete" || true
      else
        curl -s -u "$JENKINS_USER:$JENKINS_TOKEN" -X POST "$JENKINS_URL/job/$JOB_NAME/doDelete" || true
      fi
      # small pause
      sleep 1
    fi

    if [ -n "$JENKINS_CRUMB" ]; then
      curl -s -u "$JENKINS_USER:$JENKINS_TOKEN" -H "Jenkins-Crumb: $JENKINS_CRUMB" -H "Content-Type: application/xml" --data-binary @- "$JENKINS_URL/createItem?name=$JOB_NAME" <<< "$CONFIG_XML" || true
    else
      curl -s -u "$JENKINS_USER:$JENKINS_TOKEN" -H "Content-Type: application/xml" --data-binary @- "$JENKINS_URL/createItem?name=$JOB_NAME" <<< "$CONFIG_XML" || true
    fi

    # trigger build
    echo "Triggering build for $JOB_NAME"
    if [ -n "$JENKINS_CRUMB" ]; then
      curl -s -u "$JENKINS_USER:$JENKINS_TOKEN" -X POST -H "Jenkins-Crumb: $JENKINS_CRUMB" "$JENKINS_URL/job/$JOB_NAME/build" || true
    else
      curl -s -u "$JENKINS_USER:$JENKINS_TOKEN" -X POST "$JENKINS_URL/job/$JOB_NAME/build" || true
    fi

    # poll for build result
    echo "Waiting for build to start..."
    START=$(date +%s)
    BUILD_NUMBER=""
    while [ -z "$BUILD_NUMBER" ]; do
      if [ $(( $(date +%s) - START )) -gt $TIMEOUT ]; then
        echo "Build did not start in time"; break
      fi
      # get last build number
      LAST_JSON=$(curl -s -u "$JENKINS_USER:$JENKINS_TOKEN" "$JENKINS_URL/job/$JOB_NAME/api/json" || true)
      BUILD_NUMBER=$(echo "$LAST_JSON" | sed -n 's/.*"lastBuild"[^{]*{[^}]*"number"[[:space:]]*:[[:space:]]*\([0-9]*\).*/\1/p')
      if [ -n "$BUILD_NUMBER" ]; then echo "Build number: $BUILD_NUMBER"; break; fi
      sleep 2
    done

    # wait for build completion
    if [ -n "$BUILD_NUMBER" ]; then
      echo "Waiting for build $BUILD_NUMBER to finish..."
      START2=$(date +%s)
      while true; do
        if [ $(( $(date +%s) - START2 )) -gt $TIMEOUT ]; then
          echo "Build did not finish in time"; break
        fi
        BUILD_JSON=$(curl -s -u "$JENKINS_USER:$JENKINS_TOKEN" "$JENKINS_URL/job/$JOB_NAME/$BUILD_NUMBER/api/json" || true)
        BUILD_RESULT=$(echo "$BUILD_JSON" | sed -n 's/.*"result"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p') || true
        if [ -n "$BUILD_RESULT" ]; then
          echo "Build result: $BUILD_RESULT"
          break
        fi
        sleep 3
      done
    fi
  else
    echo "Jenkinsfile not found in $OUT_DIR, skipping job creation"
  fi
fi

# If GitLab credentials provided, create project and push .gitlab-ci.yml via API
if [ -n "${GITLAB_URL:-}" ] && [ -n "${GITLAB_TOKEN:-}" ]; then
  echo "Creating GitLab project and triggering pipeline..."
  GL_API="$GITLAB_URL/api/v4"
  PROJ_NAME="cicd-e2e-test"

  # delete existing project if exists
  EXIST=$(curl -s -o /dev/null -w "%{http_code}" -H "PRIVATE-TOKEN: $GITLAB_TOKEN" "$GL_API/projects?search=$PROJ_NAME") || true
  # create project
  CREATE_RESP=$(curl -s -X POST -H "Content-Type: application/json" -H "PRIVATE-TOKEN: $GITLAB_TOKEN" -d "{\"name\": \"$PROJ_NAME\", \"visibility\": \"private\", \"initialize_with_readme\": true}" "$GL_API/projects")
  PROJECT_ID=$(echo "$CREATE_RESP" | sed -n 's/.*"id"[[:space:]]*:[[:space:]]*\([0-9]*\).*/\1/p')
  if [ -z "$PROJECT_ID" ]; then
    echo "Failed to create project: $CREATE_RESP"; else echo "Created project id: $PROJECT_ID"; fi

  # prepare .gitlab-ci.yml content
  GL_FILE="$OUT_DIR/.gitlab-ci.yml"
  if [ ! -f "$GL_FILE" ]; then
    echo ".gitlab-ci.yml not found in $OUT_DIR, attempting to generate via dotnet run"
    dotnet run --project Ci_Cd/Ci_Cd.csproj -- --repo https://github.com/loufp/hhClub --output "$OUT_DIR" --format dir || true
  fi

  if [ -f "$GL_FILE" ]; then
    CI_CONTENT=$(python3 - <<PY
import base64,sys
print(base64.b64encode(open('$GL_FILE','rb').read()).decode())
PY
)
    # create file via API on default branch (usually master/main)
    BRANCH="main"
    # try to detect default_branch
    DEF_BRANCH=$(echo "$CREATE_RESP" | sed -n 's/.*"default_branch"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')
    if [ -n "$DEF_BRANCH" ]; then BRANCH="$DEF_BRANCH"; fi

    # create or update file
    echo "Creating .gitlab-ci.yml on branch $BRANCH"
    CREATE_FILE_RESP=$(curl -s -X POST -H "PRIVATE-TOKEN: $GITLAB_TOKEN" -H "Content-Type: application/json" -d "{\"branch\": \"$BRANCH\", \"commit_message\": \"Add CI\", \"actions\": [{\"action\": \"create\", \"file_path\": \".gitlab-ci.yml\", \"content\": \"$(sed ':a;N;$!ba;s/"/\\\"/g' "$GL_FILE")\"}]}" "$GL_API/projects/$PROJECT_ID/repository/commits")

    # trigger pipeline
    TRIGGER=$(curl -s -X POST -H "PRIVATE-TOKEN: $GITLAB_TOKEN" "$GL_API/projects/$PROJECT_ID/pipeline?ref=$BRANCH")
    PIPE_ID=$(echo "$TRIGGER" | sed -n 's/.*"id"[[:space:]]*:[[:space:]]*\([0-9]*\).*/\1/p')
    if [ -z "$PIPE_ID" ]; then echo "Failed to trigger pipeline: $TRIGGER"; else echo "Triggered pipeline id: $PIPE_ID"; fi

    # wait for pipeline
    if [ -n "$PIPE_ID" ]; then
      START=$(date +%s)
      while true; do
        if [ $(( $(date +%s) - START )) -gt $TIMEOUT ]; then
          echo "Pipeline did not finish in time"; break
        fi
        PJSON=$(curl -s -H "PRIVATE-TOKEN: $GITLAB_TOKEN" "$GL_API/projects/$PROJECT_ID/pipelines/$PIPE_ID")
        STATUS=$(echo "$PJSON" | sed -n 's/.*"status"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p') || true
        if [ -n "$STATUS" ] && [ "$STATUS" != "running" ] && [ "$STATUS" != "pending" ]; then
          echo "Pipeline status: $STATUS"; break
        fi
        sleep 5
      done
    fi
  else
    echo ".gitlab-ci.yml still not found, skipping GitLab pipeline creation"
  fi

  # attempt to register a runner if token available
  RUNNER_TOKEN=$(echo "$CREATE_RESP" | sed -n 's/.*"runners_token"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')
  if [ -z "$RUNNER_TOKEN" ]; then
    # try to get project details
    PDETAILS=$(curl -s -H "PRIVATE-TOKEN: $GITLAB_TOKEN" "$GL_API/projects/$PROJECT_ID")
    RUNNER_TOKEN=$(echo "$PDETAILS" | sed -n 's/.*"runners_token"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')
  fi

  if [ -n "$RUNNER_TOKEN" ]; then
    echo "Runner token: ${RUNNER_TOKEN:0:6}..."
    if command -v docker >/dev/null 2>&1; then
      echo "Registering GitLab runner via docker"
      mkdir -p /tmp/gitlab-runner-config
      docker run --rm -v /tmp/gitlab-runner-config:/etc/gitlab-runner gitlab/gitlab-runner register --non-interactive --url "$GITLAB_URL" --registration-token "$RUNNER_TOKEN" --executor docker --docker-image "docker:24.0.5" --description "cicd-e2e-runner" --tag-list "e2e" --run-untagged="true" --locked="false" || true
      echo "Starting gitlab-runner container"
      docker run -d --name gitlab-runner --restart always -v /tmp/gitlab-runner-config:/etc/gitlab-runner -v /var/run/docker.sock:/var/run/docker.sock gitlab/gitlab-runner:latest || true
      sleep 5
    else
      echo "Docker not available: cannot register runner"
    fi
  else
    echo "No runner token available for project; skipping runner registration"
  fi
fi

# Optionally teardown
if [ "$UP" = true ]; then
  echo "Teardown stack..."
  # docker compose -f "$COMPOSE_FILE" down --volumes || true
  echo "(To teardown manually run: docker compose -f $COMPOSE_FILE down --volumes)"
fi

echo "E2E smoke finished"
