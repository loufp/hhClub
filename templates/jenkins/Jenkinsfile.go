pipeline {
  agent any
  environment {
    IMAGE = "{{IMAGE_NAME}}"
    SONAR_HOST = "{{SONAR_HOST}}"
  }
  stages {
    stage('Checkout') { steps { checkout scm } }
    stage('Build') { steps { sh '{{BUILD_CMD}}' } }
    stage('Unit Tests') { steps { sh '{{TEST_CMD}}' } }
    stage('SonarQube') {
      steps {
        withCredentials([string(credentialsId: 'sonar-token', variable: 'SONAR_TOKEN')]) {
          sh "gofmt -l . || true"
          // Placeholder for sonar-scanner config for Go
        }
      }
    }
    stage('Docker Build & Push') { steps { script { docker.build("${IMAGE}:$BUILD_NUMBER").push() } } }
    stage('Deploy') { steps { echo "Deploy steps here" } }
  }
}

