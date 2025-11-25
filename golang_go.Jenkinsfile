pipeline {
    agent {
        docker { image 'golang:1.21' }
    }

    stages {
        stage('Build') {
            steps {
                sh 'go build ./...'
                sh 'go test ./...'
            }
        }

        stage('Test') {
            steps {
                sh 'go test ./...'
            }
        }

        stage('Package') {
            steps {
                sh 'docker build -t myapp:latest .' 
                echo 'Docker image built successfully'
            }
        }

        stage('Deploy') {
            steps {
                sh "if [ -n '$DOCKER_REGISTRY' ]; then docker tag myapp:latest $DOCKER_REGISTRY/myapp:latest && docker push $DOCKER_REGISTRY/myapp:latest; else echo 'DOCKER_REGISTRY not set, skipping push'; fi"
                sh "if [ -n '$KUBE_CONFIG_BASE64' ]; then echo $KUBE_CONFIG_BASE64 | base64 -d > kubeconfig && KUBECONFIG=kubeconfig kubectl apply -f k8s/; else echo 'KUBE_CONFIG_BASE64 not set, skipping k8s apply'; fi"
            }
        }

    }
}

