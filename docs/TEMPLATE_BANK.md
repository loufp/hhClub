# Банк шаблонов CI/CD

Полный набор шаблонов для 4 базовых языков с поддержкой фреймворков и оптимизированным кэшированием.

## Структура банка шаблонов

```
templates/
├── gitlab/
│   ├── base.yml                        # Универсальный шаблон
│   ├── java-spring-gradle.yml          # Java/Kotlin + Spring Boot + Gradle
│   ├── java-spring-maven.yml           # Java/Kotlin + Spring Boot + Maven
│   ├── go-modules.yml                  # Go с модулями и CGO
│   ├── nodejs-npm.yml                  # Node.js/TypeScript + npm
│   ├── nodejs-pnpm-monorepo.yml        # Node.js/TypeScript + pnpm + monorepo
│   ├── python-django-poetry.yml        # Python + Django + Poetry
│   └── python-fastapi-pip.yml          # Python + FastAPI/Flask + pip
└── jenkins/
    └── base.Jenkinsfile                # Универсальный Jenkinsfile
```

## Java/Kotlin шаблоны

### java-spring-gradle.yml
**Для:** Spring Boot приложений с Gradle  
**Кэширование:**
- `.gradle/wrapper` — Gradle wrapper
- `.gradle/caches` — зависимости и build cache

**Особенности:**
- Параллельная сборка (`--parallel`)
- Build cache (`--build-cache`)
- SonarQube интеграция
- Spring Boot JAR packaging

**Пример использования:**
```yaml
cache:
  key: ${CI_COMMIT_REF_SLUG}
  paths:
    - .gradle/wrapper
    - .gradle/caches
```

### java-spring-maven.yml
**Для:** Spring Boot приложений с Maven  
**Кэширование:**
- `.m2/repository` — Maven зависимости

**Особенности:**
- Batch mode (`--batch-mode`)
- Fail-at-end стратегия
- Maven CLI optimization
- JUnit отчёты

**Пример использования:**
```yaml
variables:
  MAVEN_OPTS: "-Dmaven.repo.local=$CI_PROJECT_DIR/.m2/repository"
```

## Go шаблоны

### go-modules.yml
**Для:** Go приложений с модулями  
**Кэширование:**
- `.go/pkg/mod` — Go модули
- `.cache/go-build` — Build cache

**Особенности:**
- CGO support (опциональный job)
- Coverage отчёты
- GOPATH и GOCACHE настройка
- Docker BuildKit

**CGO вариант:**
```yaml
build_cgo:
  variables:
    CGO_ENABLED: "1"
  script:
    - apt-get install -y gcc g++ libc6-dev
    - go build -v -o app-cgo
```

## Node.js/TypeScript шаблоны

### nodejs-npm.yml
**Для:** Стандартных Node.js/React/Next.js проектов  
**Кэширование:**
- `.npm` — npm cache
- `node_modules` — зависимости
- `.next/cache` — Next.js build cache

**Особенности:**
- `npm ci` для детерминированных установок
- Coverage reporting
- Cypress cache support
- ESLint и TypeScript проверки

### nodejs-pnpm-monorepo.yml
**Для:** Monorepo проектов (Nx, Turborepo, Lerna)  
**Кэширование:**
- `.pnpm-store` — pnpm store
- `node_modules` — зависимости

**Особенности:**
- Матричные сборки для workspace'ов
- `pnpm --filter` для выборочной сборки
- Поддержка pnpm-workspace.yaml
- Frozen lockfile

**Матричная сборка:**
```yaml
build_workspace:
  parallel:
    matrix:
      - WORKSPACE: [app, api, ui, shared]
  script:
    - pnpm --filter $WORKSPACE run build
```

## Python шаблоны

### python-django-poetry.yml
**Для:** Django проектов с Poetry  
**Кэширование:**
- `.cache/pip` — pip cache
- `.cache/pypoetry` — Poetry cache
- `.venv` — виртуальное окружение

**Особенности:**
- Poetry для управления зависимостями
- Django migrations проверка
- mypy, flake8, black линтеры
- Coverage с pytest

**Проверка миграций:**
```yaml
migrate:
  script:
    - poetry run python manage.py makemigrations --check --dry-run
  only:
    changes:
      - "*/models.py"
```

### python-fastapi-pip.yml
**Для:** FastAPI/Flask с pip  
**Кэширование:**
- `.cache/pip` — pip cache
- `venv` — виртуальное окружение

**Особенности:**
- pip с wheel оптимизацией
- pytest + coverage
- Быстрая установка зависимостей
- Минимальные requirements

