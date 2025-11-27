using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Ci_Cd.Models;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ci_Cd.Services
{
    public class AnalyzerService : IAnalyzerService
    {
        private readonly ILogger<AnalyzerService> _logger;
        private readonly List<DetectorRule> _rules;

        public AnalyzerService(ILogger<AnalyzerService> logger)
        {
            _logger = logger;
            var configPath = Path.Combine(AppContext.BaseDirectory, "detectors.json");
            try
            {
                if (File.Exists(configPath))
                {
                    var configJson = File.ReadAllText(configPath);
                    _rules = JsonSerializer.Deserialize<DetectorConfig>(configJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Frameworks ?? new List<DetectorRule>();
                }
                else
                {
                    _logger.LogWarning("detectors.json not found at {path}, using built-in fallback rules", configPath);
                    _rules = GetBuiltInRules();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read detectors.json, using built-in fallback rules");
                _rules = GetBuiltInRules();
            }
        }

        public RepoAnalysisResult Analyze(string repoPath)
        {
            var result = new RepoAnalysisResult();
            var allFiles = Directory.GetFiles(repoPath, "*", SearchOption.AllDirectories).ToList();
            var fileNames = allFiles.Select(Path.GetFileName).ToList();

            DetectLanguageAndFramework(result, allFiles, fileNames);
            
            if (result.Language == RepoAnalysisResult.ProjectLanguage.Unknown)
            {
                _logger.LogWarning("Could not determine project language.");
                return result;
            }

            ExtractProjectDetails(result, repoPath, allFiles);
            
            // >>> Deep analysis calls
            ExtractVersionsAndFrameworks(result, allFiles);
            AnalyzeTestFrameworks(result, allFiles);
            AnalyzePortsAndHealthChecks(result, allFiles);
            AnalyzeEnvironmentVariables(result, allFiles);
            AnalyzeCachingStrategy(result);
            AnalyzeProjectQuality(result, repoPath);
            // <<< End of deep analysis calls

            FindDockerfile(result, repoPath, allFiles);

            if (!result.HasDockerfile)
            {
                GenerateDockerfile(result, repoPath);
            }

            return result;
        }

        private void DetectLanguageAndFramework(RepoAnalysisResult result, List<string> allFiles, List<string> fileNames)
        {
            var candidates = _rules
                .Select(rule => (Rule: rule, Score: CalculateRuleScore(rule, allFiles, fileNames)))
                .Where(x => x.Score >= x.Rule.Threshold)
                .OrderByDescending(x => x.Score)
                .ToList();

            // Debug: Log top candidates
            _logger.LogInformation("Top 5 rule candidates:");
            foreach (var (rule, score) in candidates.Take(5))
            {
                _logger.LogInformation($"  {rule.Name} ({rule.Language}): Score={score:F1}, Threshold={rule.Threshold}");
            }

            var bestMatch = candidates.FirstOrDefault();

            if (bestMatch.Rule != null)
            {
                result.Language = Enum.Parse<RepoAnalysisResult.ProjectLanguage>(bestMatch.Rule.Language, true);
                result.Framework = bestMatch.Rule.Name;
                result.Rationale = bestMatch.Rule.Rationale ?? string.Empty;
                result.BuildCommands.AddRange(bestMatch.Rule.BuildCommands);

                // Special Kotlin override check - if Java is detected but build.gradle has Kotlin
                if (result.Language == RepoAnalysisResult.ProjectLanguage.Java)
                {
                    var buildGradle = allFiles.FirstOrDefault(f => f.EndsWith("build.gradle"));
                    var hasKotlinFiles = allFiles.Any(f => f.EndsWith(".kt"));
                    
                    if (buildGradle != null || hasKotlinFiles)
                    {
                        try
                        {
                            var content = buildGradle != null ? File.ReadAllText(buildGradle) : "";
                            
                            if (content.Contains("apply plugin: 'kotlin'") ||
                                content.Contains("kotlin-gradle-plugin") ||
                                content.Contains("kotlin-stdlib") ||
                                hasKotlinFiles)
                            {
                                _logger.LogInformation("Overriding Java detection with Kotlin based on build.gradle content and .kt files");
                                result.Language = RepoAnalysisResult.ProjectLanguage.Kotlin;
                                result.Framework = "Kotlin";
                                result.Rationale = "Kotlin project detected via build.gradle analysis and .kt files";
                                
                                // Clear and re-populate build commands for Kotlin
                                result.BuildCommands.Clear();
                                result.BuildCommands.Add("./gradlew build");
                                result.TestCommands.Clear();
                                result.TestCommands.Add("./gradlew test");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to check build.gradle for Kotlin");
                        }
                    }
                }
                // Simple test command assignment
                // Framework-specific test commands
                var testCommand = bestMatch.Rule.Name switch {
                    // Java/Kotlin frameworks
                    "Spring Boot" when bestMatch.Rule.BuildCommands.Any(c => c.Contains("gradle")) => "gradle test",
                    "Spring Boot" => "mvn test",
                    "Spring Framework" when bestMatch.Rule.BuildCommands.Any(c => c.Contains("gradle")) => "gradle test",
                    "Spring Framework" => "mvn test",
                    "Quarkus" when bestMatch.Rule.BuildCommands.Any(c => c.Contains("gradle")) => "gradle test",
                    "Quarkus" => "mvn test",
                    "Micronaut" when bestMatch.Rule.BuildCommands.Any(c => c.Contains("gradle")) => "gradle test",
                    "Micronaut" => "mvn test",
                    "Vert.x" when bestMatch.Rule.BuildCommands.Any(c => c.Contains("gradle")) => "gradle test",
                    "Vert.x" => "mvn test",
                    "Java Maven" => "mvn test",
                    "Java Gradle" => "./gradlew test",
                    "Kotlin Spring Boot" when bestMatch.Rule.BuildCommands.Any(c => c.Contains("gradle")) => "./gradlew test",
                    "Kotlin Spring Boot" => "mvn test",
                    "Kotlin" => "./gradlew test",
                    "Kotlin Multiplatform" => "./gradlew test",
                    "Ktor" => "./gradlew test",
                    
                    // Go frameworks
                    "Gin Framework" => "go test ./...",
                    "Echo Framework" => "go test ./...",
                    "Fiber Framework" => "go test ./...",
                    "Gorilla Mux" => "go test ./...",
                    "Go Standard" => "go test ./...",
                    
                    // Node.js/TypeScript frameworks
                    "React" => "npm test",
                    "Next.js" => "npm test",
                    "Vue.js" => "npm test",
                    "Angular" => "ng test",
                    "Express.js" => "npm test",
                    "NestJS" => "npm run test",
                    "Fastify" => "npm test",
                    "TypeScript" => "npm test",
                    "Node.js Standard" => "npm test",
                    
                    // Python frameworks
                    "Django" => "python manage.py test",
                    "FastAPI" => "pytest",
                    "Flask" => "pytest",
                    "Tornado" => "pytest",
                    "Starlette" => "pytest",
                    "Python Poetry" => "poetry run pytest",
                    "Python Standard" => "pytest",
                    
                    // Default fallbacks by language
                    _ => result.Language switch {
                        RepoAnalysisResult.ProjectLanguage.DotNet => "dotnet test",
                        RepoAnalysisResult.ProjectLanguage.NodeJs => "npm test",
                        RepoAnalysisResult.ProjectLanguage.Python => "pytest",
                        RepoAnalysisResult.ProjectLanguage.Java => "mvn test",
                        RepoAnalysisResult.ProjectLanguage.Kotlin => "./gradlew test",
                        RepoAnalysisResult.ProjectLanguage.Go => "go test ./...",
                        RepoAnalysisResult.ProjectLanguage.Rust => "cargo test",
                        _ => "echo 'No test command configured'"
                    }
                };
                result.TestCommands.Add(testCommand);
                _logger.LogInformation("Detected Language: {Language}, Framework: {Framework}", result.Language, result.Framework);
            }
        }

        private double CalculateRuleScore(DetectorRule rule, List<string> allFiles, List<string> fileNames)
        {
            double score = 0;
            // Language triggers (file extensions, specific files)
            score += rule.LanguageDetectionTriggers.Count(trigger => fileNames.Any(f => f.EndsWith(trigger, StringComparison.OrdinalIgnoreCase)));
            
            // File patterns
            score += rule.FilePatterns.Count(pattern => fileNames.Any(f => f.Equals(pattern, StringComparison.OrdinalIgnoreCase))) * 2.0;

            // Content-based checks (more expensive)
            var relevantFiles = GetRelevantFilesForRules(rule, allFiles);
            foreach (var file in relevantFiles)
            {
                var content = File.ReadAllText(file);
                score += rule.DependencyNames.Count(dep => content.Contains(dep, StringComparison.OrdinalIgnoreCase)) * 3.0;
                score += rule.FileRegex.Count(rx => Regex.IsMatch(content, rx, RegexOptions.IgnoreCase)) * 2.5;
            }
            
            return score;
        }

        private void ExtractProjectDetails(RepoAnalysisResult result, string repoPath, List<string> allFiles)
        {
            switch (result.Language)
            {
                case RepoAnalysisResult.ProjectLanguage.DotNet:
                    var csproj = allFiles.FirstOrDefault(f => f.EndsWith(".csproj"));
                    if (csproj != null)
                    {
                        var doc = XDocument.Load(csproj);
                        result.ProjectName = Path.GetFileNameWithoutExtension(csproj);
                        result.ProjectVersion = doc.Descendants("Version").FirstOrDefault()?.Value ?? "1.0.0";
                        result.Dependencies.AddRange(doc.Descendants("PackageReference").Select(p => new Dependency { Name = p.Attribute("Include")?.Value ?? "", Version = p.Attribute("Version")?.Value ?? "" }));
                    }
                    break;
                // detect node monorepo workspaces
                case RepoAnalysisResult.ProjectLanguage.NodeJs:
                    var packageJsonPath = allFiles.FirstOrDefault(f => f.EndsWith("package.json"));
                    if (packageJsonPath != null)
                    {
                        var json = File.ReadAllText(packageJsonPath);
                        try
                        {
                            var doc = JsonDocument.Parse(json);
                            // detect workspaces
                            if (doc.RootElement.TryGetProperty("workspaces", out var ws))
                            {
                                // workspaces can be array or object with packages
                                if (ws.ValueKind == JsonValueKind.Array)
                                {
                                    var patterns = ws.EnumerateArray().Select(it => it.GetString()).Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList();
                                    var baseDir = Path.GetDirectoryName(packageJsonPath) ?? ".";
                                    var matches = ExpandWorkspacePatterns(baseDir, patterns);
                                    result.Subprojects.AddRange(matches.Where(d => !result.Subprojects.Contains(d)));
                                }
                                else if (ws.ValueKind == JsonValueKind.Object && ws.TryGetProperty("packages", out var pkgs))
                                {
                                    var patterns = pkgs.EnumerateArray().Select(it => it.GetString()).Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList();
                                    var baseDir = Path.GetDirectoryName(packageJsonPath) ?? ".";
                                    var matches = ExpandWorkspacePatterns(baseDir, patterns);
                                    result.Subprojects.AddRange(matches.Where(d => !result.Subprojects.Contains(d)));
                                }
                            }

                            // detect pnpm workspace file
                            var repoRoot = Path.GetDirectoryName(packageJsonPath) ?? ".";
                            var pnpmWorkspace = Path.Combine(repoRoot, "pnpm-workspace.yaml");
                            if (File.Exists(pnpmWorkspace))
                            {
                                try
                                {
                                    var patterns = ParsePnpmWorkspacePatterns(pnpmWorkspace);
                                    var matches = ExpandWorkspacePatterns(repoRoot, patterns);
                                    foreach (var d in matches) if (!result.Subprojects.Contains(d)) result.Subprojects.Add(d);
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                    break;
                // Gradle multi-module detection
                case RepoAnalysisResult.ProjectLanguage.Java:
                    var settings = allFiles.FirstOrDefault(f => Path.GetFileName(f).Equals("settings.gradle", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f).Equals("settings.gradle.kts", StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrEmpty(settings))
                    {
                        try
                        {
                            var txt = File.ReadAllText(settings);
                            // naive parse: include 'module:sub' or include '"sub"'
                            var matches = Regex.Matches(txt, @"include\s+(['""])(.+?)\1", RegexOptions.IgnoreCase);
                            foreach (Match m in matches)
                            {
                                var group = m.Groups[2].Value.Trim();
                                if (!string.IsNullOrEmpty(group))
                                {
                                    // map to directory names
                                    var baseDir = Path.GetDirectoryName(settings) ?? ".";
                                    var candidate = Path.Combine(baseDir, group.Replace(':', Path.DirectorySeparatorChar));
                                    if (Directory.Exists(candidate) && !result.Subprojects.Contains(candidate)) result.Subprojects.Add(candidate);
                                }
                            }

                        }
                        catch (Exception ex) { _logger.LogDebug(ex, "Failed parsing settings.gradle"); }
                    }
                    break;
                default:
                    break;
            }
        }

        private void FindDockerfile(RepoAnalysisResult result, string repoPath, List<string> allFiles)
        {
            var df = allFiles.FirstOrDefault(f => Path.GetFileName(f).Equals("Dockerfile", StringComparison.OrdinalIgnoreCase));
            if (df != null)
            {
                result.HasDockerfile = true;
                result.DockerfilePath = df;
                result.DockerfileGenerated = false;
            }
            else
            {
                result.HasDockerfile = false;
            }
        }

        private void GenerateDockerfile(RepoAnalysisResult result, string repoPath)
        {
            var docker = GetDockerfileTemplate(result);
            if (string.IsNullOrEmpty(docker)) return;
            var outFile = Path.Combine(repoPath, "Dockerfile.generated");
            File.WriteAllText(outFile, docker);
            result.DockerfileGenerated = true;
            result.DockerfilePath = outFile;
        }

        private string GetDockerfileTemplate(RepoAnalysisResult result)
        {
            var projectName = result.ProjectName;
            switch (result.Language)
            {
                case RepoAnalysisResult.ProjectLanguage.DotNet:
                    return string.Concat(
                        "FROM ", result.GetBestImage(), " AS build\n",
                        "WORKDIR /source\n",
                        "COPY . .\n",
                        "RUN dotnet restore \"./", projectName, ".csproj\" --verbosity quiet\n",
                        "RUN dotnet publish \"./", projectName, ".csproj\" -c Release -o /app --no-restore\n\n",
                        "FROM mcr.microsoft.com/dotnet/aspnet:8.0\n",
                        "WORKDIR /app\n",
                        "COPY --from=build /app .\n",
                        "EXPOSE 8080\n",
                        "ENTRYPOINT [\"dotnet\", \"", projectName, ".dll\"]\n"
                    );
                case RepoAnalysisResult.ProjectLanguage.NodeJs:
                    return string.Concat(
                        "FROM node:20-alpine AS build\n",
                        "WORKDIR /app\n",
                        "COPY package*.json ./\n",
                        "RUN npm ci\n",
                        "COPY . .\n",
                        "RUN npm run build || true\n\n",
                        "FROM node:20-alpine\n",
                        "WORKDIR /app\n",
                        "COPY --from=build /app/package*.json ./\n",
                        "COPY --from=build /app/node_modules ./node_modules\n",
                        "COPY --from=build /app/dist ./dist\n",
                        "EXPOSE 3000\n",
                        "CMD [\"node\", \"dist/index.js\"]\n"
                    );
                case RepoAnalysisResult.ProjectLanguage.Java when result.Framework?.ToLowerInvariant().Contains("kotlin") == true:
                    return string.Concat(
                        "FROM gradle:8.5-jdk17 AS build\n",
                        "WORKDIR /home/gradle/project\n",
                        "COPY . /home/gradle/project\n",
                        "RUN ./gradlew build -x test --no-daemon\n\n",
                        "FROM eclipse-temurin:17-jre-focal\n",
                        "WORKDIR /app\n",
                        "COPY --from=build /home/gradle/project/build/libs/*.jar app.jar\n",
                        "EXPOSE 8080\n",
                        "ENTRYPOINT [\"java\", \"-jar\", \"/app/app.jar\"]\n"
                    );
                case RepoAnalysisResult.ProjectLanguage.Java:
                    return string.Concat(
                        "FROM maven:3.9-eclipse-temurin-17 AS build\n",
                        "WORKDIR /app\n",
                        "COPY pom.xml ./\n",
                        "RUN mvn -B -f pom.xml dependency:go-offline -DskipTests\n",
                        "COPY src ./src\n",
                        "RUN mvn -B -DskipTests package\n\n",
                        "FROM eclipse-temurin:17-jre-focal\n",
                        "WORKDIR /app\n",
                        "COPY --from=build /app/target/*.jar app.jar\n",
                        "EXPOSE 8080\n",
                        "ENTRYPOINT [\"java\", \"-jar\", \"/app/app.jar\"]\n"
                    );
                case RepoAnalysisResult.ProjectLanguage.Go:
                    return string.Concat(
                        "FROM golang:1.21 AS build\n",
                        "WORKDIR /src\n",
                        "COPY go.mod go.sum ./\n",
                        "RUN go mod download\n",
                        "COPY . .\n",
                        "RUN CGO_ENABLED=0 GOOS=linux go build -a -installsuffix cgo -o app ./...\n\n",
                        "FROM gcr.io/distroless/static\n",
                        "WORKDIR /app\n",
                        "COPY --from=build /src/app ./app\n",
                        "EXPOSE 8080\n",
                        "ENTRYPOINT [\"/app/app\"]\n"
                    );
                case RepoAnalysisResult.ProjectLanguage.Python:
                    return string.Concat(
                        "FROM python:3.11-slim AS build\n",
                        "WORKDIR /build\n",
                        "COPY pyproject.toml requirements.txt ./\n",
                        "RUN python -m pip install --upgrade pip setuptools wheel\n",
                        "RUN python -m pip wheel --wheel-dir /wheels -r requirements.txt\n\n",
                        "FROM python:3.11-slim\n",
                        "WORKDIR /app\n",
                        "COPY --from=build /wheels /wheels\n",
                        "RUN pip install --no-index --find-links=/wheels -r requirements.txt\n",
                        "COPY . .\n",
                        "EXPOSE 8000\n",
                        "CMD [\"gunicorn\",\"-b\",\"0.0.0.0:8000\",\"app.wsgi:application\"]\n"
                    );
                default:
                    return "";
            }
        }

        private IEnumerable<string> GetRelevantFilesForRules(DetectorRule rule, List<string> allFiles)
        {
            var lang = rule.Language.ToLowerInvariant();
            return lang switch
            {
                "dotnet" => allFiles.Where(f => f.EndsWith(".csproj")),
                "nodejs" => allFiles.Where(f => f.EndsWith("package.json")),
                "python" => allFiles.Where(f => f.EndsWith("requirements.txt") || f.EndsWith("pyproject.toml")),
                "go" => allFiles.Where(f => f.EndsWith("go.mod")),
                "java" => allFiles.Where(f => f.EndsWith("pom.xml") || f.EndsWith("build.gradle") || f.EndsWith("build.gradle.kts")),
                "rust" => allFiles.Where(f => f.EndsWith("cargo.toml")),
                _ => Enumerable.Empty<string>()
            };
        }

        private List<DetectorRule> GetBuiltInRules()
        {
            return new List<DetectorRule>
            {
                // === KOTLIN RULES ===
                new DetectorRule {
                    Name = "Kotlin",
                    Language = "Kotlin",
                    LanguageDetectionTriggers = new List<string>{ "build.gradle", "build.gradle.kts" },
                    FilePatterns = new List<string>{ "build.gradle", "build.gradle.kts" },
                    DependencyNames = new List<string>{ "kotlin-stdlib", "org.jetbrains.kotlin", "kotlin-gradle-plugin" },
                    FileRegex = new List<string>{ @"apply plugin.*kotlin", @"kotlin-gradle-plugin", @"kotlin-stdlib" },
                    BuildCommands = new List<string>{ "./gradlew build" },
                    Weight = 10, Threshold = 2, Rationale = "Pure Kotlin project detected"
                },
                new DetectorRule {
                    Name = "Kotlin Multiplatform",
                    Language = "Kotlin", 
                    LanguageDetectionTriggers = new List<string>{ "build.gradle.kts" },
                    FilePatterns = new List<string>{ "build.gradle.kts" },
                    DependencyNames = new List<string>{ "kotlin-multiplatform", "org.jetbrains.kotlin.multiplatform" },
                    FileRegex = new List<string>{ @"kotlin-multiplatform", @"kotlin\(", @"commonMain", @"commonTest" },
                    BuildCommands = new List<string>{ "./gradlew build" },
                    Weight = 6, Threshold = 3, Rationale = "Kotlin Multiplatform project detected"
                },

                // === JAVA RULES ===
                new DetectorRule {
                    Name = "Spring Boot",
                    Language = "Java",
                    LanguageDetectionTriggers = new List<string>{ "pom.xml", "build.gradle", "build.gradle.kts" },
                    FilePatterns = new List<string>{ "pom.xml", "build.gradle", "build.gradle.kts" },
                    DependencyNames = new List<string>{ "spring-boot-starter", "spring-boot", "org.springframework.boot" },
                    FileRegex = new List<string>{ @"@SpringBootApplication", @"spring-boot-starter" },
                    BuildCommands = new List<string>{ "mvn clean package -DskipTests", "gradle build -x test" },
                    Weight = 5, Threshold = 3, Rationale = "Spring Boot framework detected"
                },
                new DetectorRule {
                    Name = "Spring Framework",
                    Language = "Java",
                    LanguageDetectionTriggers = new List<string>{ "pom.xml", "build.gradle" },
                    FilePatterns = new List<string>{ "pom.xml", "build.gradle" },
                    DependencyNames = new List<string>{ "spring-core", "springframework", "spring-context" },
                    FileRegex = new List<string>{ @"@Component", @"@Service", @"@Controller", @"@Repository" },
                    BuildCommands = new List<string>{ "mvn clean package", "gradle build" },
                    Weight = 4, Threshold = 2, Rationale = "Spring Framework detected"
                },
                new DetectorRule {
                    Name = "Quarkus",
                    Language = "Java",
                    LanguageDetectionTriggers = new List<string>{ "pom.xml", "build.gradle" },
                    DependencyNames = new List<string>{ "quarkus", "io.quarkus" },
                    FileRegex = new List<string>{ @"@QuarkusApplication", @"quarkus-" },
                    BuildCommands = new List<string>{ "mvn quarkus:dev", "gradle quarkusDev" },
                    Weight = 4, Threshold = 2, Rationale = "Quarkus framework detected"
                },
                new DetectorRule {
                    Name = "Micronaut",
                    Language = "Java",
                    LanguageDetectionTriggers = new List<string>{ "pom.xml", "build.gradle" },
                    DependencyNames = new List<string>{ "micronaut", "io.micronaut" },
                    FileRegex = new List<string>{ @"@MicronautApplication", @"micronaut-" },
                    BuildCommands = new List<string>{ "mvn clean package", "gradle build" },
                    Weight = 4, Threshold = 2, Rationale = "Micronaut framework detected"
                },
                new DetectorRule {
                    Name = "Vert.x",
                    Language = "Java",
                    LanguageDetectionTriggers = new List<string>{ "pom.xml", "build.gradle" },
                    DependencyNames = new List<string>{ "vertx", "io.vertx" },
                    FileRegex = new List<string>{ @"Vertx\.vertx", @"AbstractVerticle" },
                    BuildCommands = new List<string>{ "mvn clean package", "gradle build" },
                    Weight = 4, Threshold = 2, Rationale = "Vert.x framework detected"
                },
                new DetectorRule {
                    Name = "Java Maven",
                    Language = "Java",
                    LanguageDetectionTriggers = new List<string>{ "pom.xml" },
                    FilePatterns = new List<string>{ "pom.xml" },
                    BuildCommands = new List<string>{ "mvn clean package" },
                    Weight = 3, Threshold = 1, Rationale = "Java Maven project"
                },
                new DetectorRule {
                    Name = "Java Gradle",
                    Language = "Java",
                    LanguageDetectionTriggers = new List<string>{ "build.gradle", "build.gradle.kts" },
                    FilePatterns = new List<string>{ "build.gradle", "build.gradle.kts", "settings.gradle" },
                    BuildCommands = new List<string>{ "./gradlew build" },
                    Weight = 3, Threshold = 1, Rationale = "Java Gradle project"
                },

                // === KOTLIN RULES ===
                new DetectorRule {
                    Name = "Kotlin Spring Boot",
                    Language = "Java", // Using Java language type for Kotlin on JVM
                    LanguageDetectionTriggers = new List<string>{ "build.gradle.kts", "pom.xml" },
                    FilePatterns = new List<string>{ "build.gradle.kts" },
                    DependencyNames = new List<string>{ "spring-boot-starter", "kotlin-stdlib", "org.jetbrains.kotlin" },
                    FileRegex = new List<string>{ @"@SpringBootApplication", @"kotlin", @"fun main" },
                    BuildCommands = new List<string>{ "./gradlew build", "mvn clean package" },
                    Weight = 5, Threshold = 3, Rationale = "Kotlin Spring Boot project"
                },
                new DetectorRule {
                    Name = "Ktor",
                    Language = "Java", // Kotlin on JVM
                    LanguageDetectionTriggers = new List<string>{ "build.gradle.kts" },
                    DependencyNames = new List<string>{ "ktor", "io.ktor" },
                    FileRegex = new List<string>{ @"embeddedServer", @"io\.ktor" },
                    BuildCommands = new List<string>{ "./gradlew build" },
                    Weight = 4, Threshold = 2, Rationale = "Ktor framework detected"
                },

                // === GO RULES ===
                new DetectorRule {
                    Name = "Gin Framework",
                    Language = "Go",
                    LanguageDetectionTriggers = new List<string>{ "go.mod", "go.sum" },
                    FilePatterns = new List<string>{ "go.mod", "go.sum" },
                    DependencyNames = new List<string>{ "gin-gonic/gin", "github.com/gin-gonic/gin" },
                    FileRegex = new List<string>{ @"gin\.", @"gin\.Default", @"gin\.New" },
                    BuildCommands = new List<string>{ "go mod download", "go build ./..." },
                    Weight = 4, Threshold = 2, Rationale = "Gin web framework detected"
                },
                new DetectorRule {
                    Name = "Echo Framework",
                    Language = "Go",
                    LanguageDetectionTriggers = new List<string>{ "go.mod" },
                    DependencyNames = new List<string>{ "labstack/echo", "github.com/labstack/echo" },
                    FileRegex = new List<string>{ @"echo\.", @"echo\.New" },
                    BuildCommands = new List<string>{ "go mod download", "go build ./..." },
                    Weight = 4, Threshold = 2, Rationale = "Echo web framework detected"
                },
                new DetectorRule {
                    Name = "Fiber Framework",
                    Language = "Go",
                    LanguageDetectionTriggers = new List<string>{ "go.mod" },
                    DependencyNames = new List<string>{ "gofiber/fiber", "github.com/gofiber/fiber" },
                    FileRegex = new List<string>{ @"fiber\.", @"fiber\.New" },
                    BuildCommands = new List<string>{ "go mod download", "go build ./..." },
                    Weight = 4, Threshold = 2, Rationale = "Fiber web framework detected"
                },
                new DetectorRule {
                    Name = "Gorilla Mux",
                    Language = "Go",
                    LanguageDetectionTriggers = new List<string>{ "go.mod" },
                    DependencyNames = new List<string>{ "gorilla/mux", "github.com/gorilla/mux" },
                    FileRegex = new List<string>{ @"mux\.", @"mux\.NewRouter" },
                    BuildCommands = new List<string>{ "go mod download", "go build ./..." },
                    Weight = 4, Threshold = 2, Rationale = "Gorilla Mux router detected"
                },
                new DetectorRule {
                    Name = "Go Standard",
                    Language = "Go",
                    LanguageDetectionTriggers = new List<string>{ "go.mod", "go.sum" },
                    FilePatterns = new List<string>{ "go.mod", "go.sum" },
                    BuildCommands = new List<string>{ "go mod download", "go build ./..." },
                    Weight = 3, Threshold = 1, Rationale = "Standard Go project"
                },

                // === NODE.JS / JAVASCRIPT / TYPESCRIPT RULES ===
                new DetectorRule {
                    Name = "React",
                    Language = "NodeJs",
                    LanguageDetectionTriggers = new List<string>{ "package.json" },
                    FilePatterns = new List<string>{ "package.json" },
                    DependencyNames = new List<string>{ "react", "@types/react", "react-dom", "react-scripts" },
                    FileRegex = new List<string>{ @"import.*react", @"from ['""]react['""]", @"React\." },
                    BuildCommands = new List<string>{ "npm ci", "npm run build" },
                    Weight = 5, Threshold = 2, Rationale = "React application detected"
                },
                new DetectorRule {
                    Name = "Next.js",
                    Language = "NodeJs",
                    LanguageDetectionTriggers = new List<string>{ "package.json", "next.config.js", "next.config.ts" },
                    FilePatterns = new List<string>{ "next.config.js", "next.config.ts" },
                    DependencyNames = new List<string>{ "next", "@next/", "next/" },
                    FileRegex = new List<string>{ @"next/", @"Next\.", @"getServerSideProps", @"getStaticProps" },
                    BuildCommands = new List<string>{ "npm ci", "npm run build" },
                    Weight = 5, Threshold = 2, Rationale = "Next.js framework detected"
                },
                new DetectorRule {
                    Name = "Vue.js",
                    Language = "NodeJs",
                    LanguageDetectionTriggers = new List<string>{ "package.json" },
                    DependencyNames = new List<string>{ "vue", "@vue/", "nuxt" },
                    FileRegex = new List<string>{ @"<template>", @"Vue\.", @"createApp", @"vue-" },
                    BuildCommands = new List<string>{ "npm ci", "npm run build" },
                    Weight = 5, Threshold = 2, Rationale = "Vue.js framework detected"
                },
                new DetectorRule {
                    Name = "Angular",
                    Language = "NodeJs",
                    LanguageDetectionTriggers = new List<string>{ "package.json", "angular.json" },
                    FilePatterns = new List<string>{ "angular.json", "ng.json" },
                    DependencyNames = new List<string>{ "@angular/core", "@angular/", "angular" },
                    FileRegex = new List<string>{ @"@Component", @"@Injectable", @"@angular/" },
                    BuildCommands = new List<string>{ "npm ci", "ng build" },
                    Weight = 5, Threshold = 2, Rationale = "Angular framework detected"
                },
                new DetectorRule {
                    Name = "Express.js",
                    Language = "NodeJs",
                    LanguageDetectionTriggers = new List<string>{ "package.json" },
                    DependencyNames = new List<string>{ "express", "@types/express" },
                    FileRegex = new List<string>{ @"express\(\)", @"app\.listen", @"app\.get", @"require.*express" },
                    BuildCommands = new List<string>{ "npm ci", "npm start" },
                    Weight = 4, Threshold = 2, Rationale = "Express.js server detected"
                },
                new DetectorRule {
                    Name = "NestJS",
                    Language = "NodeJs",
                    LanguageDetectionTriggers = new List<string>{ "package.json", "nest-cli.json" },
                    FilePatterns = new List<string>{ "nest-cli.json" },
                    DependencyNames = new List<string>{ "@nestjs/core", "@nestjs/common", "@nestjs/" },
                    FileRegex = new List<string>{ @"@Module", @"@Controller", @"@Injectable", @"@nestjs/" },
                    BuildCommands = new List<string>{ "npm ci", "npm run build" },
                    Weight = 5, Threshold = 2, Rationale = "NestJS framework detected"
                },
                new DetectorRule {
                    Name = "Fastify",
                    Language = "NodeJs",
                    LanguageDetectionTriggers = new List<string>{ "package.json" },
                    DependencyNames = new List<string>{ "fastify", "@fastify/" },
                    FileRegex = new List<string>{ @"fastify\(\)", @"fastify\.", @"require.*fastify" },
                    BuildCommands = new List<string>{ "npm ci", "npm start" },
                    Weight = 4, Threshold = 2, Rationale = "Fastify server detected"
                },
                new DetectorRule {
                    Name = "TypeScript",
                    Language = "NodeJs",
                    LanguageDetectionTriggers = new List<string>{ "package.json", "tsconfig.json" },
                    FilePatterns = new List<string>{ "tsconfig.json", "tsconfig.build.json" },
                    DependencyNames = new List<string>{ "typescript", "@types/node", "ts-node" },
                    FileRegex = new List<string>{ @"interface\s+\w+", @"type\s+\w+\s*=" },
                    BuildCommands = new List<string>{ "npm ci", "tsc", "npm run build" },
                    Weight = 4, Threshold = 2, Rationale = "TypeScript project detected"
                },
                new DetectorRule {
                    Name = "Node.js Standard",
                    Language = "NodeJs",
                    LanguageDetectionTriggers = new List<string>{ "package.json" },
                    FilePatterns = new List<string>{ "package.json", "package-lock.json" },
                    BuildCommands = new List<string>{ "npm ci", "npm run build" },
                    Weight = 3, Threshold = 1, Rationale = "Standard Node.js project"
                },

                // === PYTHON RULES ===
                new DetectorRule {
                    Name = "Django",
                    Language = "Python",
                    LanguageDetectionTriggers = new List<string>{ "requirements.txt", "pyproject.toml", "manage.py" },
                    FilePatterns = new List<string>{ "manage.py", "settings.py" },
                    DependencyNames = new List<string>{ "django", "Django" },
                    FileRegex = new List<string>{ @"from django", @"import django", @"DJANGO_SETTINGS_MODULE", @"django\.setup" },
                    BuildCommands = new List<string>{ "pip install -r requirements.txt", "python manage.py collectstatic --noinput" },
                    Weight = 5, Threshold = 2, Rationale = "Django web framework detected"
                },
                new DetectorRule {
                    Name = "FastAPI",
                    Language = "Python",
                    LanguageDetectionTriggers = new List<string>{ "requirements.txt", "pyproject.toml" },
                    DependencyNames = new List<string>{ "fastapi", "uvicorn" },
                    FileRegex = new List<string>{ @"from fastapi", @"FastAPI\(\)", @"@app\.(get|post|put|delete)" },
                    BuildCommands = new List<string>{ "pip install -r requirements.txt" },
                    Weight = 5, Threshold = 2, Rationale = "FastAPI framework detected"
                },
                new DetectorRule {
                    Name = "Flask",
                    Language = "Python",
                    LanguageDetectionTriggers = new List<string>{ "requirements.txt", "pyproject.toml" },
                    DependencyNames = new List<string>{ "flask", "Flask" },
                    FileRegex = new List<string>{ @"from flask", @"Flask\(__name__\)", @"@app\.route" },
                    BuildCommands = new List<string>{ "pip install -r requirements.txt" },
                    Weight = 4, Threshold = 2, Rationale = "Flask web framework detected"
                },
                new DetectorRule {
                    Name = "Tornado",
                    Language = "Python",
                    LanguageDetectionTriggers = new List<string>{ "requirements.txt", "pyproject.toml" },
                    DependencyNames = new List<string>{ "tornado" },
                    FileRegex = new List<string>{ @"import tornado", @"tornado\.web", @"RequestHandler" },
                    BuildCommands = new List<string>{ "pip install -r requirements.txt" },
                    Weight = 4, Threshold = 2, Rationale = "Tornado web framework detected"
                },
                new DetectorRule {
                    Name = "Starlette",
                    Language = "Python",
                    LanguageDetectionTriggers = new List<string>{ "requirements.txt", "pyproject.toml" },
                    DependencyNames = new List<string>{ "starlette" },
                    FileRegex = new List<string>{ @"from starlette", @"Starlette\(" },
                    BuildCommands = new List<string>{ "pip install -r requirements.txt" },
                    Weight = 4, Threshold = 2, Rationale = "Starlette framework detected"
                },
                new DetectorRule {
                    Name = "Python Poetry",
                    Language = "Python",
                    LanguageDetectionTriggers = new List<string>{ "pyproject.toml", "poetry.lock" },
                    FilePatterns = new List<string>{ "pyproject.toml", "poetry.lock" },
                    BuildCommands = new List<string>{ "poetry install", "poetry build" },
                    Weight = 4, Threshold = 1, Rationale = "Python Poetry project"
                },
                new DetectorRule {
                    Name = "Python Standard",
                    Language = "Python",
                    LanguageDetectionTriggers = new List<string>{ "requirements.txt", "setup.py", "pyproject.toml" },
                    FilePatterns = new List<string>{ "requirements.txt", "setup.py" },
                    BuildCommands = new List<string>{ "pip install -r requirements.txt" },
                    Weight = 3, Threshold = 1, Rationale = "Standard Python project"
                }
            };
        }

        private static List<string> ParsePnpmWorkspacePatterns(string yamlPath)
        {
            var lines = File.ReadAllLines(yamlPath);
            var patterns = new List<string>();
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                // match lines like - "packages/*" or - 'packages/*' or - packages/*
                if (trimmed.StartsWith("- "))
                {
                    var val = trimmed.Substring(2).Trim().Trim('"', '\'');
                    if (!string.IsNullOrEmpty(val)) patterns.Add(val);
                }
                else if (trimmed.StartsWith("packages:") || trimmed.StartsWith("workspaces:"))
                {
                    // ignore header
                }
            }
            return patterns;
        }

        private static IEnumerable<string> ExpandWorkspacePatterns(string baseDir, List<string> patterns)
        {
            var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
            foreach (var pat in patterns)
            {
                var normalized = pat.Trim().Replace('/', Path.DirectorySeparatorChar);
                matcher.AddInclude(normalized);
            }

            var result = new List<string>();
            var directoryInfo = new DirectoryInfo(baseDir);
            var directoryInfoWrapper = new DirectoryInfoWrapper(directoryInfo);
            var matchResult = matcher.Execute(directoryInfoWrapper);
            foreach (var fileMatch in matchResult.Files)
            {
                var matchedPath = Path.Combine(baseDir, fileMatch.Path);
                if (Directory.Exists(matchedPath)) result.Add(matchedPath);
                else
                {
                    // If matched path points to files (package.json), add parent dir
                    var parent = Path.GetDirectoryName(matchedPath);
                    if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent)) result.Add(parent);
                }
            }

            return result.Distinct();
        }

        // >>> Deep analysis methods
        private void ExtractVersionsAndFrameworks(RepoAnalysisResult result, List<string> allFiles)
        {
            try
            {
                switch (result.Language)
                {
                    case RepoAnalysisResult.ProjectLanguage.Java:
                        // Maven analysis
                        var pom = allFiles.FirstOrDefault(f => f.EndsWith("pom.xml"));
                        if (pom != null)
                        {
                            var content = File.ReadAllText(pom);
                            
                            // Extract Java version (multiple patterns)
                            var javaVersionMatch = Regex.Match(content, @"<java\.version>(\d+\.?\d*)</java\.version>") ??
                                                 Regex.Match(content, @"<maven\.compiler\.source>(\d+)</maven\.compiler\.source>") ??
                                                 Regex.Match(content, @"<maven\.compiler\.target>(\d+)</maven\.compiler\.target>");
                            if (javaVersionMatch != null && javaVersionMatch.Success) 
                                result.LanguageVersion = javaVersionMatch.Groups[1].Value;
                            
                            // Detect frameworks
                            if (content.Contains("spring-boot-starter") || content.Contains("org.springframework.boot")) 
                                result.DetectedFrameworks.Add("Spring Boot");
                            if (content.Contains("spring-core") || content.Contains("org.springframework")) 
                                result.DetectedFrameworks.Add("Spring Framework");
                            if (content.Contains("io.quarkus") || content.Contains("quarkus-")) 
                                result.DetectedFrameworks.Add("Quarkus");
                            if (content.Contains("io.micronaut") || content.Contains("micronaut-")) 
                                result.DetectedFrameworks.Add("Micronaut");
                            if (content.Contains("io.vertx") || content.Contains("vertx-")) 
                                result.DetectedFrameworks.Add("Vert.x");
                            
                            result.BuildTools.Add("Maven");
                            
                            // Parse Maven dependencies from pom.xml
                            try
                            {
                                var xmlDoc = XDocument.Parse(content);
                                XNamespace ns = xmlDoc.Root?.Name.Namespace ?? XNamespace.None;
                                
                                // Extract dependencies
                                var dependencies = xmlDoc.Descendants(ns + "dependency");
                                foreach (var dep in dependencies)
                                {
                                    var groupId = dep.Element(ns + "groupId")?.Value ?? "";
                                    var artifactId = dep.Element(ns + "artifactId")?.Value ?? "";
                                    var version = dep.Element(ns + "version")?.Value ?? "";
                                    var scope = dep.Element(ns + "scope")?.Value ?? "";
                                    
                                    if (!string.IsNullOrEmpty(groupId) && !string.IsNullOrEmpty(artifactId))
                                    {
                                        var dependency = new Dependency
                                        {
                                            Name = $"{groupId}:{artifactId}",
                                            Version = version
                                        };
                                        
                                        // Avoid duplicates
                                        if (!result.Dependencies.Any(d => d.Name == dependency.Name))
                                        {
                                            result.Dependencies.Add(dependency);
                                        }
                                        
                                        // Detect test frameworks and tools from dependencies
                                        var depNameLower = dependency.Name.ToLowerInvariant();
                                        if (depNameLower.Contains("junit") && !result.TestFrameworks.Contains("JUnit"))
                                            result.TestFrameworks.Add("JUnit");
                                        if (depNameLower.Contains("testng") && !result.TestFrameworks.Contains("TestNG"))
                                            result.TestFrameworks.Add("TestNG");
                                        if (depNameLower.Contains("mockito") && !result.TestFrameworks.Contains("Mockito"))
                                            result.TestFrameworks.Add("Mockito");
                                        if (depNameLower.Contains("jacoco") && !result.CoverageTools.Contains("JaCoCo"))
                                            result.CoverageTools.Add("JaCoCo");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogDebug(ex, "Failed parsing Maven dependencies from pom.xml");
                            }
                        }
                        
                        // Gradle analysis
                        var gradle = allFiles.FirstOrDefault(f => f.EndsWith("build.gradle") || f.EndsWith("build.gradle.kts"));
                        if (gradle != null)
                        {
                            var content = File.ReadAllText(gradle);
                            
                            // Extract Java/Kotlin version
                            var javaVersionMatch = Regex.Match(content, @"sourceCompatibility\s*[=:]\s*['""]?(\d+\.?\d*)['""]?") ??
                                                 Regex.Match(content, @"targetCompatibility\s*[=:]\s*['""]?(\d+\.?\d*)['""]?") ??
                                                 Regex.Match(content, @"jvmTarget\s*[=:]\s*['""](\d+\.?\d*)['""]");
                            if (javaVersionMatch != null && javaVersionMatch.Success) 
                                result.LanguageVersion = javaVersionMatch.Groups[1].Value;
                            
                            // Detect Kotlin
                            if (content.Contains("kotlin") || content.Contains("org.jetbrains.kotlin")) 
                                result.DetectedFrameworks.Add("Kotlin");
                            
                            // Detect frameworks
                            if (content.Contains("spring-boot") || content.Contains("org.springframework.boot")) 
                                result.DetectedFrameworks.Add("Spring Boot");
                            if (content.Contains("io.ktor") || content.Contains("ktor-")) 
                                result.DetectedFrameworks.Add("Ktor");
                            if (content.Contains("io.quarkus")) 
                                result.DetectedFrameworks.Add("Quarkus");
                            if (content.Contains("io.micronaut")) 
                                result.DetectedFrameworks.Add("Micronaut");
                            if (content.Contains("io.vertx")) 
                                result.DetectedFrameworks.Add("Vert.x");
                            
                            result.BuildTools.Add("Gradle");
                        }
                        break;
                        
                    case RepoAnalysisResult.ProjectLanguage.Kotlin:
                        // Enhanced Gradle analysis for Kotlin projects
                        var kotlinGradle = allFiles.FirstOrDefault(f => f.EndsWith("build.gradle") || f.EndsWith("build.gradle.kts"));
                        if (kotlinGradle != null)
                        {
                            var content = File.ReadAllText(kotlinGradle);
                            
                            // Extract Kotlin version (multiple patterns)
                            var kotlinVersionMatch = Regex.Match(content, @"kotlin_version\s*=\s*['""]([^'""]+)['""]") ??
                                                   Regex.Match(content, @"org\.jetbrains\.kotlin:kotlin-gradle-plugin:([^'""]+)['""]") ??
                                                   Regex.Match(content, @"kotlin-stdlib:([^'""]+)['""]");
                            if (kotlinVersionMatch != null && kotlinVersionMatch.Success) 
                                result.LanguageVersion = kotlinVersionMatch.Groups[1].Value;
                            
                            // Extract JVM target version
                            var jvmTargetMatch = Regex.Match(content, @"jvmTarget\s*[=:]\s*['""](\d+)['""]") ??
                                               Regex.Match(content, @"sourceCompatibility\s*[=:]\s*['""]?(\d+\.?\d*)['""]?") ??
                                               Regex.Match(content, @"targetCompatibility\s*[=:]\s*['""]?(\d+\.?\d*)['""]?");
                            if (jvmTargetMatch != null && jvmTargetMatch.Success && string.IsNullOrEmpty(result.LanguageVersion))
                                result.LanguageVersion = jvmTargetMatch.Groups[1].Value;
                            
                            // Detect Kotlin frameworks and libraries
                            if (content.Contains("kotlin") || content.Contains("org.jetbrains.kotlin")) 
                                result.DetectedFrameworks.Add("Kotlin");
                            if (content.Contains("io.ktor") || content.Contains("ktor-")) 
                                result.DetectedFrameworks.Add("Ktor");
                            if (content.Contains("spring-boot") || content.Contains("org.springframework.boot")) 
                                result.DetectedFrameworks.Add("Spring Boot");
                            if (content.Contains("io.quarkus")) 
                                result.DetectedFrameworks.Add("Quarkus");
                            if (content.Contains("io.micronaut")) 
                                result.DetectedFrameworks.Add("Micronaut");
                            if (content.Contains("kotlin-multiplatform") || content.Contains("multiplatform")) 
                                result.DetectedFrameworks.Add("Kotlin Multiplatform");
                            
                            result.BuildTools.Add("Gradle");
                            
                            // Parse dependencies from build.gradle (multiple patterns)
                            var depPatterns = new[]
                            {
                                @"implementation\s+['""]([^'""]+)['""]",
                                @"compile\s+['""]([^'""]+)['""]",
                                @"api\s+['""]([^'""]+)['""]",
                                @"testImplementation\s+['""]([^'""]+)['""]",
                                @"testCompile\s+['""]([^'""]+)['""]"
                            };
                            
                            foreach (var pattern in depPatterns)
                            {
                                var depMatches = Regex.Matches(content, pattern);
                                foreach (Match match in depMatches)
                                {
                                    var dep = match.Groups[1].Value;
                                    var parts = dep.Split(':');
                                    if (parts.Length >= 2)
                                    {
                                        var dependency = new Dependency 
                                        { 
                                            Name = parts.Length > 1 ? $"{parts[0]}:{parts[1]}" : parts[0],
                                            Version = parts.Length > 2 ? parts[2] : ""
                                        };
                                        
                                        // Avoid duplicates
                                        if (!result.Dependencies.Any(d => d.Name == dependency.Name))
                                        {
                                            result.Dependencies.Add(dependency);
                                        }
                                    }
                                }
                            }
                            
                            // Detect test frameworks from dependencies
                            foreach (var dep in result.Dependencies)
                            {
                                var depName = dep.Name.ToLowerInvariant();
                                if (depName.Contains("junit") || depName.Contains("jupiter"))
                                {
                                    if (!result.TestFrameworks.Contains("JUnit"))
                                        result.TestFrameworks.Add("JUnit");
                                }
                                if (depName.Contains("kotest"))
                                {
                                    if (!result.TestFrameworks.Contains("Kotest"))
                                        result.TestFrameworks.Add("Kotest");
                                }
                                if (depName.Contains("spek"))
                                {
                                    if (!result.TestFrameworks.Contains("Spek"))
                                        result.TestFrameworks.Add("Spek");
                                }
                                if (depName.Contains("jacoco"))
                                {
                                    if (!result.CoverageTools.Contains("JaCoCo"))
                                        result.CoverageTools.Add("JaCoCo");
                                }
                            }
                            
                            // Check for Gradle multi-module setup
                            var settingsGradle = allFiles.FirstOrDefault(f => 
                                Path.GetFileName(f).Equals("settings.gradle", StringComparison.OrdinalIgnoreCase) ||
                                Path.GetFileName(f).Equals("settings.gradle.kts", StringComparison.OrdinalIgnoreCase));
                            
                            if (settingsGradle != null)
                            {
                                try
                                {
                                    var settingsContent = File.ReadAllText(settingsGradle);
                                    var includeMatches = Regex.Matches(settingsContent, @"include\s*\(\s*['""]([^'""]+)['""]");
                                    foreach (Match match in includeMatches)
                                    {
                                        var modulePath = match.Groups[1].Value.Replace(':', Path.DirectorySeparatorChar);
                                        var baseDir = Path.GetDirectoryName(settingsGradle) ?? ".";
                                        var moduleDir = Path.Combine(baseDir, modulePath);
                                        if (Directory.Exists(moduleDir) && !result.Subprojects.Contains(moduleDir))
                                        {
                                            result.Subprojects.Add(moduleDir);
                                        }
                                    }
                                }
                                catch (Exception ex) 
                                { 
                                    _logger.LogDebug(ex, "Failed parsing settings.gradle for Kotlin project"); 
                                }
                            }
                        }
                        break;
                        
                    case RepoAnalysisResult.ProjectLanguage.NodeJs:
                        var packageJson = allFiles.FirstOrDefault(f => f.EndsWith("package.json"));
                        if (packageJson != null)
                        {
                            var json = JsonDocument.Parse(File.ReadAllText(packageJson));
                            
                            // Extract Node version
                            if (json.RootElement.TryGetProperty("engines", out var engines) && 
                                engines.TryGetProperty("node", out var nodeVer))
                                result.LanguageVersion = nodeVer.GetString() ?? "";
                            
                            // Parse dependencies and devDependencies
                            if (json.RootElement.TryGetProperty("dependencies", out var deps))
                            {
                                foreach (var dep in deps.EnumerateObject())
                                {
                                    var version = dep.Value.GetString() ?? "";
                                    if (!result.Dependencies.Any(d => d.Name == dep.Name))
                                        result.Dependencies.Add(new Dependency { Name = dep.Name, Version = version });
                                }
                            }
                            if (json.RootElement.TryGetProperty("devDependencies", out var devDeps))
                            {
                                foreach (var dep in devDeps.EnumerateObject())
                                {
                                    var version = dep.Value.GetString() ?? "";
                                    if (!result.Dependencies.Any(d => d.Name == dep.Name))
                                        result.Dependencies.Add(new Dependency { Name = dep.Name, Version = version });
                                }
                            }
                            
                            // Check all deps for framework/tool detection
                            var allDeps = new List<string>();
                            if (json.RootElement.TryGetProperty("dependencies", out var deps2))
                            {
                                foreach (var dep in deps2.EnumerateObject())
                                    allDeps.Add(dep.Name);
                            }
                            if (json.RootElement.TryGetProperty("devDependencies", out var devDeps2))
                            {
                                foreach (var dep in devDeps2.EnumerateObject())
                                    allDeps.Add(dep.Name);
                            }
                            
                            // Detect frameworks
                            if (allDeps.Contains("react") || allDeps.Contains("@types/react")) 
                                result.DetectedFrameworks.Add("React");
                            if (allDeps.Contains("next") || allDeps.Any(d => d.StartsWith("@next/"))) 
                                result.DetectedFrameworks.Add("Next.js");
                            if (allDeps.Contains("vue") || allDeps.Any(d => d.StartsWith("@vue/"))) 
                                result.DetectedFrameworks.Add("Vue.js");
                            if (allDeps.Any(d => d.StartsWith("@angular/"))) 
                                result.DetectedFrameworks.Add("Angular");
                            if (allDeps.Contains("express") || allDeps.Contains("@types/express")) 
                                result.DetectedFrameworks.Add("Express.js");
                            if (allDeps.Any(d => d.StartsWith("@nestjs/"))) 
                                result.DetectedFrameworks.Add("NestJS");
                            if (allDeps.Contains("fastify") || allDeps.Any(d => d.StartsWith("@fastify/"))) 
                                result.DetectedFrameworks.Add("Fastify");
                            if (allDeps.Contains("typescript") || allDeps.Contains("@types/node")) 
                                result.DetectedFrameworks.Add("TypeScript");
                            
                            // Detect build tools
                            var baseDir = Path.GetDirectoryName(packageJson)!;
                            if (File.Exists(Path.Combine(baseDir, "pnpm-lock.yaml")) || 
                                File.Exists(Path.Combine(baseDir, "pnpm-workspace.yaml"))) 
                                result.BuildTools.Add("pnpm");
                            else if (File.Exists(Path.Combine(baseDir, "yarn.lock"))) 
                                result.BuildTools.Add("yarn");
                            else 
                                result.BuildTools.Add("npm");
                        }
                        
                        // Check for TypeScript config
                        if (allFiles.Any(f => f.EndsWith("tsconfig.json")))
                        {
                            if (!result.DetectedFrameworks.Contains("TypeScript"))
                                result.DetectedFrameworks.Add("TypeScript");
                        }
                        break;
                        
                    case RepoAnalysisResult.ProjectLanguage.Python:
                        // pyproject.toml analysis
                        var pyproject = allFiles.FirstOrDefault(f => f.EndsWith("pyproject.toml"));
                        if (pyproject != null)
                        {
                            var content = File.ReadAllText(pyproject);
                            
                            // Extract Python version
                            var pyVerMatch = Regex.Match(content, @"python\s*=\s*['""]([^'""]+)['""]") ??
                                           Regex.Match(content, @"requires-python\s*=\s*['""]([^'""]+)['""]");
                            if (pyVerMatch != null && pyVerMatch.Success) 
                                result.LanguageVersion = pyVerMatch.Groups[1].Value;
                            
                            // Detect frameworks and parse dependencies
                            if (content.Contains("django") || content.Contains("Django")) 
                                result.DetectedFrameworks.Add("Django");
                            if (content.Contains("fastapi") || content.Contains("FastAPI")) 
                                result.DetectedFrameworks.Add("FastAPI");
                            if (content.Contains("flask") || content.Contains("Flask")) 
                                result.DetectedFrameworks.Add("Flask");
                            if (content.Contains("tornado")) 
                                result.DetectedFrameworks.Add("Tornado");
                            if (content.Contains("starlette")) 
                                result.DetectedFrameworks.Add("Starlette");
                            
                            if (content.Contains("[tool.poetry]")) 
                                result.BuildTools.Add("Poetry");
                            
                            // Parse dependencies from pyproject.toml
                            try
                            {
                                var depMatches = Regex.Matches(content, @"^[\s]*[""']([a-zA-Z0-9\-_\.]+)[""']\s*=\s*[""']([^""']+)[""']", RegexOptions.Multiline);
                                foreach (Match match in depMatches)
                                {
                                    var depName = match.Groups[1].Value;
                                    var depVersion = match.Groups[2].Value;
                                    
                                    if (!string.IsNullOrEmpty(depName) && !result.Dependencies.Any(d => d.Name == depName))
                                    {
                                        result.Dependencies.Add(new Dependency { Name = depName, Version = depVersion });
                                    }
                                }
                            }
                            catch { /* Skip if parsing fails */ }
                        }
                        
                        // requirements.txt analysis
                        var requirements = allFiles.FirstOrDefault(f => f.EndsWith("requirements.txt"));
                        if (requirements != null)
                        {
                            var content = File.ReadAllText(requirements);
                            
                            // Detect frameworks
                            if (content.Contains("django") || content.Contains("Django")) 
                                result.DetectedFrameworks.Add("Django");
                            if (content.Contains("fastapi") || content.Contains("FastAPI")) 
                                result.DetectedFrameworks.Add("FastAPI");
                            if (content.Contains("flask") || content.Contains("Flask")) 
                                result.DetectedFrameworks.Add("Flask");
                            if (content.Contains("tornado")) 
                                result.DetectedFrameworks.Add("Tornado");
                            if (content.Contains("starlette")) 
                                result.DetectedFrameworks.Add("Starlette");
                            
                            if (!result.BuildTools.Contains("Poetry"))
                                result.BuildTools.Add("pip");
                            
                            // Parse dependencies from requirements.txt
                            var lines = content.Split('\n');
                            foreach (var line in lines)
                            {
                                var trimmed = line.Trim();
                                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                                    continue;
                                
                                // Parse format: package==version or package>=version
                                var match = Regex.Match(trimmed, @"^([a-zA-Z0-9\-_\.]+)\s*(?:[=<>!]+\s*)?([0-9\.\*]+)?");
                                if (match.Success)
                                {
                                    var depName = match.Groups[1].Value;
                                    var depVersion = match.Groups[2].Success ? match.Groups[2].Value : "";
                                    
                                    if (!string.IsNullOrEmpty(depName) && !result.Dependencies.Any(d => d.Name == depName))
                                    {
                                        result.Dependencies.Add(new Dependency { Name = depName, Version = depVersion });
                                    }
                                }
                            }
                        }
                        
                        // Django-specific files
                        if (allFiles.Any(f => f.EndsWith("manage.py")) || 
                            allFiles.Any(f => f.EndsWith("settings.py")))
                        {
                            if (!result.DetectedFrameworks.Contains("Django"))
                                result.DetectedFrameworks.Add("Django");
                        }
                        break;
                        
                    case RepoAnalysisResult.ProjectLanguage.Go:
                        var goMod = allFiles.FirstOrDefault(f => f.EndsWith("go.mod"));
                        if (goMod != null)
                        {
                            var content = File.ReadAllText(goMod);
                            
                            // Extract Go version
                            var goVerMatch = Regex.Match(content, @"^go\s+(\d+\.\d+)", RegexOptions.Multiline);
                            if (goVerMatch.Success) 
                                result.LanguageVersion = goVerMatch.Groups[1].Value;
                            
                            // Detect frameworks from dependencies
                            if (content.Contains("github.com/gin-gonic/gin") || content.Contains("gin-gonic/gin")) 
                                result.DetectedFrameworks.Add("Gin");
                            if (content.Contains("github.com/labstack/echo") || content.Contains("labstack/echo")) 
                                result.DetectedFrameworks.Add("Echo");
                            if (content.Contains("github.com/gofiber/fiber") || content.Contains("gofiber/fiber")) 
                                result.DetectedFrameworks.Add("Fiber");
                            if (content.Contains("github.com/gorilla/mux") || content.Contains("gorilla/mux")) 
                                result.DetectedFrameworks.Add("Gorilla Mux");
                            
                            result.BuildTools.Add("Go Modules");
                            
                            // Parse dependencies from go.mod
                            var lines = content.Split('\n');
                            bool inRequire = false;
                            foreach (var line in lines)
                            {
                                var trimmed = line.Trim();
                                
                                if (trimmed.StartsWith("require ("))
                                {
                                    inRequire = true;
                                    continue;
                                }
                                
                                if (inRequire && trimmed == ")")
                                {
                                    inRequire = false;
                                    continue;
                                }
                                
                                if (trimmed.StartsWith("require ") && !trimmed.Contains("("))
                                {
                                    var match = Regex.Match(trimmed, @"^require\s+([^\s]+)\s+v?([0-9\.]+)");
                                    if (match.Success)
                                    {
                                        var depName = match.Groups[1].Value;
                                        var depVersion = match.Groups[2].Value;
                                        if (!result.Dependencies.Any(d => d.Name == depName))
                                            result.Dependencies.Add(new Dependency { Name = depName, Version = depVersion });
                                    }
                                }
                                
                                if (inRequire && !string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("//"))
                                {
                                    var match = Regex.Match(trimmed, @"^([^\s]+)\s+v?([0-9\.]+)");
                                    if (match.Success)
                                    {
                                        var depName = match.Groups[1].Value;
                                        var depVersion = match.Groups[2].Value;
                                        if (!result.Dependencies.Any(d => d.Name == depName))
                                            result.Dependencies.Add(new Dependency { Name = depName, Version = depVersion });
                                    }
                                }
                            }
                        }
                        break;
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed during version/framework extraction."); }
        }

        private void AnalyzeTestFrameworks(RepoAnalysisResult result, List<string> allFiles)
        {
            try
            {
                // Check dependencies first
                var dependencies = result.Dependencies.Select(d => d.Name.ToLowerInvariant()).ToHashSet();
                
                // Java / Kotlin
                if (result.Language == RepoAnalysisResult.ProjectLanguage.Java || 
                    result.Language == RepoAnalysisResult.ProjectLanguage.Kotlin)
                {
                    // From dependencies
                    if (dependencies.Contains("junit") || result.Dependencies.Any(d => d.Name.Contains("junit-jupiter") || d.Name.Contains("junit:junit"))) 
                        result.TestFrameworks.Add("JUnit");
                    if (dependencies.Contains("testng") || result.Dependencies.Any(d => d.Name.Contains("testng"))) 
                        result.TestFrameworks.Add("TestNG");
                    if (result.Dependencies.Any(d => d.Name.Contains("kotest")))
                        result.TestFrameworks.Add("Kotest");
                    if (result.Dependencies.Any(d => d.Name.Contains("spek")))
                        result.TestFrameworks.Add("Spek");
                    if (dependencies.Contains("jacoco") || result.Dependencies.Any(d => d.Name.Contains("jacoco"))) 
                        result.CoverageTools.Add("JaCoCo");
                    
                    // From test files if no frameworks detected
                    if (!result.TestFrameworks.Any())
                    {
                        var testFiles = allFiles.Where(f => 
                            (f.Contains("/test/") || f.Contains("/tests/")) && 
                            (f.EndsWith(".java") || f.EndsWith(".kt"))).ToList();
                        
                        foreach (var testFile in testFiles.Take(10))
                        {
                            try
                            {
                                var content = File.ReadAllText(testFile);
                                if ((content.Contains("import org.junit") || content.Contains("@Test") || 
                                     content.Contains("@org.junit")) && !result.TestFrameworks.Contains("JUnit"))
                                {
                                    result.TestFrameworks.Add("JUnit");
                                }
                                if ((content.Contains("import org.testng") || content.Contains("@org.testng")) && 
                                    !result.TestFrameworks.Contains("TestNG"))
                                {
                                    result.TestFrameworks.Add("TestNG");
                                }
                                if ((content.Contains("import io.kotest") || content.Contains("import kotest")) && 
                                    !result.TestFrameworks.Contains("Kotest"))
                                {
                                    result.TestFrameworks.Add("Kotest");
                                }
                            }
                            catch { /* Skip files that can't be read */ }
                        }
                    }
                }
                // TypeScript / JavaScript
                else if (result.Language == RepoAnalysisResult.ProjectLanguage.NodeJs)
                {
                    // From dependencies
                    if (dependencies.Contains("jest")) 
                    { 
                        result.TestFrameworks.Add("Jest"); 
                        result.CoverageTools.Add("Jest Coverage"); 
                    }
                    if (dependencies.Contains("mocha")) result.TestFrameworks.Add("Mocha");
                    if (dependencies.Contains("jasmine")) result.TestFrameworks.Add("Jasmine");
                    if (dependencies.Contains("vitest")) result.TestFrameworks.Add("Vitest");
                    if (dependencies.Contains("cypress")) result.TestFrameworks.Add("Cypress");
                    if (dependencies.Contains("playwright")) result.TestFrameworks.Add("Playwright");
                    if (dependencies.Contains("nyc") || dependencies.Contains("istanbul")) 
                        result.CoverageTools.Add("Istanbul/nyc");
                    if (dependencies.Contains("c8")) result.CoverageTools.Add("c8");
                    
                    // From test files
                    if (!result.TestFrameworks.Any())
                    {
                        var testFiles = allFiles.Where(f => 
                            (f.Contains("/test/") || f.Contains("/tests/") || f.Contains("/__tests__/") ||
                             f.EndsWith(".test.ts") || f.EndsWith(".test.js") || 
                             f.EndsWith(".spec.ts") || f.EndsWith(".spec.js"))).ToList();
                        
                        foreach (var testFile in testFiles.Take(10))
                        {
                            try
                            {
                                var content = File.ReadAllText(testFile);
                                if ((content.Contains("from 'jest'") || content.Contains("from \"jest\"") ||
                                     content.Contains("describe(") || content.Contains("it(") || content.Contains("test(")) &&
                                    !result.TestFrameworks.Contains("Jest"))
                                {
                                    result.TestFrameworks.Add("Jest");
                                }
                                if ((content.Contains("from 'mocha'") || content.Contains("from \"mocha\"")) &&
                                    !result.TestFrameworks.Contains("Mocha"))
                                {
                                    result.TestFrameworks.Add("Mocha");
                                }
                                if ((content.Contains("from 'vitest'") || content.Contains("from \"vitest\"")) &&
                                    !result.TestFrameworks.Contains("Vitest"))
                                {
                                    result.TestFrameworks.Add("Vitest");
                                }
                            }
                            catch { /* Skip files that can't be read */ }
                        }
                    }
                }
                // Python
                else if (result.Language == RepoAnalysisResult.ProjectLanguage.Python)
                {
                    // From dependencies
                    if (dependencies.Contains("pytest")) result.TestFrameworks.Add("pytest");
                    if (dependencies.Contains("unittest")) result.TestFrameworks.Add("unittest");
                    if (dependencies.Contains("nose")) result.TestFrameworks.Add("nose");
                    if (dependencies.Contains("coverage") || dependencies.Contains("pytest-cov")) 
                        result.CoverageTools.Add("coverage.py");
                    
                    // From test files
                    if (!result.TestFrameworks.Any())
                    {
                        var testFiles = allFiles.Where(f => 
                            (f.Contains("/test/") || f.Contains("/tests/") ||
                             f.StartsWith("test_") || f.Contains("/test_")) && 
                            f.EndsWith(".py")).ToList();
                        
                        foreach (var testFile in testFiles.Take(10))
                        {
                            try
                            {
                                var content = File.ReadAllText(testFile);
                                if ((content.Contains("import pytest") || content.Contains("from pytest")) &&
                                    !result.TestFrameworks.Contains("pytest"))
                                {
                                    result.TestFrameworks.Add("pytest");
                                }
                                if ((content.Contains("import unittest") || content.Contains("from unittest")) &&
                                    !result.TestFrameworks.Contains("unittest"))
                                {
                                    result.TestFrameworks.Add("unittest");
                                }
                            }
                            catch { /* Skip files that can't be read */ }
                        }
                    }
                }
                // Go
                else if (result.Language == RepoAnalysisResult.ProjectLanguage.Go)
                {
                    // Go has built-in testing
                    var testFiles = allFiles.Where(f => f.EndsWith("_test.go")).ToList();
                    if (testFiles.Any())
                    {
                        result.TestFrameworks.Add("Go testing");
                        
                        // Check for additional frameworks
                        foreach (var testFile in testFiles.Take(10))
                        {
                            try
                            {
                                var content = File.ReadAllText(testFile);
                                if (content.Contains("github.com/stretchr/testify") && 
                                    !result.TestFrameworks.Contains("Testify"))
                                {
                                    result.TestFrameworks.Add("Testify");
                                }
                                if (content.Contains("github.com/onsi/ginkgo") && 
                                    !result.TestFrameworks.Contains("Ginkgo"))
                                {
                                    result.TestFrameworks.Add("Ginkgo");
                                }
                                if (content.Contains("github.com/onsi/gomega") && 
                                    !result.TestFrameworks.Contains("Gomega"))
                                {
                                    result.TestFrameworks.Add("Gomega");
                                }
                            }
                            catch { /* Skip files that can't be read */ }
                        }
                    }
                    
                    // Check for coverage
                    if (allFiles.Any(f => f.Contains("coverage.out") || f.Contains("cover.out")))
                    {
                        result.CoverageTools.Add("Go cover");
                    }
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed during test framework analysis."); }
        }

        private void AnalyzePortsAndHealthChecks(RepoAnalysisResult result, List<string> allFiles)
        {
            try
            {
                foreach (var file in allFiles)
                {
                    var content = File.ReadAllText(file);
                    // Simple port detection
                    var portMatches = Regex.Matches(content, @"\.Listen\(""*:(\d+)""\)|app\.listen\((\d+)\)|--port=(\d+)|server\.port\s*=\s*(\d+)", RegexOptions.IgnoreCase);
                    foreach (Match match in portMatches)
                    {
                        for (int i = 1; i < match.Groups.Count; i++)
                        {
                            if (match.Groups[i].Success && int.TryParse(match.Groups[i].Value, out var port))
                                result.DetectedPorts[port.ToString()] = $"Found in {Path.GetFileName(file)}";
                        }
                    }
                    // Health check detection
                    if (content.Contains("/health") || content.Contains("/healthz") || content.Contains("/live") || content.Contains("/ready"))
                        result.HealthChecks.Add($"Potential health check endpoint found in {Path.GetFileName(file)}");
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed during port/health check analysis."); }
        }

        private void AnalyzeEnvironmentVariables(RepoAnalysisResult result, List<string> allFiles)
        {
            try
            {
                var envFiles = allFiles.Where(f => Path.GetFileName(f).StartsWith(".env")).ToList();
                foreach (var file in envFiles)
                {
                    result.EnvFiles[Path.GetFileName(file)] = File.ReadAllText(file);
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed during environment variable analysis."); }
        }

        private void AnalyzeCachingStrategy(RepoAnalysisResult result)
        {
            try
            {
                switch (result.Language)
                {
                    case RepoAnalysisResult.ProjectLanguage.Java when result.BuildTools.Contains("Maven"):
                        result.CachingStrategy["Maven"] = "~/.m2/repository";
                        result.Recommendations.Add("Cache ~/.m2/repository to speed up builds.");
                        break;
                    case RepoAnalysisResult.ProjectLanguage.Java when result.BuildTools.Contains("Gradle"):
                        result.CachingStrategy["Gradle"] = "~/.gradle/caches";
                        result.Recommendations.Add("Cache ~/.gradle/caches and ~/.gradle/wrapper to speed up builds.");
                        break;
                    case RepoAnalysisResult.ProjectLanguage.Kotlin:
                        result.CachingStrategy["Gradle"] = "~/.gradle/caches";
                        result.CachingStrategy["Konan"] = "~/.konan";
                        result.Recommendations.Add("Cache ~/.gradle/caches, ~/.gradle/wrapper and ~/.konan for Kotlin builds.");
                        break;
                    case RepoAnalysisResult.ProjectLanguage.NodeJs when result.BuildTools.Contains("npm"):
                        result.CachingStrategy["npm"] = "~/.npm";
                        result.Recommendations.Add("Cache ~/.npm for npm, or the global cache directory for yarn/pnpm.");
                        break;
                    case RepoAnalysisResult.ProjectLanguage.Python:
                        result.CachingStrategy["pip"] = "~/.cache/pip";
                        result.Recommendations.Add("Cache ~/.cache/pip for faster dependency installation.");
                        break;
                    case RepoAnalysisResult.ProjectLanguage.Go:
                        result.CachingStrategy["Go"] = "$(go env GOMODCACHE)";
                        result.Recommendations.Add("Cache Go module download path.");
                        break;
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed during caching strategy analysis."); }
        }
        
        private void AnalyzeProjectQuality(RepoAnalysisResult result, string repoPath)
        {
            try
            {
                // Check for missing tests
                var testDir = Path.Combine(repoPath, "src", "test");
                var testsDir = Path.Combine(repoPath, "tests");
                var testDir2 = Path.Combine(repoPath, "test");
                
                bool hasTestDirectory = Directory.Exists(testDir) || Directory.Exists(testsDir) || Directory.Exists(testDir2);
                
                if (!result.TestFrameworks.Any())
                {
                    if (!hasTestDirectory)
                    {
                        result.Recommendations.Add("⚠️ No test directory found. Consider adding unit tests to improve code quality.");
                    }
                    else
                    {
                        result.Recommendations.Add("⚠️ Test directory exists but no test framework detected. Add JUnit or TestNG dependency.");
                    }
                    
                    // Language-specific test framework recommendations
                    switch (result.Language)
                    {
                        case RepoAnalysisResult.ProjectLanguage.Java:
                            result.Recommendations.Add("💡 Add JUnit 5 dependency: org.junit.jupiter:junit-jupiter:5.9.3");
                            break;
                        case RepoAnalysisResult.ProjectLanguage.Kotlin:
                            result.Recommendations.Add("💡 Add JUnit 5 or Kotest for testing");
                            result.Recommendations.Add("💡 Kotest: io.kotest:kotest-runner-junit5:5.5.0");
                            break;
                        case RepoAnalysisResult.ProjectLanguage.NodeJs:
                            result.Recommendations.Add("💡 Add Jest for testing: npm install --save-dev jest @types/jest");
                            result.Recommendations.Add("💡 Or use Vitest: npm install --save-dev vitest");
                            break;
                        case RepoAnalysisResult.ProjectLanguage.Python:
                            result.Recommendations.Add("💡 Add pytest for testing: pip install pytest");
                            break;
                        case RepoAnalysisResult.ProjectLanguage.Go:
                            result.Recommendations.Add("💡 Create *_test.go files using Go's built-in testing package");
                            result.Recommendations.Add("💡 Consider Testify for assertions: go get github.com/stretchr/testify");
                            break;
                    }
                }
                
                // Check for missing coverage tools
                if (!result.CoverageTools.Any())
                {
                    switch (result.Language)
                    {
                        case RepoAnalysisResult.ProjectLanguage.Java:
                        case RepoAnalysisResult.ProjectLanguage.Kotlin:
                            result.Recommendations.Add("📊 Add JaCoCo plugin for code coverage tracking");
                            break;
                        case RepoAnalysisResult.ProjectLanguage.NodeJs:
                            if (result.TestFrameworks.Contains("Jest"))
                            {
                                result.Recommendations.Add("📊 Enable Jest coverage: add --coverage flag to test script");
                            }
                            else if (result.TestFrameworks.Contains("Vitest"))
                            {
                                result.Recommendations.Add("📊 Enable Vitest coverage: vitest --coverage");
                            }
                            else
                            {
                                result.Recommendations.Add("📊 Add nyc/istanbul for code coverage: npm install --save-dev nyc");
                            }
                            break;
                        case RepoAnalysisResult.ProjectLanguage.Python:
                            result.Recommendations.Add("📊 Add coverage.py: pip install coverage pytest-cov");
                            break;
                        case RepoAnalysisResult.ProjectLanguage.Go:
                            result.Recommendations.Add("📊 Use Go's built-in coverage: go test -cover ./...");
                            result.Recommendations.Add("📊 Generate coverage report: go test -coverprofile=coverage.out");
                            break;
                    }
                }
                
                // Check for minimal dependencies (sample/educational project indicator)
                if (result.Dependencies.Count == 0)
                {
                    result.Recommendations.Add("ℹ️ No dependencies found. This might be a very basic or educational project.");
                }
                else if (result.Dependencies.Count == 1 && result.Dependencies.Any(d => d.Name.ToLowerInvariant().Contains("junit")))
                {
                    result.Recommendations.Add("ℹ️ Only test dependencies found. Consider if production dependencies are needed.");
                }
                
                // Check for missing README or documentation
                var readmeFiles = Directory.GetFiles(repoPath, "README*", SearchOption.TopDirectoryOnly);
                if (readmeFiles.Length == 0)
                {
                    result.Recommendations.Add("📝 Add README.md file to document the project");
                }
                
                // Check for missing CI/CD configuration
                var hasCiConfig = Directory.GetFiles(repoPath, ".gitlab-ci.yml", SearchOption.TopDirectoryOnly).Any() ||
                                 Directory.GetFiles(repoPath, "Jenkinsfile", SearchOption.TopDirectoryOnly).Any() ||
                                 Directory.Exists(Path.Combine(repoPath, ".github", "workflows"));
                
                if (!hasCiConfig)
                {
                    result.Recommendations.Add("🔧 No CI/CD configuration detected. Generated pipelines can be used to set up automation.");
                }
            }
            catch (Exception ex) 
            { 
                _logger.LogWarning(ex, "Failed during project quality analysis."); 
            }
        }
        // <<< End of deep analysis methods
    }
}
