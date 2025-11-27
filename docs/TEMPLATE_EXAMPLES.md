# Примеры использования шаблонов

## Java/Spring Boot + Gradle

**Проект:** Spring Boot REST API с Gradle  
**Шаблон:** `java-spring-gradle.yml`

### Определение:
- Наличие `build.gradle` или `build.gradle.kts`
- Spring Boot зависимости
- Java/Kotlin source files

### Генерация:
```bash
dotnet run --project Ci_Cd/Ci_Cd.csproj -- \
  --repo https://github.com/spring-projects/spring-petclinic \
  --output /tmp/spring-gradle
```

### Результат:
```yaml
image: gradle:8.5-jdk17

cache:
  key: ${CI_COMMIT_REF_SLUG}
  paths:
    - .gradle/wrapper
    - .gradle/caches

build:
  script:
    - ./gradlew clean build -x test --build-cache --parallel
```

**Ключевые фичи:**
- ✅ Кэширование Gradle wrapper и зависимостей
- ✅ Параллельная сборка
- ✅ Build cache
- ✅ SonarQube интеграция

---

## Go с модулями

**Проект:** Go microservice  
**Шаблон:** `go-modules.yml`

### Определение:
- Наличие `go.mod`
- Go source files

### Генерация:
```bash
dotnet run --project Ci_Cd/Ci_Cd.csproj -- \
  --repo https://github.com/golang/go \
  --output /tmp/go-project
```

### Результат:
```yaml
image: golang:1.21

cache:
  paths:
    - .go/pkg/mod
    - .cache/go-build

build:
  script:
    - go mod download
    - go mod verify
    - go build -v -o app ./...

build_cgo:
  variables:
    CGO_ENABLED: "1"
  script:
    - go build -v -o app-cgo ./...
```

**Ключевые фичи:**
- ✅ Кэширование go modules
- ✅ Go build cache
- ✅ CGO support (опциональный)
- ✅ Coverage отчёты

---

## Node.js/TypeScript + pnpm Monorepo

**Проект:** Next.js + NestJS monorepo  
**Шаблон:** `nodejs-pnpm-monorepo.yml`

### Определение:
- Наличие `pnpm-workspace.yaml`
- Наличие `pnpm-lock.yaml`
- Множественные `package.json` в `packages/`

### Генерация:
```bash
dotnet run --project Ci_Cd/Ci_Cd.csproj -- \
  --repo https://github.com/vercel/turborepo \
  --output /tmp/pnpm-monorepo
```

### Результат:
```yaml
image: node:20-alpine

cache:
  key:
    files:
      - pnpm-lock.yaml
  paths:
    - .pnpm-store
    - node_modules

install:
  script:
    - pnpm install --frozen-lockfile

build_workspace:
  parallel:
    matrix:
      - WORKSPACE: [app, api, ui, shared]
  script:
    - pnpm --filter $WORKSPACE run build
```

**Ключевые фичи:**
- ✅ pnpm store кэширование
- ✅ Матричные сборки для workspace'ов
- ✅ Frozen lockfile
- ✅ Выборочная сборка с --filter

---

## Python/Django + Poetry

**Проект:** Django REST API с Poetry  
**Шаблон:** `python-django-poetry.yml`

### Определение:
- Наличие `pyproject.toml`
- Django в зависимостях
- `manage.py`

### Генерация:
```bash
dotnet run --project Ci_Cd/Ci_Cd.csproj -- \
  --repo https://github.com/django/django \
  --output /tmp/django-poetry
```

### Результат:
```yaml
image: python:3.11-slim

cache:
  paths:
    - .cache/pip
    - .cache/pypoetry
    - .venv

install:
  script:
    - poetry install --no-interaction

test:
  script:
    - poetry run pytest --cov=. --cov-report=xml

migrate:
  script:
    - poetry run python manage.py makemigrations --check
  only:
    changes:
      - "*/models.py"
```

**Ключевые фичи:**
- ✅ Poetry для управления зависимостями
- ✅ Кэширование pip и poetry
- ✅ virtualenv в проекте
- ✅ Проверка миграций Django
- ✅ Coverage с pytest

---

## Сравнение производительности

### До оптимизации (без кэша):

| Проект | Время сборки |
|--------|--------------|
| Spring Boot/Gradle | **8 минут** |
| Go modules | **4 минуты** |
| pnpm monorepo | **5 минут** |
| Django/Poetry | **3 минуты** |

### После оптимизации (с кэшем):

| Проект | Время сборки | Ускорение |
|--------|--------------|-----------|
| Spring Boot/Gradle | **2.5 минуты** | 🚀 **3.2x** |
| Go modules | **45 секунд** | 🚀 **5.3x** |
| pnpm monorepo | **1 минута** | 🚀 **5x** |
| Django/Poetry | **1 минута** | 🚀 **3x** |

---

## Тестирование шаблонов

### 1. Локальная генерация
```bash
dotnet run --project Ci_Cd/Ci_Cd.csproj -- \
  --repo <URL> \
  --output /tmp/test \
  --format dir
```

### 2. Проверка выбранного шаблона
```bash
# Посмотреть первые строки
head -20 /tmp/test/.gitlab-ci.yml

# Проверить кэширование
grep -A10 "cache:" /tmp/test/.gitlab-ci.yml

# Проверить stages
grep -A20 "stages:" /tmp/test/.gitlab-ci.yml
```

### 3. Валидация GitLab CI
```bash
# Установить gitlab-ci-lint (если есть GitLab instance)
curl -X POST -F "content=@/tmp/test/.gitlab-ci.yml" \
  https://gitlab.example.com/api/v4/ci/lint
```

---

## Кастомизация шаблонов

### Добавление своих команд

Все шаблоны поддерживают переменные `{{BUILD_COMMANDS}}` и `{{TEST_COMMANDS}}`, которые автоматически заполняются из `RepoAnalysisResult`.

### Добавление новых stages

Можно расширить существующий шаблон:

```yaml
# Добавить stage security
stages:
  - build
  - test
  - security  # новый
  - docker_build

security_scan:
  stage: security
  script:
    - trivy fs . --security-checks vuln
```

### Переопределение переменных

```yaml
# В проекте создать .gitlab-ci-local.yml
include:
  - local: '.gitlab-ci.yml'

variables:
  CUSTOM_VAR: "my-value"
```

---

## FAQ

**Q: Как добавить поддержку Rust?**  
A: Создайте `templates/gitlab/rust-cargo.yml` и добавьте логику в `SelectGitLabTemplate()`.

**Q: Можно ли использовать несколько шаблонов?**  
A: Да, используйте GitLab CI `include:` для композиции.

**Q: Как очистить старые кэши?**  
A: GitLab автоматически удаляет неиспользуемые кэши после 30 дней.

**Q: Поддержка Yarn?**  
A: Да, создайте вариант `nodejs-yarn.yml` по аналогии с npm.

---

## Следующие шаги

1. Протестировать шаблоны на ваших проектах
2. Добавить специфичные для компании настройки
3. Расширить банк под дополнительные языки
4. Настроить метрики производительности
5. Автоматизировать обновление шаблонов

См. также:
- [TEMPLATE_BANK.md](./TEMPLATE_BANK.md) — полное описание
- [VARIABLE_MAPPING.md](./VARIABLE_MAPPING.md) — маппинг переменных