## Автоматический выбор шаблона

Система автоматически выбирает правильный шаблон на основе:

### 1. Язык программирования
Определяется через `AnalyzerService` по файлам проекта.

### 2. Фреймворк
Обнаруживается в зависимостях и конфигурационных файлах.

### 3. Система сборки
Проверяется наличие:
- `build.gradle` / `pom.xml`
- `go.mod`
- `package.json` / `pnpm-workspace.yaml`
- `requirements.txt` / `pyproject.toml`

### Логика выбора:

```csharp
Java + Spring + Gradle → java-spring-gradle.yml
Java + Spring + Maven  → java-spring-maven.yml
Go                     → go-modules.yml
Node.js + pnpm         → nodejs-pnpm-monorepo.yml
Node.js + npm          → nodejs-npm.yml
Python + Django        → python-django-poetry.yml
Python + FastAPI       → python-fastapi-pip.yml
```

## Стратегии кэширования

### Cache key стратегии:

**По ветке (default):**
```yaml
cache:
  key: ${CI_COMMIT_REF_SLUG}
```

**По lock-файлу:**
```yaml
cache:
  key:
    files:
      - package-lock.json
```

**Глобальный:**
```yaml
cache:
  key: global
```

### Оптимизация кэша:

1. **Минимизация размера:** Кэшировать только необходимое
2. **Правильный key:** Использовать lock-файлы для детерминизма
3. **Очистка:** Периодическая чистка старых кэшей
4. **Compression:** Автоматическая компрессия GitLab CI

## Переменные окружения

Все шаблоны поддерживают универсальные переменные через `VariableMapper`:

```yaml
{{CI_REGISTRY}}          → $CI_REGISTRY
{{CI_PROJECT_PATH}}      → $CI_PROJECT_PATH
{{CI_COMMIT_SHORT_SHA}}  → $CI_COMMIT_SHORT_SHA
{{BUILD_NUMBER}}         → $CI_PIPELINE_IID
```

## Добавление нового шаблона

1. Создайте файл в `templates/gitlab/` или `templates/jenkins/`
2. Используйте универсальные переменные `{{VARIABLE}}`
3. Добавьте логику выбора в `TemplateService.SelectGitLabTemplate()`
4. Протестируйте с реальным проектом

**Пример:**

```yaml
# templates/gitlab/rust-cargo.yml
image: rust:1.74

cache:
  key: ${CI_COMMIT_REF_SLUG}
  paths:
    - .cargo
    - target

stages:
  - build
  - test

build:
  script:
    - cargo build --release
```

```csharp
// В SelectGitLabTemplate():
if (lang == "rust")
    return "rust-cargo.yml";
```

## Тестирование шаблонов

```bash
# Генерация для конкретного проекта
dotnet run --project Ci_Cd/Ci_Cd.csproj -- \
  --repo https://github.com/user/spring-boot-app \
  --output /tmp/test

# Проверка выбранного шаблона
cat /tmp/test/.gitlab-ci.yml | head -5

# Проверка кэширования
grep -A5 "cache:" /tmp/test/.gitlab-ci.yml
```

## Best Practices

✅ **DO:**
- Кэшируйте зависимости, а не артефакты сборки
- Используйте lock-файлы для cache key
- Добавляйте expire_in для artifacts
- Параллелизируйте независимые job'ы
- Используйте матричные сборки для monorepo

❌ **DON'T:**
- Не кэшируйте node_modules на разных ОС
- Не кэшируйте vendor в Docker образах
- Не используйте глобальный ключ для всех проектов
- Не забывайте очищать старые кэши

## Метрики производительности

После внедрения кэширования ожидаемый прирост:

| Язык/Стек | Без кэша | С кэшем | Ускорение |
|-----------|----------|---------|-----------|
| Java/Gradle | 5-8 мин | 2-3 мин | **2.5x** |
| Java/Maven | 4-6 мин | 1.5-2 мин | **3x** |
| Go modules | 2-4 мин | 30-60 сек | **4x** |
| Node.js/npm | 3-5 мин | 1-2 мин | **2.5x** |
| Node.js/pnpm | 2-3 мин | 30-45 сек | **4x** |
| Python/pip | 2-3 мин | 30-60 сек | **3x** |
| Python/poetry | 3-4 мин | 1-1.5 мин | **3x** |

## Поддержка

Для вопросов и предложений по шаблонам см. [VARIABLE_MAPPING.md](./VARIABLE_MAPPING.md)

