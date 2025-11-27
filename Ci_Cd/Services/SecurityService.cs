using System.Text;
using System.Text.RegularExpressions;
using Ci_Cd.Models;
using Microsoft.Extensions.Logging;

namespace Ci_Cd.Services
{
    public interface ISecurityService
    {
        SecretAuditResult AuditProject(string repoPath);
        SecretValidationResult ValidateRequiredSecrets(RepoAnalysisResult analysis, string[] requiredSecrets);
        List<string> GenerateSecretManagerRecommendations(RepoAnalysisResult analysis);
        SecretIntegrationGuide GenerateSecretIntegration(string secretManager);
    }

    public class SecurityService : ISecurityService
    {
        private readonly ILogger<SecurityService> _logger;
        private static readonly string[] CommonSecretPatterns = new[]
        {
            @"password\s*=\s*[""']([^""']+)[""']",
            @"api[_-]?key\s*=\s*[""']([^""']+)[""']",
            @"secret\s*=\s*[""']([^""']+)[""']",
            @"token\s*=\s*[""']([^""']+)[""']",
            @"aws_secret_access_key\s*=\s*[""']([^""']+)[""']",
            @"PRIVATE[_-]?KEY\s*[=:]\s*[""']?([A-Za-z0-9/+=]+)",
            @"mongodb://[^\s:]+:[^\s@]+@",
            @"postgres://[^\s:]+:[^\s@]+@",
            @"github_token[""']?\s*[:=]\s*[""']([^""']+)[""']"
        };

        public SecurityService(ILogger<SecurityService> logger)
        {
            _logger = logger;
        }

        public SecretAuditResult AuditProject(string repoPath)
        {
            var result = new SecretAuditResult
            {
                ProjectPath = repoPath,
                AuditedAt = DateTime.UtcNow
            };

            try
            {
                var files = Directory.GetFiles(repoPath, "*", SearchOption.AllDirectories)
                    .Where(f => !IsIgnoredPath(f))
                    .ToList();

                foreach (var file in files)
                {
                    try
                    {
                        if (IsTextFile(file))
                        {
                            var content = File.ReadAllText(file);
                            var secrets = FindSecrets(content, file);
                            result.SecretsFound.AddRange(secrets);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to audit file: {file}", file);
                    }
                }

                result.IsSafe = result.SecretsFound.Count == 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Secret audit failed");
                result.Errors.Add(ex.Message);
            }

            return result;
        }

        public SecretValidationResult ValidateRequiredSecrets(RepoAnalysisResult analysis, string[] requiredSecrets)
        {
            var result = new SecretValidationResult();

            foreach (var secret in requiredSecrets)
            {
                var validation = new SecretValidation
                {
                    SecretName = secret,
                    Status = "MISSING",
                    Fallback = SecretManagerDefaults.GetFallbackForSecret(secret)
                };

                // Check in environment
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(secret)))
                {
                    validation.Status = "SET";
                    validation.Source = "ENVIRONMENT";
                }

                // Check in config files
                if (CheckSecretInConfig(secret))
                {
                    validation.Status = "CONFIGURED";
                    validation.Source = "CONFIG";
                }

                result.SecretValidations.Add(validation);
            }

            result.AllValid = result.SecretValidations.All(s => s.Status != "MISSING");
            return result;
        }

        public List<string> GenerateSecretManagerRecommendations(RepoAnalysisResult analysis)
        {
            var recommendations = new List<string>();

            if (analysis.Language == RepoAnalysisResult.ProjectLanguage.Java)
            {
                recommendations.Add("🔐 Java: Use Spring Cloud Config Server for centralized secret management");
                recommendations.Add("🔐 Java: Integrate with HashiCorp Vault for dynamic secrets");
                recommendations.Add("🔐 Java: Consider AWS Secrets Manager for AWS-hosted deployments");
            }
            else if (analysis.Language == RepoAnalysisResult.ProjectLanguage.NodeJs)
            {
                recommendations.Add("🔐 Node.js: Use dotenv for local development secrets");
                recommendations.Add("🔐 Node.js: Integrate with AWS Secrets Manager client");
                recommendations.Add("🔐 Node.js: Consider Vault Go client for HashiCorp integration");
            }
            else if (analysis.Language == RepoAnalysisResult.ProjectLanguage.Python)
            {
                recommendations.Add("🔐 Python: Use python-dotenv for local configuration");
                recommendations.Add("🔐 Python: Integrate hvac library for HashiCorp Vault");
                recommendations.Add("🔐 Python: Consider boto3 for AWS Secrets Manager");
            }

            recommendations.Add("🔐 Enable secret scanning in CI/CD pipeline");
            recommendations.Add("🔐 Use GitOps with encrypted secrets (sealed-secrets, external-secrets-operator)");
            recommendations.Add("🔐 Rotate secrets regularly (at least quarterly)");

            return recommendations;
        }

        public SecretIntegrationGuide GenerateSecretIntegration(string secretManager)
        {
            var guide = new SecretIntegrationGuide
            {
                SecretManager = secretManager,
                GeneratedAt = DateTime.UtcNow
            };

            guide.ConfigurationSteps = secretManager switch
            {
                "vault" => GenerateVaultIntegration(),
                "aws-secrets-manager" => GenerateAwsSecretsManagerIntegration(),
                "gitlab-secrets" => GenerateGitLabSecretsIntegration(),
                "kubernetes-secrets" => GenerateK8sSecretsIntegration(),
                _ => new List<string> { "Unknown secret manager" }
            };

            return guide;
        }

