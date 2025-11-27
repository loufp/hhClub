pipeline {
  agent { docker { image '{{IMAGE}}' args '-v /var/run/docker.sock:/var/run/docker.sock' } }
  environment {
    DOCKER_IMAGE_NAME = "{{IMAGE_NAME}}"
  }
  stages {
    stage('Build') {
      steps {
        sh '''
{{BUILD_CMDS_JENKINS}}
        '''
      }
    }

    stage('Test') {
      steps {
        sh '''
{{TEST_CMDS_JENKINS}}
        '''
      }
    }

    stage('SonarQube Analysis') {
      steps {
        withSonarQubeEnv('MySonarQube') {
          sh 'sonar-scanner -Dsonar.projectKey=${JOB_NAME} -Dsonar.sources=. -Dsonar.login=${SONAR_TOKEN}'
        }
      }
    }

    stage('Package and Publish') {
      steps {
        script {
          sh "docker build -t ${DOCKER_IMAGE_NAME} . -f {{DOCKERFILE}}"
          withCredentials([usernamePassword(credentialsId: 'docker-credentials', usernameVariable: 'DOCKER_USER', passwordVariable: 'DOCKER_PASS')]) {
            sh 'echo $DOCKER_PASS | docker login -u $DOCKER_USER --password-stdin'
            sh 'docker push ${DOCKER_IMAGE_NAME}'
          }

          // create artifact
          sh 'mkdir -p package_artifacts || true'
          sh '[ -d build/libs ] && cp -r build/libs package_artifacts/ || true'
          sh 'tar -czf artifact.tar.gz package_artifacts || true'

          // Nexus upload
          withCredentials([usernamePassword(credentialsId: 'nexus-credentials', usernameVariable: 'NEXUS_USER', passwordVariable: 'NEXUS_PASS')]) {
            sh 'if [ -n "$NEXUS_USER" ]; then curl -u $NEXUS_USER:$NEXUS_PASS --upload-file artifact.tar.gz $NEXUS_URL/repository/maven-releases/${JOB_NAME}-${BUILD_NUMBER}.tar.gz || true; fi'
          }

          // Artifactory upload (credentials id: artifactory-credentials expected)
          withCredentials([usernamePassword(credentialsId: 'artifactory-credentials', usernameVariable: 'ART_USER', passwordVariable: 'ART_PASS')]) {
            sh 'if [ -n "$ART_USER" ] && [ -n "$ARTIFACTORY_URL" ]; then curl -u $ART_USER:$ART_PASS -T artifact.tar.gz "$ARTIFACTORY_URL/$ARTIFACTORY_REPO/${JOB_NAME}-${BUILD_NUMBER}.tar.gz" || true; fi'
          }

          // GitHub Releases (token stored in credentials 'github-token')
          withCredentials([string(credentialsId: 'github-token', variable: 'GITHUB_TOKEN')]) {
            sh 'if [ -n "$GITHUB_TOKEN" ] && [ -n "$GITHUB_REPO" ]; then if command -v gh >/dev/null 2>&1; then gh auth login --with-token < <(echo $GITHUB_TOKEN) || true; gh release create ${GIT_COMMIT} artifact.tar.gz --repo $GITHUB_REPO --title "${JOB_NAME}-${BUILD_NUMBER}" || true; fi; fi'
          }
        }
      }
    }

    stage('Deploy to Staging') {
      steps {
        input message: 'Deploy to staging?', ok: 'Deploy'
        sh 'echo Deploying to staging...'
      }
    }

    stage('Deploy to Production') {
      steps {
        input message: 'Deploy to production?', ok: 'Deploy'
        sh 'echo Deploying to production...'
      }
    }
  }
}
