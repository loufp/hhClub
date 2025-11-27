# SonarQube Integration

Полноценная интеграция с SonarQube включая quality gates, правила CE и автоматическую генерацию конфигураций.

## Возможности

✅ **Автоматическая генерация sonar-project.properties**  
✅ **Quality Gate с ожиданием результата**  
✅ **Поддержка всех языков** (Java, Go, Node.js, Python)  
✅ **Coverage reporting интеграция**  
✅ **Полные e2e тесты против реального SonarQube**  
✅ **Таймауты и retry логика**  
✅ **Детальная отчётность по нарушениям**

## Архитектура

```
TemplateService → SonarQubeService
                    ↓
    GenerateSonarProperties()
    GenerateQualityGateScript()
                    ↓
         GitLab CI / Jenkins
                    ↓
            SonarQube Server
                    ↓
           Quality Gate Check
```

## Генерация sonar-project.properties

`SonarQubeService` автоматически создаёт конфигурацию по языку проекта:

### Java/Kotlin
```properties
sonar.projectKey=my-project
sonar.sources=src/main/java
sonar.tests=src/test/java
sonar.java.binaries=build/classes
sonar.coverage.jacoco.xmlReportPaths=build/reports/jacoco/test/jacocoTestReport.xml
sonar.qualitygate.wait=true
sonar.qualitygate.timeout=300
```

### Go
```properties
sonar.projectKey=my-project
sonar.sources=.
sonar.tests=.
sonar.test.inclusions=**/*_test.go
sonar.exclusions=**/*_test.go,vendor/**
sonar.go.coverage.reportPaths=coverage.out
sonar.qualitygate.wait=true
```

### TypeScript/JavaScript
```properties
sonar.projectKey=my-project
sonar.sources=src
sonar.tests=src,test
sonar.test.inclusions=**/*.test.ts,**/*.spec.ts
sonar.javascript.lcov.reportPaths=coverage/lcov.info
sonar.typescript.tsconfigPath=tsconfig.json
sonar.qualitygate.wait=true
```

### Python
```properties
sonar.projectKey=my-project
sonar.sources=.
sonar.tests=tests
sonar.python.coverage.reportPaths=coverage.xml
sonar.python.version=3.11
sonar.qualitygate.wait=true
```

## Quality Gate проверка

Все шаблоны включают полноценную проверку Quality Gate:

```bash
# 1. Дождаться завершения анализа
TASK_URL=$(cat .scannerwork/report-task.txt | grep ceTaskUrl | cut -d'=' -f2-)

# 2. Polling статуса
MAX_ATTEMPTS=30
while [ $ATTEMPT -lt $MAX_ATTEMPTS ]; do
  TASK_STATUS=$(curl -s -u ${SONAR_TOKEN}: "$TASK_URL" | jq -r '.task.status')
  if [ "$TASK_STATUS" = "SUCCESS" ]; then
    break
  fi
  sleep 10
  ATTEMPT=$((ATTEMPT+1))
done

# 3. Проверка Quality Gate
ANALYSIS_ID=$(curl -s -u ${SONAR_TOKEN}: "$TASK_URL" | jq -r '.task.analysisId')
QG_STATUS=$(curl -s -u ${SONAR_TOKEN}: "${SONAR_HOST_URL}/api/qualitygates/project_status?analysisId=$ANALYSIS_ID" | jq -r '.projectStatus.status')

if [ "$QG_STATUS" != "OK" ]; then
  echo "Quality gate failed!"
  # Показать нарушенные условия
  curl -s -u ${SONAR_TOKEN}: "${SONAR_HOST_URL}/api/qualitygates/project_status?analysisId=$ANALYSIS_ID" | jq '.projectStatus.conditions'
  exit 1
fi
```

## Пример в GitLab CI

```yaml
sonar:
  stage: sonar
  image: sonarsource/sonar-scanner-cli:latest
  dependencies:
    - test
  rules:
    - if: $SONAR_TOKEN
  before_script:
    - apk add --no-cache jq curl
  script:
    # Генерация sonar-project.properties
    - |
      cat > sonar-project.properties << EOF
      sonar.projectKey={{CI_PROJECT_PATH}}
      sonar.sources=src
      sonar.qualitygate.wait=true
      sonar.qualitygate.timeout=300
      EOF
    
    # Запуск анализа
    - sonar-scanner -Dsonar.host.url=$SONAR_HOST_URL -Dsonar.login=$SONAR_TOKEN
    
    # Ожидание и проверка Quality Gate
    - |
      TASK_URL=$(cat .scannerwork/report-task.txt | grep ceTaskUrl | cut -d'=' -f2-)
      # ... polling logic ...
      QG_STATUS=$(curl ...)
      if [ "$QG_STATUS" != "OK" ]; then
        exit 1
      fi
```

## Локальное тестирование

### 1. Запуск SonarQube

```bash
cd /Users/kirillkirill13let/RiderProjects/Ci_Cd
docker compose -f Ci_Cd/docker-compose.integration.yml up -d sonarqube
```

Дождаться готовности (60-90 секунд):
```bash
curl http://localhost:9000/api/system/status
# {"status":"UP"}
```

### 2. Автоматический тест-прогон

