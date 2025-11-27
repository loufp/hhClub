using System.Text;
using System.Text.RegularExpressions;
using Ci_Cd.Models;

namespace Ci_Cd.Services
{
    public interface IArtifactManager
    {
        ArtifactInfo DetectArtifactType(RepoAnalysisResult analysis);
        string DetermineVersion(string repoPath);
        string GenerateVersionStrategy(RepoAnalysisResult analysis);
    }

    public class ArtifactManager : IArtifactManager
    {
        public ArtifactInfo DetectArtifactType(RepoAnalysisResult analysis)
        {
            var artifactInfo = new ArtifactInfo();

            switch (analysis.Language)
            {
                case RepoAnalysisResult.ProjectLanguage.Java:
                    artifactInfo.ArtifactType = "jar";
                    artifactInfo.ArtifactPath = analysis.BuildTools.Contains("Maven")
                        ? "target/*.jar"
                        : "build/libs/*.jar";
                    artifactInfo.RepositoryType = "nexus-maven";
                    break;

                case RepoAnalysisResult.ProjectLanguage.Kotlin:
                    artifactInfo.ArtifactType = "jar";
                    artifactInfo.ArtifactPath = "build/libs/*.jar"; // Kotlin всегда использует Gradle
                    artifactInfo.RepositoryType = "nexus-maven"; // JVM артефакты идут в Maven репозиторий
                    break;

                case RepoAnalysisResult.ProjectLanguage.NodeJs:
                    artifactInfo.ArtifactType = "npm-package";
                    artifactInfo.ArtifactPath = ".";
                    artifactInfo.RepositoryType = "npm-registry";
                    break;

                case RepoAnalysisResult.ProjectLanguage.Python:
                    artifactInfo.ArtifactType = analysis.BuildTools.Contains("Poetry")
                        ? "wheel-poetry" : "wheel";
                    artifactInfo.ArtifactPath = "dist/*.whl";
                    artifactInfo.RepositoryType = "nexus-pypi";
                    break;

                case RepoAnalysisResult.ProjectLanguage.Go:
                    artifactInfo.ArtifactType = "binary";
                    artifactInfo.ArtifactPath = "bin/*";
                    artifactInfo.RepositoryType = "github-releases";
                    break;

                default:
                    artifactInfo.ArtifactType = "generic";
                    artifactInfo.RepositoryType = "unknown";
                    break;
            }

            // Docker image всегда как дополнительный артефакт
            artifactInfo.DockerImage = $"$CI_REGISTRY_IMAGE:$CI_COMMIT_SHORT_SHA";
            artifactInfo.DockerImageLatest = $"$CI_REGISTRY_IMAGE:latest";

            return artifactInfo;
        }

        public string DetermineVersion(string repoPath)
        {
            // Попытка получить версию из git tags
            if (Directory.Exists(Path.Combine(repoPath, ".git")))
            {
                var versionFile = new[] { "version.txt", "VERSION", ".version" }
                    .FirstOrDefault(f => File.Exists(Path.Combine(repoPath, f)));

                if (!string.IsNullOrEmpty(versionFile))
                {
                    var version = File.ReadAllText(Path.Combine(repoPath, versionFile)).Trim();
                    if (IsValidSemanticVersion(version))
                        return version;
                }
            }

            // Default версия
            return "0.1.0";
        }

        public string GenerateVersionStrategy(RepoAnalysisResult analysis)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Version strategy based on git tags and semantic versioning");
            sb.AppendLine();
            
            sb.AppendLine("RELEASE_VERSION:");
            sb.AppendLine("  - Main branch: release version (v1.2.3)");
            sb.AppendLine("  - Tag: bumps patch version");
            sb.AppendLine();
            
            sb.AppendLine("SNAPSHOT_VERSION:");
            sb.AppendLine("  - Develop branch: snapshot (1.2.3-SNAPSHOT)");
            sb.AppendLine("  - Feature branch: dev version (1.2.3-dev.{build})");
            sb.AppendLine();
            
            sb.AppendLine("GIT_TAGS:");
            sb.AppendLine("  - v{major}.{minor}.{patch} → release");
            sb.AppendLine("  - v{major}.{minor}.{patch}-rc.{n} → release candidate");
            sb.AppendLine("  - v{major}.{minor}.{patch}-beta.{n} → beta release");

            return sb.ToString();
        }

        private string GenerateJavaUploadCommand(RepoAnalysisResult analysis)
        {
            if (analysis.BuildTools.Contains("Maven"))
            {
                return @"mvn deploy:deploy-file \
  -DgroupId=${GROUP_ID} \
  -DartifactId=${ARTIFACT_ID} \
  -Dversion=${VERSION} \
  -Dpackaging=jar \
  -Dfile=target/*.jar \
  -DrepositoryId=nexus \
  -Durl=${NEXUS_URL}/repository/maven-releases/";
            }
            else
            {
                return @"./gradlew publish \
  -PnexusUrl=${NEXUS_URL} \
  -PnexusUser=${NEXUS_USER} \
  -PnexusPassword=${NEXUS_PASSWORD}";
            }
        }

        private string GenerateNodeUploadCommand(RepoAnalysisResult analysis)
        {
            return @"echo ""//registry.npmjs.org/:_authToken=$NPM_TOKEN"" > ~/.npmrc && \
npm publish --access public";
        }

        private string GeneratePythonUploadCommand(RepoAnalysisResult analysis)
        {
            if (analysis.BuildTools.Contains("Poetry"))
            {
                return @"poetry config repositories.nexus ${NEXUS_URL}/repository/pypi-hosted/ && \
poetry publish --repository nexus \
  --username ${NEXUS_USER} \
  --password ${NEXUS_PASSWORD}";
            }
            else
            {
                return @"python -m twine upload \
  --repository-url ${NEXUS_URL}/repository/pypi-hosted/ \
  --username ${NEXUS_USER} \
  --password ${NEXUS_PASSWORD} \
  dist/*";
            }
        }

        private string GenerateGoUploadCommand(RepoAnalysisResult analysis)
        {
            return @"gh release create ${CI_COMMIT_TAG} \
  --title ""Release ${CI_COMMIT_TAG}"" \
  --notes ""$(cat CHANGELOG.md | head -20)"" \
  bin/*";
        }

        private bool IsValidSemanticVersion(string version)
        {
            var semverPattern = @"^v?\d+\.\d+\.\d+(-[a-zA-Z0-9]+(\.[a-zA-Z0-9]+)*)?(\+[a-zA-Z0-9]+)?$";
            return Regex.IsMatch(version, semverPattern);
        }
    }

    public class ArtifactInfo
    {
        public string ArtifactType { get; set; } = string.Empty;
        public string ArtifactPath { get; set; } = string.Empty;
        public string RepositoryType { get; set; } = string.Empty;
        public string DockerImage { get; set; } = string.Empty;
        public string DockerImageLatest { get; set; } = string.Empty;
    }
}