        private List<FoundSecret> FindSecrets(string content, string filePath)
        {
            var secrets = new List<FoundSecret>();

            foreach (var pattern in CommonSecretPatterns)
            {
                try
                {
                    var matches = Regex.Matches(content, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                    foreach (Match match in matches)
                    {
                        var lineNumber = content.Substring(0, match.Index).Count(c => c == '\n') + 1;
                        secrets.Add(new FoundSecret
                        {
                            FilePath = filePath,
                            LineNumber = lineNumber,
                            Pattern = pattern,
                            Severity = DetermineSeverity(match.Value)
                        });
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    _logger.LogWarning("Regex timeout on pattern: {pattern}", pattern);
                }
            }

            return secrets;
        }

        private bool IsIgnoredPath(string path)
        {
            var ignoredPatterns = new[] { ".git", "node_modules", ".gradle", ".m2", "__pycache__", ".venv", "vendor" };
            return ignoredPatterns.Any(pattern => path.Contains(Path.DirectorySeparatorChar + pattern + Path.DirectorySeparatorChar) ||
                                                  path.Contains(Path.DirectorySeparatorChar + pattern));
        }

        private bool IsTextFile(string filePath)
        {
            var textExtensions = new[] { ".txt", ".yml", ".yaml", ".json", ".xml", ".config", ".env", ".sh", ".py", ".java", ".go", ".js", ".ts", ".cs" };
            var extension = Path.GetExtension(filePath).ToLower();
            return textExtensions.Contains(extension);
        }

        private string DetermineSeverity(string secretValue)
        {
            if (secretValue.Contains("password") || secretValue.Contains("secret")) return "CRITICAL";
            if (secretValue.Contains("token") || secretValue.Contains("key")) return "HIGH";
            return "MEDIUM";
        }

        private bool CheckSecretInConfig(string secret)
        {
            var configPaths = new[] { ".env", ".env.local", ".env.production", "config.yml" };
            return configPaths.Any(config => 
                File.Exists(config) && File.ReadAllText(config).Contains(secret));
        }

        private List<string> GenerateVaultIntegration()
        {
            return new List<string>
            {
                "1. Install HashiCorp Vault client library",
                "2. Configure Vault address: VAULT_ADDR=https://vault.example.com:8200",
                "3. Authenticate with Vault (token, AppRole, JWT)",
                "4. Mount secret engine: vault secrets enable -version=2 kv",
                "5. Store secrets: vault kv put secret/cicd/my-project NPM_TOKEN=xxx",
                "6. Grant CI/CD service appropriate policies",
                "7. Use Vault agent for automatic secret injection"
            };
        }

        private List<string> GenerateAwsSecretsManagerIntegration()
        {
            return new List<string>
            {
                "1. Create AWS Secrets Manager secret: aws secretsmanager create-secret",
                "2. Install boto3: pip install boto3",
                "3. Configure AWS credentials (IAM role, access keys)",
                "4. Grant CI/CD role permissions: secretsmanager:GetSecretValue",
                "5. Use in code: client = boto3.client('secretsmanager')",
                "6. Retrieve secrets: response = client.get_secret_value(SecretId='my-secret')",
                "7. Enable automatic secret rotation in AWS"
            };
        }

        private List<string> GenerateGitLabSecretsIntegration()
        {
            return new List<string>
            {
                "1. Add secrets in GitLab Project Settings > CI/CD > Variables",
                "2. Mark as 'Protected' and 'Masked' for sensitive data",
                "3. Reference in .gitlab-ci.yml: $MY_SECRET_VAR",
                "4. Use on specific branches/environments",
                "5. Limit access to specific runners",
                "6. Audit secret access in pipeline logs (use masking)",
                "7. Rotate secrets regularly through GitLab UI"
            };
        }

        private List<string> GenerateK8sSecretsIntegration()
        {
            return new List<string>
            {
                "1. Create Kubernetes secret: kubectl create secret generic my-secret",
                "2. Use sealed-secrets for GitOps: kubectl apply -f sealed-secret.yaml",
                "3. Or use external-secrets-operator for dynamic sync",
                "4. Mount secrets in pod: volumeMounts with secretKeyRef",
                "5. Reference in deployment: valueFrom.secretKeyRef",
                "6. Enable RBAC for secret access",
                "7. Use tools like Sealed Secrets or External Secrets Operator"
            };
        }
    }

    public class SecretAuditResult
    {
        public string ProjectPath { get; set; } = "";
        public DateTime AuditedAt { get; set; }
        public List<FoundSecret> SecretsFound { get; } = new();
        public List<string> Errors { get; } = new();
        public bool IsSafe { get; set; } = true;
    }

    public class FoundSecret
    {
        public string FilePath { get; set; } = "";
        public int LineNumber { get; set; }
        public string Pattern { get; set; } = "";
        public string Severity { get; set; } = "";
    }

    public class SecretValidationResult
    {
        public List<SecretValidation> SecretValidations { get; } = new();
        public bool AllValid { get; set; }
    }

    public class SecretValidation
    {
        public string SecretName { get; set; } = "";
        public string Status { get; set; } = "";
        public string Source { get; set; } = "";
        public string Fallback { get; set; } = "";
    }

    public class SecretIntegrationGuide
    {
        public string SecretManager { get; set; } = "";
        public DateTime GeneratedAt { get; set; }
        public List<string> ConfigurationSteps { get; set; } = new();
    }

    public static class SecretManagerDefaults
    {
        public static string GetFallbackForSecret(string secretName)
        {
            return secretName switch
            {
                "NEXUS_USER" => "admin",
                "NEXUS_PASSWORD" => "admin123",
                "NPM_TOKEN" => "npm-token-placeholder",
                "DOCKER_TOKEN" => "docker-token-placeholder",
                "SONAR_TOKEN" => "sonar-token-placeholder",
                "GITHUB_TOKEN" => "github-token-placeholder",
                _ => "configure-this-secret"
            };
        }
    }
}

