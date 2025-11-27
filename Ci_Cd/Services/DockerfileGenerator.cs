using System.Text;
using Ci_Cd.Models;

namespace Ci_Cd.Services
{
    public interface IDockerfileGenerator
    {
        string GenerateDockerfile(RepoAnalysisResult analysis);
        string GenerateDockerignore(RepoAnalysisResult analysis);
    }

    public class DockerfileGenerator : IDockerfileGenerator
    {
        public string GenerateDockerfile(RepoAnalysisResult analysis)
        {
            return analysis.Language switch
            {
                RepoAnalysisResult.ProjectLanguage.Java => GenerateJavaDockerfile(analysis),
                RepoAnalysisResult.ProjectLanguage.Kotlin => GenerateKotlinDockerfile(analysis),
                RepoAnalysisResult.ProjectLanguage.NodeJs => GenerateNodeDockerfile(analysis),
                RepoAnalysisResult.ProjectLanguage.Python => GeneratePythonDockerfile(analysis),
                RepoAnalysisResult.ProjectLanguage.Go => GenerateGoDockerfile(analysis),
                _ => GenerateGenericDockerfile(analysis)
            };
        }

        private string GenerateJavaDockerfile(RepoAnalysisResult analysis)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("FROM " + (analysis.BuildTools.Contains("Maven") 
                ? $"maven:3.9-eclipse-temurin-{analysis.LanguageVersion ?? "17"}"
                : $"gradle:8.5-jdk{analysis.LanguageVersion ?? "17"}") + " AS builder");
            sb.AppendLine("WORKDIR /build");
            sb.AppendLine();
            
            if (analysis.BuildTools.Contains("Maven"))
            {
                sb.AppendLine("COPY pom.xml .");
                sb.AppendLine("RUN --mount=type=cache,target=/root/.m2 \\");
                sb.AppendLine("    mvn -B -DskipTests=true dependency:go-offline");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("COPY build.gradle* settings.gradle* ./");
                sb.AppendLine("RUN --mount=type=cache,target=/root/.gradle \\");
                sb.AppendLine("    ./gradlew dependencies --no-daemon");
                sb.AppendLine();
            }
            
            sb.AppendLine("COPY src ./src");
            if (!analysis.BuildTools.Contains("Maven"))
            {
                sb.AppendLine("COPY gradle ./gradle");
                sb.AppendLine("COPY gradlew* ./");
            }
            sb.AppendLine();
            
            if (analysis.BuildTools.Contains("Maven"))
            {
                sb.AppendLine("RUN --mount=type=cache,target=/root/.m2 \\");
                sb.AppendLine("    mvn -B -DskipTests=true clean package");
            }
            else
            {
                sb.AppendLine("RUN --mount=type=cache,target=/root/.gradle \\");
                sb.AppendLine("    ./gradlew build -x test --no-daemon");
            }
            sb.AppendLine();
            
            sb.AppendLine($"FROM eclipse-temurin:{analysis.LanguageVersion ?? "17"}-jre-focal");
            sb.AppendLine("WORKDIR /app");
            sb.AppendLine();
            
            sb.AppendLine("RUN groupadd -r appuser && useradd -r -g appuser appuser");
            sb.AppendLine();
            
            sb.AppendLine("COPY --from=builder /build/target/*.jar /app/app.jar");
            sb.AppendLine("COPY --from=builder /build/build/libs/*.jar /app/app.jar");
            sb.AppendLine("COPY --chown=appuser:appuser . /app/");
            sb.AppendLine();
            
            if (analysis.DetectedPorts.Any())
            {
                var firstPort = analysis.DetectedPorts.Keys.First();
                sb.AppendLine($"HEALTHCHECK --interval=30s --timeout=3s --start-period=40s --retries=3 \\");
                sb.AppendLine($"  CMD curl -f http://localhost:{firstPort}/actuator/health || exit 1");
                sb.AppendLine();
            }
            