```bash
./scripts/ci/test-sonarqube-integration.sh
```

Скрипт выполнит:
- ✓ Запуск SonarQube
- ✓ Ожидание готовности
- ✓ Создание тестового проекта
- ✓ Генерация токена
- ✓ Запуск интеграционных тестов
- ✓ Отчёт о результатах

### 3. Ручной запуск тестов

```bash
export SONAR_HOST_URL=http://localhost:9000
export SONAR_TOKEN=admin

dotnet test tests/Ci_Cd.Tests/Ci_Cd.Tests.csproj \
  --filter "FullyQualifiedName~SonarQubeIntegrationTests" \
  -v normal
```

## Интеграционные тесты

### SonarQube_ShouldBeHealthy
Проверяет доступность SonarQube API и статус `UP`.

### SonarQube_CreateProject_AndAnalyze
- Создание проекта через API
- Генерация токена
- Создание тестовых файлов
- Проверка существования проекта

### SonarQube_QualityGate_ConfigurationExists
Проверяет наличие настроенных Quality Gates.

## Настройка Quality Gates

По умолчанию SonarQube включает "Sonar way" quality gate:

### Conditions (стандартные):
- **Coverage**: минимум 80%
- **Duplicated Lines**: максимум 3%
- **Maintainability Rating**: A
- **Reliability Rating**: A
- **Security Rating**: A
- **Security Hotspots Reviewed**: 100%

### Кастомизация

Через SonarQube UI:
1. Quality Gates → Create
2. Добавить условия
3. Set as Default

Через API:
```bash
curl -u admin:admin -X POST \
  "http://localhost:9000/api/qualitygates/create?name=Custom+Gate"

curl -u admin:admin -X POST \
  "http://localhost:9000/api/qualitygates/create_condition?gateId=1&metric=coverage&op=LT&error=80"
```

## Переменные окружения

| Переменная | Описание | Пример |
|-----------|----------|--------|
| `SONAR_HOST_URL` | URL SonarQube сервера | `http://localhost:9000` |
| `SONAR_TOKEN` | Токен для аутентификации | `squ_xxx` |
| `SONAR_PROJECT_KEY` | Ключ проекта | `my-org/my-project` |

## Метрики и отчётность

### Coverage Integration

**Java (JaCoCo):**
```yaml
test:
  script:
    - ./gradlew test jacocoTestReport
  artifacts:
    reports:
      coverage_report:
        coverage_format: jacoco
        path: build/reports/jacoco/test/jacocoTestReport.xml
```

**Go:**
```yaml
test:
  script:
    - go test -coverprofile=coverage.out ./...
    - go tool cover -html=coverage.out -o coverage.html
```

**JavaScript/TypeScript:**
```yaml
test:
  script:
    - npm run test -- --coverage
  artifacts:
    paths:
      - coverage/lcov.info
```

**Python:**
```yaml
test:
  script:
    - pytest --cov=. --cov-report=xml
```

### Quality Gate Results

При провале Quality Gate, лог покажет нарушенные условия:

```json
{
  "projectStatus": {
    "status": "ERROR",
    "conditions": [
      {
        "status": "ERROR",
        "metricKey": "coverage",
        "comparator": "LT",
        "errorThreshold": "80",
        "actualValue": "65.3"
      }
    ]
  }
}
```

## Troubleshooting

### SonarQube не запускается

```bash
# Проверить логи
docker compose -f Ci_Cd/docker-compose.integration.yml logs sonarqube

# Увеличить memory для Docker
# Docker Desktop → Settings → Resources → Memory: 4GB+
```

### Quality Gate зависает

Увеличить timeout:
```properties
sonar.qualitygate.timeout=600
```

### Отсутствует coverage

Убедитесь, что:
1. Тесты генерируют coverage отчёт
2. Путь к отчёту правильный в `sonar-project.properties`
3. Артефакт coverage доступен на этапе sonar

### Token authentication failed

Создать новый токен:
```bash
curl -u admin:admin -X POST \
  "http://localhost:9000/api/user_tokens/generate?name=ci-token"
```

## Production Deployment

### Для GitLab CI

Добавить в Variables:
```
SONAR_HOST_URL = https://sonarqube.company.com
SONAR_TOKEN = <masked token>
```

### Для Jenkins

Credentials:
```groovy
withSonarQubeEnv('MySonarQube') {
  sh 'sonar-scanner'
}
```

Quality Gate check:
```groovy
timeout(time: 5, unit: 'MINUTES') {
  def qg = waitForQualityGate()
  if (qg.status != 'OK') {
    error "Quality gate failed: ${qg.status}"
  }
}
```

## Best Practices

✅ **Всегда используйте Quality Gate wait**  
✅ **Настройте branch analysis для PR**  
✅ **Интегрируйте coverage отчёты**  
✅ **Настройте notifications**  
✅ **Используйте pull request decoration**  
✅ **Регулярно обновляйте правила**

## Ссылки

- [SonarQube Documentation](https://docs.sonarqube.org/)
- [Quality Gates](https://docs.sonarqube.org/latest/user-guide/quality-gates/)
- [Web API](https://next.sonarqube.com/sonarqube/web_api)
- [Coverage Import](https://docs.sonarqube.org/latest/analysis/coverage/)

