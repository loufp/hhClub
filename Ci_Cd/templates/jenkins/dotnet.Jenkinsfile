pipeline {
  agent {
    docker { image '{{IMAGE}}' args '-v /var/run/docker.sock:/var/run/docker.sock' }
  }
  environment {
    DOCKER_IMAGE_NAME = "{{IMAGE_NAME}}"
  }
  stages {
    stage('Build') { steps { script { {{BUILD_CMDS_JENKINS}} } } }
    stage('Test') { steps { script { {{TEST_CMDS_JENKINS}} } } }
    stage('Sonar') { steps { withSonarQubeEnv('MySonarQube') { sh 'sonar-scanner -Dsonar.projectKey=${JOB_NAME} -Dsonar.sources=. -Dsonar.login=${SONAR_TOKEN}' } } }
    stage('Package') { steps { script { sh "docker build -t $DOCKER_IMAGE_NAME . -f {{DOCKERFILE}}"; sh "docker push $DOCKER_IMAGE_NAME" } } }
    stage('Deploy') { steps { input 'Deploy to Production?'; sh "echo Deploying $DOCKER_IMAGE_NAME" } }
  }
}

