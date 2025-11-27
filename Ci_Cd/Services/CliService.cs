using System.Text;
using System.Text.Json;
using Ci_Cd.Models;
using Ci_Cd.Services;
using Microsoft.Extensions.Logging;

namespace Ci_Cd.Services
{
    public interface IInteractiveCli
    {
        Task<CliOptions> PromptForOptions();
        void DisplayMenu();
        void DisplayResults(CliExecutionResult result);
        bool ConfirmAction(string message);
    }

    public interface IConfigurationService
    {
        CiCdConfig LoadConfig(string filePath);
        void SaveConfig(string filePath, CiCdConfig config);
        CiCdConfig CreateDefaultConfig();
    }

    public class InteractiveCli : IInteractiveCli
    {
        private readonly ILogger<InteractiveCli> _logger;

        public InteractiveCli(ILogger<InteractiveCli> logger)
        {
            _logger = logger;
        }

        public async Task<CliOptions> PromptForOptions()
        {
            Console.Clear();
            Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║   CI/CD Pipeline Generator - Interactive CLI               ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            var options = new CliOptions();

            // Repository selection
            Console.Write("📁 Enter repository URL or local path: ");
            options.RepositoryPath = Console.ReadLine() ?? "";
            
            if (string.IsNullOrWhiteSpace(options.RepositoryPath))
            {
                _logger.LogError("Repository path is required");
                return options;
            }

            // Output directory
            Console.Write("📂 Output directory (default: ./output): ");
            var outputDir = Console.ReadLine();
            options.OutputDirectory = string.IsNullOrWhiteSpace(outputDir) 
                ? "./output" 
                : outputDir;

            // Generation options
            DisplayMenu();
            Console.Write("Select option (1-5): ");
            var choice = Console.ReadLine();

            options.GenerateGitLabCI = choice is "1" or "3" or "5";
            options.GenerateJenkinsfile = choice is "2" or "3" or "5";
            options.GenerateDocker = choice != "4";
            options.GenerateReports = true;

            // Validation options
            Console.WriteLine("\n🔍 Validation Options:");
            options.DryRun = ConfirmAction("Run in dry-run mode (preview only)?");
            options.ValidateOnly = ConfirmAction("Validate only (no generation)?");
            options.Verbose = ConfirmAction("Enable verbose output?");

            // Load configuration if exists
            var configPath = Path.Combine(options.OutputDirectory, ".cicd-config.yml");
            if (File.Exists(configPath))
            {
                options.UseConfig = ConfirmAction($"Use existing config from {configPath}?");
                options.ConfigPath = configPath;
            }

            options.Execute = ConfirmAction("Execute build/tests after generation?");
            options.CreateZipArchive = ConfirmAction("Create zip archive of output?");

            return options;
        }

        public void DisplayMenu()
        {
            Console.WriteLine("\n📋 Generation Options:");
            Console.WriteLine("  1. GitLab CI only");
            Console.WriteLine("  2. Jenkins only");
            Console.WriteLine("  3. Both GitLab + Jenkins");
            Console.WriteLine("  4. Docker only");
            Console.WriteLine("  5. Full (GitLab + Jenkins + Docker)");
        }

        public void DisplayResults(CliExecutionResult result)
        {
            Console.WriteLine("\n╔════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                   GENERATION RESULTS                       ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            if (result.Success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✅ Generation completed successfully!");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("❌ Generation failed!");
                Console.ResetColor();
            }

            Console.WriteLine();
            Console.WriteLine("📊 Generated Files:");
            foreach (var file in result.GeneratedFiles)
            {
                Console.WriteLine($"  ✓ {file}");
            }

            if (result.Validations.Any())
            {
                Console.WriteLine("\n✅ Validation Results:");
                foreach (var validation in result.Validations)
                {
                    var icon = validation.IsValid ? "✓" : "✗";
                    var color = validation.IsValid ? ConsoleColor.Green : ConsoleColor.Red;
                    Console.ForegroundColor = color;
                    Console.WriteLine($"  {icon} {validation.Name}: {validation.Message}");
                    Console.ResetColor();
                }
            }

            if (result.Warnings.Any())
            {
                Console.WriteLine("\n⚠️  Warnings:");
                foreach (var warning in result.Warnings)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  ⚠ {warning}");
                    Console.ResetColor();
                }
            }

            if (result.Recommendations.Any())
            {
                Console.WriteLine("\n💡 Recommendations:");
                foreach (var rec in result.Recommendations)
                {
                    Console.WriteLine($"  • {rec}");
                }
            }