            sb.AppendLine("ENV JAVA_OPTS=\"-XX:+UseG1GC -XX:MaxGCPauseMillis=200 -XX:+UnlockExperimentalVMOptions -XX:G1NewCollectionHeuristicPercent=35\"");
            sb.AppendLine();
            
            foreach (var port in analysis.DetectedPorts)
            {
                sb.AppendLine($"EXPOSE {port.Key}");
            }
            if (!analysis.DetectedPorts.Any())
            {
                sb.AppendLine("EXPOSE 8080");
            }
            sb.AppendLine();
            
            sb.AppendLine("USER appuser");
            sb.AppendLine();
            
            sb.AppendLine("ENTRYPOINT [\"java\", \"${JAVA_OPTS}\", \"-jar\", \"app.jar\"]");
            
            return sb.ToString();
        }

        private string GenerateKotlinDockerfile(RepoAnalysisResult analysis)
        {
            var sb = new StringBuilder();
            
            var javaVersion = analysis.LanguageVersion ?? "17";
            sb.AppendLine($"FROM gradle:8.5-jdk{javaVersion} AS builder");
            sb.AppendLine("WORKDIR /build");
            sb.AppendLine();
            
            sb.AppendLine("COPY build.gradle* settings.gradle* ./");
            sb.AppendLine("COPY gradle.properties* ./");
            sb.AppendLine("RUN --mount=type=cache,target=/root/.gradle \\");
            sb.AppendLine("    gradle dependencies --no-daemon");
            sb.AppendLine();
            
            sb.AppendLine("COPY src ./src");
            sb.AppendLine("COPY gradle ./gradle");
            sb.AppendLine("COPY gradlew* ./");
            sb.AppendLine();
            
            sb.AppendLine("RUN --mount=type=cache,target=/root/.gradle \\");
            sb.AppendLine("    --mount=type=cache,target=/root/.konan \\");
            sb.AppendLine("    chmod +x gradlew && ./gradlew build -x test --no-daemon");
            sb.AppendLine();
            
            sb.AppendLine($"FROM eclipse-temurin:{javaVersion}-jre-focal");
            sb.AppendLine("WORKDIR /app");
            sb.AppendLine();
            
            sb.AppendLine("RUN groupadd -r appuser && useradd -r -g appuser appuser");
            sb.AppendLine();
            
            sb.AppendLine("COPY --from=builder /build/build/libs/*.jar /app/app.jar");
            sb.AppendLine("COPY --chown=appuser:appuser . /app/");
            sb.AppendLine();
            
            if (analysis.DetectedPorts.Any())
            {
                var firstPort = analysis.DetectedPorts.Keys.First();
                sb.AppendLine($"HEALTHCHECK --interval=30s --timeout=3s --start-period=40s --retries=3 \\");
                sb.AppendLine($"  CMD curl -f http://localhost:{firstPort}/actuator/health || exit 1");
                sb.AppendLine();
            }
            
            sb.AppendLine("ENV JAVA_OPTS=\"-XX:+UseG1GC -XX:MaxGCPauseMillis=200\"");
            sb.AppendLine("ENV KOTLIN_OPTS=\"-Xmx512m\"");
            sb.AppendLine();
            
            foreach (var port in analysis.DetectedPorts)
            {
                sb.AppendLine($"EXPOSE {port.Key}");
            }
            if (!analysis.DetectedPorts.Any())
            {
                sb.AppendLine("EXPOSE 8080");
            }
            sb.AppendLine();
            
            sb.AppendLine("USER appuser");
            sb.AppendLine();
            
            sb.AppendLine("ENTRYPOINT [\"java\", \"${JAVA_OPTS}\", \"${KOTLIN_OPTS}\", \"-jar\", \"app.jar\"]");
            
            return sb.ToString();
        }

        private string GenerateNodeDockerfile(RepoAnalysisResult analysis)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine();
            
            var nodeVersion = ExtractNodeVersion(analysis.LanguageVersion);
            
