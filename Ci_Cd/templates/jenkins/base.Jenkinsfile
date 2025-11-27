pipeline {
  agent {
    docker {
      image '{{DOCKER_IMAGE}}'
      args '-v /var/run/docker.sock:/var/run/docker.sock'
    }
  }
  
  environment {
    DOCKER_IMAGE_NAME = "{{CI_REGISTRY}}/{{JOB_NAME}}:{{BUILD_NUMBER}}"
    REGISTRY_CREDENTIALS = credentials('docker-registry')
    NEXUS_CREDENTIALS = credentials('nexus-credentials')
    SONAR_TOKEN = credentials('sonar-token')
  }
  
  stages {
    stage('Build') {
      steps {
        script {
          {{BUILD_COMMANDS_JENKINS}}
        }
      }
    }
    
    stage('Test') {
      steps {
        script {
          {{TEST_COMMANDS_JENKINS}}
        }
      }
    }
    
    stage('SonarQube Analysis') {
      when {
        expression { env.SONAR_TOKEN != null }
      }
      steps {
        withSonarQubeEnv('MySonarQube') {
          sh 'sonar-scanner -Dsonar.projectKey={{JOB_NAME}} -Dsonar.sources=. -Dsonar.login=${SONAR_TOKEN}'
        }
      }
    }
    
    stage('Docker Build') {
      when {
        branch pattern: "main|master|develop", comparator: "REGEXP"
      }
      steps {
        script {
          sh "docker build -t ${DOCKER_IMAGE_NAME} -t {{CI_REGISTRY}}/{{JOB_NAME}}:latest ."
        }
      }
    }
    
    stage('Docker Push') {
      when {
        branch pattern: "main|master|develop", comparator: "REGEXP"
      }
      steps {
        script {
          withCredentials([usernamePassword(credentialsId: 'docker-registry', usernameVariable: 'DOCKER_USER', passwordVariable: 'DOCKER_PASS')]) {
            sh 'echo $DOCKER_PASS | docker login -u $DOCKER_USER --password-stdin {{CI_REGISTRY}}'
            sh "docker push ${DOCKER_IMAGE_NAME}"
            sh "docker push {{CI_REGISTRY}}/{{JOB_NAME}}:latest"
          }
        }
      }
    }
    
    stage('Package Artifacts') {
      steps {
        script {
          sh 'mkdir -p package_artifacts || true'
          sh '[ -d build ] && cp -r build package_artifacts/ || true'
          sh '[ -d dist ] && cp -r dist package_artifacts/ || true'
          sh '[ -d target ] && cp -r target package_artifacts/ || true'
          sh 'tar -czf artifact.tar.gz package_artifacts || true'
        }
      }
    }
    
    stage('Upload to Nexus') {
      when {
        branch pattern: "main|master|develop", comparator: "REGEXP"
      }
      steps {
        script {
          withCredentials([usernamePassword(credentialsId: 'nexus-credentials', usernameVariable: 'NEXUS_USER', passwordVariable: 'NEXUS_PASS')]) {
            sh "curl -u $NEXUS_USER:$NEXUS_PASS --upload-file artifact.tar.gz https://nexus.example.com/repository/releases/{{JOB_NAME}}-{{BUILD_NUMBER}}.tar.gz || true"
          }
        }
      }
    }
    
    stage('Deploy to Staging') {
      when {
        branch 'develop'
      }
      steps {
        echo "Deploying to Staging..."
        sh "echo Deploying image ${DOCKER_IMAGE_NAME}"
      }
    }
    
    stage('Deploy to Production') {
      when {
        branch pattern: "main|master", comparator: "REGEXP"
      }
      steps {
        input 'Deploy to Production?'
        echo "Deploying to Production..."
        sh "echo Deploying image ${DOCKER_IMAGE_NAME}"
      }
    }
  }
  
  post {
    always {
      cleanWs()
    }
    success {
      echo 'Pipeline succeeded!'
    }
    failure {
      echo 'Pipeline failed!'
    }
  }
}

