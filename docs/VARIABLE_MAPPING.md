# Variable Mapping: Jenkins ↔ GitLab

Централизованный маппер переменных обеспечивает единообразное использование переменных окружения в шаблонах пайплайнов для Jenkins и GitLab CI.

## Архитектура

```
TemplateService
    ↓
TemplateEngine
    ↓
VariableMapper → MapToGitLab / MapToJenkins
```

## Карта соответствия переменных

| Универсальная переменная | GitLab CI | Jenkins |
|-------------------------|-----------|---------|
| `{{CI_COMMIT_REF_NAME}}` | `$CI_COMMIT_REF_NAME` | `${env.BRANCH_NAME}` |
| `{{CI_COMMIT_SHORT_SHA}}` | `$CI_COMMIT_SHORT_SHA` | `${env.GIT_COMMIT.take(8)}` |
| `{{CI_COMMIT_SHA}}` | `$CI_COMMIT_SHA` | `${env.GIT_COMMIT}` |
| `{{CI_PROJECT_NAME}}` | `$CI_PROJECT_NAME` | `${env.JOB_BASE_NAME}` |
| `{{CI_PROJECT_PATH}}` | `$CI_PROJECT_PATH` | `${env.JOB_NAME}` |
| `{{CI_PIPELINE_ID}}` | `$CI_PIPELINE_ID` | `${env.BUILD_ID}` |
| `{{CI_JOB_ID}}` | `$CI_JOB_ID` | `${env.BUILD_ID}` |
| `{{CI_REGISTRY}}` | `$CI_REGISTRY` | `registry.example.com` |
| `{{CI_REGISTRY_USER}}` | `$CI_REGISTRY_USER` | `${REGISTRY_CREDENTIALS_USR}` |
| `{{CI_REGISTRY_PASSWORD}}` | `$CI_REGISTRY_PASSWORD` | `${REGISTRY_CREDENTIALS_PSW}` |
| `{{BUILD_NUMBER}}` | `$CI_PIPELINE_IID` | `${env.BUILD_NUMBER}` |
| `{{JOB_NAME}}` | `$CI_PROJECT_PATH` | `${env.JOB_NAME}` |
| `{{BRANCH_NAME}}` | `$CI_COMMIT_REF_NAME` | `${env.BRANCH_NAME}` |
| `{{WORKSPACE}}` | `$CI_PROJECT_DIR` | `${env.WORKSPACE}` |

## Использование в шаблонах

### Пример универсального шаблона:

```yaml
docker_build:
  script:
    - docker build -t {{CI_REGISTRY}}/{{CI_PROJECT_PATH}}:{{CI_COMMIT_SHORT_SHA}} .
    - docker push {{CI_REGISTRY}}/{{CI_PROJECT_PATH}}:{{CI_COMMIT_SHORT_SHA}}
```

### После маппинга в GitLab CI:

```yaml
docker_build:
  script:
    - docker build -t $CI_REGISTRY/$CI_PROJECT_PATH:$CI_COMMIT_SHORT_SHA .
    - docker push $CI_REGISTRY/$CI_PROJECT_PATH:$CI_COMMIT_SHORT_SHA
```

### После маппинга в Jenkins:

```groovy
stage('Docker Build') {
  steps {
    sh "docker build -t registry.example.com/\${env.JOB_NAME}:\${env.GIT_COMMIT.take(8)} ."
    sh "docker push registry.example.com/\${env.JOB_NAME}:\${env.GIT_COMMIT.take(8)}"
  }
}
```

## Добавление новых переменных

Для добавления новой переменной в маппер:

1. Откройте `Services/VariableMapper.cs`
2. Добавьте новую пару в словари `_gitlabVariables` и `_jenkinsVariables`
3. Обновите шаблоны в `templates/gitlab/` и `templates/jenkins/`
4. Добавьте unit-тест в `tests/Ci_Cd.Tests/VariableMapperTests.cs`

## Преимущества централизованного маппинга

✅ **Единый источник истины**: Все правила маппинга в одном месте  
✅ **Легкая поддержка**: Изменения применяются автоматически ко всем шаблонам  
✅ **Переносимость**: Шаблоны работают в обеих CI-системах без изменений  
✅ **Тестируемость**: Unit-тесты гарантируют корректность маппинга  
✅ **Расширяемость**: Легко добавлять новые переменные и CI-системы

## Пример использования в коде

```csharp
var mapper = new VariableMapper();
var template = "Image: {{CI_REGISTRY}}/{{CI_PROJECT_PATH}}:{{BUILD_NUMBER}}";

// Для GitLab
var gitlabConfig = mapper.MapToGitLab(template);
// Результат: "Image: $CI_REGISTRY/$CI_PROJECT_PATH:$CI_PIPELINE_IID"

// Для Jenkins
var jenkinsConfig = mapper.MapToJenkins(template);
// Результат: "Image: registry.example.com/${env.JOB_NAME}:${env.BUILD_NUMBER}"
```

## Интеграция с TemplateEngine

`TemplateEngine` автоматически применяет маппинг при рендеринге:

```csharp
var engine = new TemplateEngine(variableMapper);

// Для GitLab
var gitlabYaml = engine.RenderForGitLab(template, analysis);

// Для Jenkins
var jenkinsfile = engine.RenderForJenkins(template, analysis);
```

## Тестирование

Запуск тестов маппера:

```bash
dotnet test --filter "FullyQualifiedName~VariableMapperTests"
```

Проверка сгенерированных файлов:

```bash
dotnet run -- --repo https://github.com/user/repo --output /tmp/test
cat /tmp/test/.gitlab-ci.yml
cat /tmp/test/Jenkinsfile
```

