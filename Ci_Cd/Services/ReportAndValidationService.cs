using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ci_Cd.Models;

namespace Ci_Cd.Services
{
    public interface IPipelineValidator
    {
        ValidationResult ValidateGitLabCi(string yaml);
        ValidationResult ValidateJenkinsfile(string groovy);
        string GenerateDiffPreview(string original, string generated);
    }

    public interface IReportGenerator
    {
        string GenerateJsonReport(RepoAnalysisResult analysis, ArtifactInfo artifacts);
        string GenerateYamlReport(RepoAnalysisResult analysis, ArtifactInfo artifacts);
        string GenerateRecommendations(RepoAnalysisResult analysis);
        string GenerateDependencyTree(RepoAnalysisResult analysis);
    }

    public class PipelineValidator : IPipelineValidator
    {
        public ValidationResult ValidateGitLabCi(string yaml)
        {
            var result = new ValidationResult { IsValid = true };

            // Проверка обязательных секций
            var hasStages = yaml.Contains("stages:");
            var hasJobs = Regex.IsMatch(yaml, @"^\w+:\s*$", RegexOptions.Multiline);
            
            if (!hasStages)
            {
                result.Errors.Add("Missing 'stages:' section");
                result.IsValid = false;
            }

            if (!hasJobs)
            {
                result.Warnings.Add("No jobs defined");
            }

            // Валидация синтаксиса YAML
            try
            {
                var yamlLines = yaml.Split('\n');
                
                foreach (var line in yamlLines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var indent = line.Length - line.TrimStart().Length;
                    
                    // Проверка отступов
                    if (indent % 2 != 0 && !line.TrimStart().StartsWith("#"))
                    {
                        result.Warnings.Add($"Irregular indentation on line: {line.Trim()}");
                    }

                    // Проверка валидных ключей
                    var keyMatch = Regex.Match(line, @"^(\s*)([a-zA-Z_][a-zA-Z0-9_-]*):");
                    if (keyMatch.Success)
                    {
                        var key = keyMatch.Groups[2].Value;
                        var validKeys = new[] { 
                            "stages", "variables", "cache", "image", "script", 
                            "artifacts", "rules", "needs", "environment", "deploy",
                            "when", "only", "except", "tags", "retry", "timeout",
                            "coverage", "allow_failure", "interruptible"
                        };

                        if (!validKeys.Contains(key) && !yaml.Contains(key + ":"))
                        {
                            result.Warnings.Add($"Unknown key: {key}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"YAML parsing error: {ex.Message}");
                result.IsValid = false;
            }

            return result;
        }

        public ValidationResult ValidateJenkinsfile(string groovy)
        {
            var result = new ValidationResult { IsValid = true };

            // Проверка обязательных элементов
            if (!groovy.Contains("pipeline {"))
            {
                result.Errors.Add("Missing 'pipeline' block");
                result.IsValid = false;
            }

            if (!groovy.Contains("stages {"))
            {
                result.Errors.Add("Missing 'stages' block");
                result.IsValid = false;
            }

            if (!groovy.Contains("stage("))
            {
                result.Errors.Add("No stages defined");
                result.IsValid = false;
            }

            // Проверка синтаксиса Groovy
            var bracketCount = groovy.Count(c => c == '{') - groovy.Count(c => c == '}');
            if (bracketCount != 0)
            {
                result.Errors.Add($"Unbalanced braces (difference: {bracketCount})");
                result.IsValid = false;
            }

            var parenCount = groovy.Count(c => c == '(') - groovy.Count(c => c == ')');
            if (parenCount != 0)
            {
                result.Errors.Add($"Unbalanced parentheses (difference: {parenCount})");
                result.IsValid = false;
            }

            // Проверка цитирования строк
            var stringLiterals = Regex.Matches(groovy, @"""([^""]|\\"")""");
            if (stringLiterals.Count == 0)
            {
                result.Warnings.Add("No string literals found - might indicate missing quotes");
            }

            return result;
        }

        public string GenerateDiffPreview(string original, string generated)
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════════");
            sb.AppendLine("DIFF PREVIEW - Changes to be applied");
            sb.AppendLine("═══════════════════════════════════════════════════");
            sb.AppendLine();

            var originalLines = string.IsNullOrEmpty(original) ? new List<string>() : original.Split('\n').ToList();
            var generatedLines = generated.Split('\n').ToList();

            var maxLines = Math.Max(originalLines.Count, generatedLines.Count);

            for (int i = 0; i < maxLines; i++)
            {
                var origLine = i < originalLines.Count ? originalLines[i] : "";
                var genLine = i < generatedLines.Count ? generatedLines[i] : "";

                if (origLine != genLine)
                {
                    if (string.IsNullOrEmpty(origLine))
                    {
                        sb.AppendLine($"  + {genLine}");
                    }
                    else if (string.IsNullOrEmpty(genLine))
                    {
                        sb.AppendLine($"  - {origLine}");
                    }
                    else
                    {
                        sb.AppendLine($"  - {origLine}");
                        sb.AppendLine($"  + {genLine}");
                    }
                }
                else if (!string.IsNullOrWhiteSpace(origLine))
                {
                    sb.AppendLine($"    {origLine}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════════════");

            return sb.ToString();
        }
    }

    public class ReportGenerator : IReportGenerator
    {
        public string GenerateJsonReport(RepoAnalysisResult analysis, ArtifactInfo artifacts)
        {
            var report = new
            {
                metadata = new
                {
                    timestamp = DateTime.UtcNow.ToString("O"),
                    projectName = analysis.ProjectName,
                    projectVersion = analysis.ProjectVersion,
                    language = analysis.Language.ToString(),
                    framework = analysis.Framework
                },
                analysis = new
                {
                    languageVersion = analysis.LanguageVersion,
                    detectedFrameworks = analysis.DetectedFrameworks,
                    buildTools = analysis.BuildTools,
                    testFrameworks = analysis.TestFrameworks,
                    coverageTools = analysis.CoverageTools,
                    detectedPorts = analysis.DetectedPorts,
                    healthChecks = analysis.HealthChecks,
                    subprojects = analysis.Subprojects.Count
                },
                artifacts = new
                {
                    type = artifacts.ArtifactType,
                    path = artifacts.ArtifactPath,
                    repository = artifacts.RepositoryType,
                    dockerImage = artifacts.DockerImage
                },
                dependencies = new
                {
                    count = analysis.Dependencies.Count,
                    items = analysis.Dependencies.Select(d => new { d.Name, d.Version })
                },
                caching = analysis.CachingStrategy,
                recommendations = analysis.Recommendations
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            return JsonSerializer.Serialize(report, options);
        }

        public string GenerateYamlReport(RepoAnalysisResult analysis, ArtifactInfo artifacts)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("# Repository Analysis Report (YAML format)");
            sb.AppendLine($"timestamp: '{DateTime.UtcNow:O}'");
            sb.AppendLine();
            
            sb.AppendLine("metadata:");
            sb.AppendLine($"  projectName: '{EscapeYamlString(analysis.ProjectName)}'");
            sb.AppendLine($"  projectVersion: '{EscapeYamlString(analysis.ProjectVersion)}'");
            sb.AppendLine($"  language: {analysis.Language}");
            sb.AppendLine($"  framework: '{EscapeYamlString(analysis.Framework)}'");
            sb.AppendLine();

            sb.AppendLine("analysis:");
            var languageVersion = string.IsNullOrEmpty(analysis.LanguageVersion) ? "" : analysis.LanguageVersion;
            sb.AppendLine($"  languageVersion: '{EscapeYamlString(languageVersion)}'");
            
            // Правильные YAML списки с обработкой пустых коллекций
            if (analysis.DetectedFrameworks.Any())
            {
                sb.AppendLine("  detectedFrameworks:");
                foreach (var fw in analysis.DetectedFrameworks)
                    sb.AppendLine($"    - '{EscapeYamlString(fw)}'");
            }
            else
            {
                sb.AppendLine("  detectedFrameworks: []");
            }
            
            if (analysis.BuildTools.Any())
            {
                sb.AppendLine("  buildTools:");
                foreach (var tool in analysis.BuildTools)
                    sb.AppendLine($"    - '{EscapeYamlString(tool)}'");
            }
            else
            {
                sb.AppendLine("  buildTools: []");
            }
            
            if (analysis.TestFrameworks.Any())
            {
                sb.AppendLine("  testFrameworks:");
                foreach (var test in analysis.TestFrameworks)
                    sb.AppendLine($"    - '{EscapeYamlString(test)}'");
            }
            else
            {
                sb.AppendLine("  testFrameworks: []");
            }
            
            if (analysis.CoverageTools.Any())
            {
                sb.AppendLine("  coverageTools:");
                foreach (var cov in analysis.CoverageTools)
                    sb.AppendLine($"    - '{EscapeYamlString(cov)}'");
            }
            else
            {
                sb.AppendLine("  coverageTools: []");
            }
            
            if (analysis.DetectedPorts.Any())
            {
                sb.AppendLine("  detectedPorts:");
                foreach (var port in analysis.DetectedPorts)
                    sb.AppendLine($"    {port.Key}: '{EscapeYamlString(port.Value)}'");
            }
            else
            {
                sb.AppendLine("  detectedPorts: {}");
            }
            
            if (analysis.HealthChecks.Any())
            {
                sb.AppendLine("  healthChecks:");
                foreach (var check in analysis.HealthChecks)
                    sb.AppendLine($"    - '{EscapeYamlString(check)}'");
            }
            else
            {
                sb.AppendLine("  healthChecks: []");
            }
            
            sb.AppendLine($"  subprojects: {analysis.Subprojects.Count}");
            sb.AppendLine();

            sb.AppendLine("artifacts:");
            sb.AppendLine($"  type: '{EscapeYamlString(artifacts.ArtifactType)}'");
            sb.AppendLine($"  path: '{EscapeYamlString(artifacts.ArtifactPath)}'");
            sb.AppendLine($"  repository: '{EscapeYamlString(artifacts.RepositoryType)}'");
            sb.AppendLine($"  dockerImage: '{EscapeYamlString(artifacts.DockerImage)}'");
            sb.AppendLine();

            sb.AppendLine("dependencies:");
            sb.AppendLine($"  count: {analysis.Dependencies.Count}");
            if (analysis.Dependencies.Any())
            {
                sb.AppendLine("  items:");
                foreach (var dep in analysis.Dependencies.Take(20))
                {
                    var version = string.IsNullOrEmpty(dep.Version) ? "latest" : dep.Version;
                    sb.AppendLine($"    - name: '{EscapeYamlString(dep.Name)}'");
                    sb.AppendLine($"      version: '{EscapeYamlString(version)}'");
                }
                if (analysis.Dependencies.Count > 20)
                    sb.AppendLine($"  # ... and {analysis.Dependencies.Count - 20} more dependencies");
            }
            else
            {
                sb.AppendLine("  items: []");
            }
            sb.AppendLine();

            if (analysis.CachingStrategy.Any())
            {
                sb.AppendLine("caching:");
                foreach (var cache in analysis.CachingStrategy)
                    sb.AppendLine($"  {EscapeYamlString(cache.Key)}: '{EscapeYamlString(cache.Value)}'");
            }
            else
            {
                sb.AppendLine("caching: {}");
            }
            sb.AppendLine();

            if (analysis.Recommendations.Any())
            {
                sb.AppendLine("recommendations:");
                foreach (var rec in analysis.Recommendations)
                    sb.AppendLine($"  - '{EscapeYamlString(rec)}'");
            }
            else
            {
                sb.AppendLine("recommendations: []");
            }

            return sb.ToString();
        }

        private static string EscapeYamlString(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "";
            
            return input
                .Replace("'", "''")     // Escape single quotes
                .Replace("\\", "\\\\")  // Escape backslashes
                .Replace("\n", "\\n")   // Escape newlines
                .Replace("\r", "\\r");  // Escape carriage returns
        }

        public string GenerateRecommendations(RepoAnalysisResult analysis)
        {
            var recommendations = new List<string>();

            // Security recommendations
            if (!analysis.TestFrameworks.Any())
                recommendations.Add("⚠️  WARNING: No test frameworks detected. Add unit tests for better code quality.");

            if (!analysis.CoverageTools.Any())
                recommendations.Add("💡 RECOMMENDATION: Add code coverage reporting (JaCoCo for Java, coverage.py for Python, etc.)");

            // Build optimization
            if (analysis.Language == RepoAnalysisResult.ProjectLanguage.Java && !analysis.BuildTools.Contains("Gradle"))
                recommendations.Add("💡 SUGGESTION: Consider using Gradle for faster parallel builds.");

            // Security best practices
            if (!analysis.HealthChecks.Any())
                recommendations.Add("🔧 TODO: Add health check endpoints for container orchestration.");

            if (analysis.DetectedPorts.Count == 0)
                recommendations.Add("⚠️  WARNING: No ports detected. Ensure your application exposes required ports.");

            // Monorepo optimization
            if (analysis.Subprojects.Count > 0)
                recommendations.Add($"✅ DETECTED: Monorepo with {analysis.Subprojects.Count} subprojects. Matrix builds will be used for parallelization.");

            // Documentation
            if (analysis.Dependencies.Count > 50)
                recommendations.Add("📝 SUGGESTION: Document your dependencies in DEPENDENCIES.md for clarity.");

            // Docker optimization
            if (analysis.Language == RepoAnalysisResult.ProjectLanguage.Go)
                recommendations.Add("✅ EXCELLENT: Go projects benefit from distroless images (20MB vs 800MB standard).");

            // Version management
            recommendations.Add("📦 ACTION: Ensure semantic versioning with git tags (v1.2.3) for consistent releases.");

            return string.Join("\n", recommendations);
        }

        public string GenerateDependencyTree(RepoAnalysisResult analysis)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Dependency Tree");
            sb.AppendLine("════════════════════════════════════════════════════════════");
            sb.AppendLine();

            sb.AppendLine($"📦 {analysis.ProjectName} v{analysis.ProjectVersion}");
            sb.AppendLine($"├─ Language: {analysis.Language}");
            sb.AppendLine($"├─ Framework: {analysis.Framework}");
            sb.AppendLine($"├─ Build Tools: {string.Join(", ", analysis.BuildTools)}");
            sb.AppendLine();

            sb.AppendLine("Dependencies (top 20):");
            var sortedDeps = analysis.Dependencies.OrderBy(d => d.Name).Take(20).ToList();
            for (int i = 0; i < sortedDeps.Count; i++)
            {
                var dep = sortedDeps[i];
                var isLast = i == sortedDeps.Count - 1;
                var prefix = isLast ? "└──" : "├──";
                sb.AppendLine($"{prefix} {dep.Name}@{dep.Version}");
            }

            if (analysis.Dependencies.Count > 20)
                sb.AppendLine($"└── ... and {analysis.Dependencies.Count - 20} more dependencies");

            return sb.ToString();
        }
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();
    }
}

