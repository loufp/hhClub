namespace Ci_Cd.Models
{
    public class RepoAnalysisResult
    {
        public enum ProjectLanguage
        {
            Unknown, DotNet, NodeJs, Go, Python, Java, Kotlin, Rust, Cpp, Php, Ruby, Elixir
        }

        public ProjectLanguage Language { get; set; } = ProjectLanguage.Unknown;
        public string Framework { get; set; } = "Unknown";
        public string ProjectName { get; set; } = "unnamed-project";
        public string ProjectVersion { get; set; } = "1.0.0";
        
        public bool HasDockerfile { get; set; }
        public string? DockerfilePath { get; set; }
        public bool DockerfileGenerated { get; set; }

        public List<string> BuildCommands { get; } = new();
        public List<string> TestCommands { get; } = new();
        public List<Dependency> Dependencies { get; } = new();
        public List<int> ExposedPorts { get; } = new();
        public Dictionary<string, string> EnvironmentVariables { get; } = new();
        public string Rationale { get; set; } = string.Empty;
        public List<string> Subprojects { get; } = new();

        // >>> Deep analysis properties
        public string LanguageVersion { get; set; } = string.Empty;
        public List<string> DetectedFrameworks { get; } = new();
        public List<string> TestFrameworks { get; } = new();
        public List<string> CoverageTools { get; } = new();
        public List<string> BuildTools { get; } = new();
        public Dictionary<string, string> DetectedPorts { get; } = new();
        public List<string> HealthChecks { get; } = new();
        public Dictionary<string, string> EnvFiles { get; } = new();
        public Dictionary<string, string> CachingStrategy { get; } = new();
        public List<string> Recommendations { get; } = new();
        // <<< End of deep analysis properties

        public string GetBestImage()
        {
            return Language switch
            {
                ProjectLanguage.DotNet => GetDotNetImage(),
                ProjectLanguage.NodeJs => GetNodeImage(),
                ProjectLanguage.Go => GetGoImage(),
                ProjectLanguage.Python => GetPythonImage(),
                ProjectLanguage.Java => GetJavaImage(),
                ProjectLanguage.Kotlin => GetKotlinImage(),
                ProjectLanguage.Rust => "rust:1.78",
                _ => "ubuntu:latest"
            };
        }

        private string GetDotNetImage()
        {
            if (!string.IsNullOrEmpty(LanguageVersion))
            {
                return LanguageVersion switch
                {
                    "6.0" => "mcr.microsoft.com/dotnet/sdk:6.0",
                    "7.0" => "mcr.microsoft.com/dotnet/sdk:7.0",
                    "8.0" => "mcr.microsoft.com/dotnet/sdk:8.0",
                    _ => "mcr.microsoft.com/dotnet/sdk:8.0"
                };
            }
            return "mcr.microsoft.com/dotnet/sdk:8.0";
        }

        private string GetNodeImage()
        {
            if (!string.IsNullOrEmpty(LanguageVersion))
            {
                var version = LanguageVersion.Replace(">=", "").Replace("^", "").Split('.')[0];
                return version switch
                {
                    "16" => "node:16-alpine",
                    "18" => "node:18-alpine",
                    "20" => "node:20-alpine",
                    "21" => "node:21-alpine",
                    _ => "node:20-alpine"
                };
            }
            return "node:20-alpine";
        }

        private string GetGoImage()
        {
            if (!string.IsNullOrEmpty(LanguageVersion))
            {
                return $"golang:{LanguageVersion}";
            }
            return "golang:1.21";
        }

        private string GetPythonImage()
        {
            if (!string.IsNullOrEmpty(LanguageVersion))
            {
                var version = LanguageVersion.Replace(">=", "").Replace("^", "").Split('.').Take(2);
                var majorMinor = string.Join(".", version);
                return $"python:{majorMinor}-slim";
            }
            return "python:3.11-slim";
        }

        private string GetJavaImage()
        {
            if (!string.IsNullOrEmpty(LanguageVersion))
            {
                var javaVer = LanguageVersion switch
                {
                    "8" => "8",
                    "11" => "11", 
                    "17" => "17",
                    "21" => "21",
                    _ => "17"
                };
                
                if (BuildTools.Contains("Gradle"))
                    return $"gradle:8.5-jdk{javaVer}";
                else
                    return $"maven:3.9-eclipse-temurin-{javaVer}";
            }
            return "maven:3.9-eclipse-temurin-17";
        }

        private string GetKotlinImage()
        {
            if (!string.IsNullOrEmpty(LanguageVersion))
            {
                var javaVer = LanguageVersion switch
                {
                    "8" => "8",
                    "11" => "11", 
                    "17" => "17",
                    "21" => "21",
                    _ => "17"
                };
                
                // Kotlin always uses Gradle
                return $"gradle:8.5-jdk{javaVer}";
            }
            return "gradle:8.5-jdk17";
        }
    }

    public class Dependency
    {
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }
}
