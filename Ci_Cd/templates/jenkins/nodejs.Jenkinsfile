pipeline {
  agent { docker { image '{{IMAGE}}' args '-v /var/run/docker.sock:/var/run/docker.sock' } }
  stages {
    stage('Build') { steps { script { {{BUILD_CMDS_JENKINS}} } } }
    stage('Test') { steps { script { {{TEST_CMDS_JENKINS}} } } }
    stage('Package') { steps { script { sh "docker build -t $DOCKER_IMAGE_NAME . -f {{DOCKERFILE}}"; sh "docker push $DOCKER_IMAGE_NAME" } } }
  }
}