            sb.AppendLine($"# Stage 1: Dependencies");
            sb.AppendLine($"FROM node:{nodeVersion}-alpine AS dependencies");
            sb.AppendLine("WORKDIR /app");
            sb.AppendLine("COPY package*.json ./");
            sb.AppendLine("RUN --mount=type=cache,target=/root/.npm \\");
            sb.AppendLine("    npm ci --only=production");
            sb.AppendLine();
            
            sb.AppendLine($"# Stage 2: Builder");
            sb.AppendLine($"FROM node:{nodeVersion}-alpine AS builder");
            sb.AppendLine("WORKDIR /app");
            sb.AppendLine("COPY package*.json ./");
            sb.AppendLine("RUN --mount=type=cache,target=/root/.npm \\");
            sb.AppendLine("    npm ci");
            sb.AppendLine("COPY . .");
            sb.AppendLine("RUN npm run build || true");
            sb.AppendLine();
            
            sb.AppendLine($"FROM node:{nodeVersion}-alpine");
            sb.AppendLine("WORKDIR /app");
            sb.AppendLine();
            
            sb.AppendLine("RUN addgroup -g 1001 -S nodejs && adduser -S nodejs -u 1001");
            sb.AppendLine();
            
            sb.AppendLine("COPY --from=dependencies /app/node_modules /app/node_modules");
            sb.AppendLine("COPY --from=builder /app/dist /app/dist");
            sb.AppendLine("COPY --from=builder /app/package*.json /app/");
            sb.AppendLine("COPY --chown=nodejs:nodejs . /app/");
            sb.AppendLine();
            
            if (analysis.DetectedPorts.Any())
            {
                var firstPort = analysis.DetectedPorts.Keys.First();
                sb.AppendLine($"HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \\");
                sb.AppendLine($"  CMD wget --no-verbose --tries=1 --spider http://localhost:{firstPort}/health || exit 1");
                sb.AppendLine();
            }
            
            foreach (var port in analysis.DetectedPorts)
            {
                sb.AppendLine($"EXPOSE {port.Key}");
            }
            if (!analysis.DetectedPorts.Any())
            {
                sb.AppendLine("EXPOSE 3000");
            }
            sb.AppendLine();
            
            sb.AppendLine("USER nodejs");
            sb.AppendLine("CMD [\"node\", \"dist/index.js\"]");
            
