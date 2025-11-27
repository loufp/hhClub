pipeline {
  agent any
  environment {
    IMAGE = "{{IMAGE_NAME}}"
    SONAR_HOST = "{{SONAR_HOST}}"
    NEXUS_URL = "{{NEXUS_URL}}"
  }
  stages {
    stage('Checkout') {
      steps { checkout scm }
    }
    stage('Build') {
      steps {
        sh '{{BUILD_CMD}}'
      }
    }
    stage('Unit Tests') {
      steps {
        sh '{{TEST_CMD}}'
      }
    }
    stage('SonarQube Analysis') {
      steps {
        withCredentials([string(credentialsId: 'sonar-token', variable: 'SONAR_TOKEN')]) {
          sh "mvn sonar:sonar -Dsonar.host.url=${SONAR_HOST} -Dsonar.login=${SONAR_TOKEN}"
        }
      }
    }
    stage('Docker Build & Push') {
      steps {
        script {
          docker.build("${IMAGE}:$BUILD_NUMBER").push()
        }
      }
    }
    stage('Upload Artifacts') {
      steps {
        sh "python3 scripts/nexus_push.py --file target/*.jar --nexus ${NEXUS_URL}"
      }
    }
    stage('Deploy to staging') {
      when { branch 'develop' }
      steps {
        echo "deploy to staging — placeholder"
      }
    }
    stage('Deploy to production') {
      when { branch 'master' }
      steps {
        input message: 'Confirm deploy to production'
        echo "deploy to prod — placeholder"
      }
    }
  }
  post {
    always { junit allowEmptyResults: true, testResults: '**/target/surefire-reports/*.xml' }
  }
}