            Console.WriteLine();
            Console.WriteLine($"📁 Output directory: {result.OutputDirectory}");
            Console.WriteLine($"⏱️  Execution time: {result.ExecutionTime.TotalSeconds:F2}s");
        }

        public bool ConfirmAction(string message)
        {
            while (true)
            {
                Console.Write($"{message} (y/n): ");
                var response = Console.ReadLine()?.ToLower();
                
                if (response == "y" || response == "yes") return true;
                if (response == "n" || response == "no") return false;
                
                Console.WriteLine("Please enter 'y' or 'n'");
            }
        }
    }

    public class ConfigurationService : IConfigurationService
    {
        private readonly ILogger<ConfigurationService> _logger;

        public ConfigurationService(ILogger<ConfigurationService> logger)
        {
            _logger = logger;
        }

        public CiCdConfig LoadConfig(string filePath)
        {
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("Config file not found: {path}", filePath);
                return CreateDefaultConfig();
            }

            try
            {
                var content = File.ReadAllText(filePath);
                // Parse YAML (simplified - would use YamlDotNet in production)
                var config = ParseYamlConfig(content);
                _logger.LogInformation("Config loaded from {path}", filePath);
                return config;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load config from {path}", filePath);
                return CreateDefaultConfig();
            }
        }

        public void SaveConfig(string filePath, CiCdConfig config)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var yaml = GenerateYamlConfig(config);
                File.WriteAllText(filePath, yaml);
                _logger.LogInformation("Config saved to {path}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save config to {path}", filePath);
                throw;
            }
        }

        public CiCdConfig CreateDefaultConfig()
        {
            return new CiCdConfig
            {
                ProjectName = "my-project",
                GenerateGitLabCI = true,
                GenerateJenkinsfile = true,
                GenerateDocker = true,
                ExecuteTests = false,
                SonarQubeEnabled = true,
                NexusEnabled = true,
                DockerRegistryEnabled = true,
                SecretManager = "vault",
                RequiredSecrets = new[] 
                { 
                    "NEXUS_USER", "NEXUS_PASSWORD", 
                    "NPM_TOKEN", "DOCKER_TOKEN", "SONAR_TOKEN" 
                }
            };
        }

        private CiCdConfig ParseYamlConfig(string yaml)
        {
            var config = CreateDefaultConfig();
            
            var lines = yaml.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains("projectName:"))
                    config.ProjectName = ExtractValue(line);
                if (line.Contains("generateGitLabCI:"))
                    config.GenerateGitLabCI = ExtractBool(line);
                if (line.Contains("generateJenkinsfile:"))
                    config.GenerateJenkinsfile = ExtractBool(line);
                if (line.Contains("secretManager:"))
                    config.SecretManager = ExtractValue(line);
            }

            return config;
        }

        private string GenerateYamlConfig(CiCdConfig config)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# CI/CD Configuration");
            sb.AppendLine($"projectName: {config.ProjectName}");
            sb.AppendLine($"generateGitLabCI: {(config.GenerateGitLabCI ? "true" : "false")}");
            sb.AppendLine($"generateJenkinsfile: {(config.GenerateJenkinsfile ? "true" : "false")}");
            sb.AppendLine($"generateDocker: {(config.GenerateDocker ? "true" : "false")}");
            sb.AppendLine($"executeTests: {(config.ExecuteTests ? "true" : "false")}");
            sb.AppendLine();
            sb.AppendLine("integrations:");
            sb.AppendLine($"  sonarqube: {(config.SonarQubeEnabled ? "true" : "false")}");
            sb.AppendLine($"  nexus: {(config.NexusEnabled ? "true" : "false")}");
            sb.AppendLine($"  dockerRegistry: {(config.DockerRegistryEnabled ? "true" : "false")}");
            sb.AppendLine();
            sb.AppendLine("security:");
            sb.AppendLine($"  secretManager: {config.SecretManager}");
            sb.AppendLine("  requiredSecrets:");
            foreach (var secret in config.RequiredSecrets)
                sb.AppendLine($"    - {secret}");

            return sb.ToString();
        }

        private static string ExtractValue(string line)
        {
            var parts = line.Split(':');
            return parts.Length > 1 ? parts[1].Trim().Trim('"', '\'') : "";
        }

        private static bool ExtractBool(string line)
        {
            return line.Contains("true");
        }
    }

    public class CliOptions
    {
        public string RepositoryPath { get; set; } = "";
        public string OutputDirectory { get; set; } = "./output";
        public string? ConfigPath { get; set; }
        public bool UseConfig { get; set; }
        public bool GenerateGitLabCI { get; set; }
        public bool GenerateJenkinsfile { get; set; }
        public bool GenerateDocker { get; set; }
        public bool GenerateReports { get; set; }
        public bool DryRun { get; set; }
        public bool ValidateOnly { get; set; }
        public bool Verbose { get; set; }
        public bool Execute { get; set; }
        public bool CreateZipArchive { get; set; }
    }

    public class CliExecutionResult
    {
        public bool Success { get; set; }
        public List<string> GeneratedFiles { get; } = new();
        public List<ValidationItem> Validations { get; } = new();
        public List<string> Warnings { get; } = new();
        public List<string> Recommendations { get; } = new();
        public string OutputDirectory { get; set; } = "";
        public TimeSpan ExecutionTime { get; set; }
    }

    public class ValidationItem
    {
        public string Name { get; set; } = "";
        public bool IsValid { get; set; }
        public string Message { get; set; } = "";
    }

    public class CiCdConfig
    {
        public string ProjectName { get; set; } = "";
        public bool GenerateGitLabCI { get; set; }
        public bool GenerateJenkinsfile { get; set; }
        public bool GenerateDocker { get; set; }
        public bool ExecuteTests { get; set; }
        public bool SonarQubeEnabled { get; set; }
        public bool NexusEnabled { get; set; }
        public bool DockerRegistryEnabled { get; set; }
        public string SecretManager { get; set; } = "vault";
        public string[] RequiredSecrets { get; set; } = Array.Empty<string>();
    }
}