            return sb.ToString();
        }

        private string GeneratePythonDockerfile(RepoAnalysisResult analysis)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine();
            
            var pythonVersion = ExtractPythonVersion(analysis.LanguageVersion);
            var isPoetry = analysis.BuildTools.Contains("Poetry");
            
            sb.AppendLine($"# Stage 1: Builder");
            sb.AppendLine($"FROM python:{pythonVersion}-slim AS builder");
            sb.AppendLine("WORKDIR /build");
            sb.AppendLine();
            
            if (isPoetry)
            {
                sb.AppendLine("RUN --mount=type=cache,target=/root/.cache/pip \\");
                sb.AppendLine("    pip install --no-cache-dir poetry");
                sb.AppendLine();
                sb.AppendLine("COPY pyproject.toml poetry.lock ./");
                sb.AppendLine("RUN poetry export -f requirements.txt | pip wheel --no-cache-dir --wheel-dir /wheels -r /dev/stdin");
            }
            else
            {
                sb.AppendLine("COPY requirements.txt .");
                sb.AppendLine("RUN --mount=type=cache,target=/root/.cache/pip \\");
                sb.AppendLine("    pip wheel --no-cache-dir --wheel-dir /wheels -r requirements.txt");
            }
            sb.AppendLine();
            
            sb.AppendLine($"FROM python:{pythonVersion}-slim");
            sb.AppendLine("WORKDIR /app");
            sb.AppendLine();
            
            sb.AppendLine("RUN groupadd -r appuser && useradd -r -g appuser appuser");
            sb.AppendLine();
            
            sb.AppendLine("COPY --from=builder /wheels /wheels");
            if (isPoetry)
            {
                sb.AppendLine("COPY pyproject.toml poetry.lock ./");
            }
            else
            {
                sb.AppendLine("COPY requirements.txt .");
            }
            sb.AppendLine();
            
            sb.AppendLine("RUN --mount=type=cache,from=builder,source=/wheels,target=/wheels \\");
            sb.AppendLine("    pip install --no-cache /wheels/*");
            sb.AppendLine();
            
            sb.AppendLine("COPY --chown=appuser:appuser . /app/");
            sb.AppendLine();
            
            if (analysis.DetectedPorts.Any())
            {
                var firstPort = analysis.DetectedPorts.Keys.First();
                sb.AppendLine($"HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \\");
                sb.AppendLine($"  CMD python -c \"import requests; requests.get('http://localhost:{firstPort}/health')\" || exit 1");
                sb.AppendLine();
            }
            
            foreach (var port in analysis.DetectedPorts)
            {
                sb.AppendLine($"EXPOSE {port.Key}");
            }
            if (!analysis.DetectedPorts.Any())
            {
                sb.AppendLine("EXPOSE 8000");
            }
            sb.AppendLine();
            
            sb.AppendLine("USER appuser");
            sb.AppendLine("CMD [\"gunicorn\", \"-b\", \"0.0.0.0:8000\", \"app.wsgi:application\"]");
            
            return sb.ToString();
        }

        private string GenerateGoDockerfile(RepoAnalysisResult analysis)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine();
            
            var goVersion = string.IsNullOrEmpty(analysis.LanguageVersion) ? "1.21" : analysis.LanguageVersion;
            
            sb.AppendLine($"# Stage 1: Builder");
            sb.AppendLine($"FROM golang:{goVersion} AS builder");
            sb.AppendLine("WORKDIR /build");
            sb.AppendLine();
            
            sb.AppendLine("COPY go.mod go.sum ./");
            sb.AppendLine("RUN --mount=type=cache,target=/go/pkg/mod \\");
            sb.AppendLine("    go mod download");
            sb.AppendLine();
            
            sb.AppendLine("COPY . .");
            sb.AppendLine("RUN --mount=type=cache,target=/root/.cache/go-build \\");
            sb.AppendLine("    CGO_ENABLED=0 GOOS=linux GOARCH=amd64 go build -ldflags='-w -s' -o /build/app ./...");
            sb.AppendLine();
            
            sb.AppendLine($"# Stage 2: Runtime (distroless for minimal size)");
            sb.AppendLine($"FROM gcr.io/distroless/base-debian11:nonroot");
            sb.AppendLine("WORKDIR /app");
            sb.AppendLine();
            
            sb.AppendLine("COPY --from=builder /build/app /app/app");
            sb.AppendLine();
            
            // EXPOSE ports
            foreach (var port in analysis.DetectedPorts)
            {
                sb.AppendLine($"EXPOSE {port.Key}");
            }
            if (!analysis.DetectedPorts.Any())
            {
                sb.AppendLine("EXPOSE 8080");
            }
            sb.AppendLine();
            
            // Distroless already runs as nonroot
            sb.AppendLine("ENTRYPOINT [\"/app/app\"]");
            
            return sb.ToString();
        }

        private string GenerateGenericDockerfile(RepoAnalysisResult analysis)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine($"FROM {analysis.GetBestImage()}");
            sb.AppendLine("WORKDIR /app");
            sb.AppendLine();
            
            sb.AppendLine("RUN groupadd -r appuser && useradd -r -g appuser appuser");
            sb.AppendLine();
            
            sb.AppendLine("COPY . /app/");
            sb.AppendLine("RUN chown -R appuser:appuser /app");
            sb.AppendLine();
            
            foreach (var port in analysis.DetectedPorts)
            {
                sb.AppendLine($"EXPOSE {port.Key}");
            }
            
            sb.AppendLine();
            sb.AppendLine("USER appuser");
            sb.AppendLine("CMD [\"/bin/sh\", \"-c\", \"echo 'No CMD specified'\"]");
            
            return sb.ToString();
        }

        public string GenerateDockerignore(RepoAnalysisResult analysis)
        {
            var sb = new StringBuilder();
            
            // Common ignores
            sb.AppendLine(".git/");
            sb.AppendLine(".gitignore");
            sb.AppendLine(".gitattributes");
            sb.AppendLine();
            
            sb.AppendLine(".github/");
            sb.AppendLine(".gitlab-ci.yml");
            sb.AppendLine("Jenkinsfile");
            sb.AppendLine(".circleci/");
            sb.AppendLine();
            
            sb.AppendLine("docs/");
            sb.AppendLine("README*");
            sb.AppendLine("*.md");
            sb.AppendLine();
            
            sb.AppendLine(".test/");
            sb.AppendLine("coverage/");
            sb.AppendLine("htmlcov/");
            sb.AppendLine("*.coverage");
            sb.AppendLine();
            
            sb.AppendLine(".vscode/");
            sb.AppendLine(".idea/");
            sb.AppendLine("*.swp");
            sb.AppendLine("*.swo");
            sb.AppendLine();
            
            // Language-specific ignores
            switch (analysis.Language)
            {
                case RepoAnalysisResult.ProjectLanguage.Java:
                    sb.AppendLine("target/");
                    sb.AppendLine("build/");
                    sb.AppendLine("*.class");
                    sb.AppendLine("*.jar");
                    sb.AppendLine(".gradle/");
                    sb.AppendLine("gradle-wrapper.jar");
                    break;
                    
                case RepoAnalysisResult.ProjectLanguage.Kotlin:
                    sb.AppendLine("build/");
                    sb.AppendLine("*.class");
                    sb.AppendLine("*.jar");
                    sb.AppendLine(".gradle/");
                    sb.AppendLine("gradle-wrapper.jar");
                    sb.AppendLine(".konan/");
                    break;
                    
                case RepoAnalysisResult.ProjectLanguage.NodeJs:
                    sb.AppendLine("node_modules/");
                    sb.AppendLine("npm-debug.log");
                    sb.AppendLine("yarn-error.log");
                    sb.AppendLine(".pnpm-store/");
                    sb.AppendLine("dist/");
                    sb.AppendLine(".next/");
                    break;
                    
                case RepoAnalysisResult.ProjectLanguage.Python:
                    sb.AppendLine("__pycache__/");
                    sb.AppendLine("*.py[cod]");
                    sb.AppendLine("*.egg-info/");
                    sb.AppendLine("dist/");
                    sb.AppendLine("build/");
                    sb.AppendLine(".venv/");
                    sb.AppendLine("venv/");
                    sb.AppendLine(".env");
                    break;
                    
                case RepoAnalysisResult.ProjectLanguage.Go:
                    sb.AppendLine("vendor/");
                    sb.AppendLine("*.exe");
                    sb.AppendLine("bin/");
                    break;
            }
            
            sb.AppendLine();
            sb.AppendLine("Dockerfile*");
            sb.AppendLine(".dockerignore");
            sb.AppendLine("docker-compose*.yml");
            sb.AppendLine();
            
            sb.AppendLine(".DS_Store");
            sb.AppendLine("Thumbs.db");
            sb.AppendLine("*.tmp");
            
            return sb.ToString();
        }

        private string ExtractNodeVersion(string engineVersion)
        {
            if (string.IsNullOrEmpty(engineVersion)) return "20-alpine";
            
            var version = engineVersion.Replace(">=", "").Replace("^", "").Replace("~", "").Trim();
            var major = version.Split('.')[0];
            
            return major switch
            {
                "16" => "16-alpine",
                "17" => "17-alpine",
                "18" => "18-alpine",
                "19" => "19-alpine",
                "20" => "20-alpine",
                "21" => "21-alpine",
                _ => "20-alpine"
            };
        }

        private string ExtractPythonVersion(string versionSpec)
        {
            if (string.IsNullOrEmpty(versionSpec)) return "3.11-slim";
            
            var version = versionSpec.Replace(">=", "").Replace("^", "").Replace("~", "").Trim();
            var parts = version.Split('.');
            
            if (parts.Length >= 2)
                return $"{parts[0]}.{parts[1]}-slim";
            
            return "3.11-slim";
        }
    }
}

