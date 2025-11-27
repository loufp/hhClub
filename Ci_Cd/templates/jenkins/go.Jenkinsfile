pipeline {
  agent { docker { image '{{IMAGE}}' args '-v /var/run/docker.sock:/var/run/docker.sock' } }
  stages {
    stage('Build') { steps { sh '{{BUILD_CMDS_JENKINS}}' } }
    stage('Test') { steps { sh '{{TEST_CMDS_JENKINS}}' } }
    stage('Package') { steps { sh "docker build -t $DOCKER_IMAGE_NAME . -f {{DOCKERFILE}}"; sh "docker push $DOCKER_IMAGE_NAME" } }
  }
}

