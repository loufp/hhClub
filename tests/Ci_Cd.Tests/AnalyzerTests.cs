using System.IO;
using Ci_Cd.Services;
using Ci_Cd.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ci_Cd.Tests
{
    public class AnalyzerTests
    {
        [Fact]
        public void Detects_NodeJs_from_package_json()
        {
            var dir = Path.Combine(Path.GetTempPath(), "test_node_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "package.json"), "{ \"name\": \"myapp\", \"version\": \"1.0.0\", \"scripts\": { \"build\": \"tsc\", \"test\": \"jest\" } }");

            var analyzer = new AnalyzerService(new Microsoft.Extensions.Logging.Abstractions.NullLogger<AnalyzerService>());
            var res = analyzer.Analyze(dir);
            Assert.Equal(Ci_Cd.Models.RepoAnalysisResult.ProjectLanguage.NodeJs, res.Language);
            Assert.Contains("npm run build", res.BuildCommands[0] + res.BuildCommands[^1]);

            Directory.Delete(dir, true);
        }

        [Fact]
        public void Detects_Python_from_requirements()
        {
            var dir = Path.Combine(Path.GetTempPath(), "test_py_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "requirements.txt"), "django==4.2.0\n");

            var analyzer = new AnalyzerService(new Microsoft.Extensions.Logging.Abstractions.NullLogger<AnalyzerService>());
            var res = analyzer.Analyze(dir);
            Assert.Equal(Ci_Cd.Models.RepoAnalysisResult.ProjectLanguage.Python, res.Language);
            Assert.Contains("pip install", res.BuildCommands[0]);

            Directory.Delete(dir, true);
        }

        [Fact]
        public void Detects_Go_from_go_mod()
        {
            var dir = Path.Combine(Path.GetTempPath(), "test_go_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "go.mod"), "module github.com/example/myapp\n\nrequire ( github.com/stretchr/testify v1.8.0 )");

            var analyzer = new AnalyzerService(new Microsoft.Extensions.Logging.Abstractions.NullLogger<AnalyzerService>());
            var res = analyzer.Analyze(dir);
            Assert.Equal(Ci_Cd.Models.RepoAnalysisResult.ProjectLanguage.Go, res.Language);
            Assert.Contains("go build", res.BuildCommands[0]);

            Directory.Delete(dir, true);
        }

        [Fact]
        public void Detects_Java_from_pom()
        {
            var dir = Path.Combine(Path.GetTempPath(), "test_java_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "pom.xml"), "<project xmlns=\"http://maven.apache.org/POM/4.0.0\"> <artifactId>myapp</artifactId> <version>1.0.0</version> </project>");

            var analyzer = new AnalyzerService(new Microsoft.Extensions.Logging.Abstractions.NullLogger<AnalyzerService>());
            var res = analyzer.Analyze(dir);
            Assert.Equal(Ci_Cd.Models.RepoAnalysisResult.ProjectLanguage.Java, res.Language);
            Assert.Contains("mvn", res.BuildCommands[0]);

            Directory.Delete(dir, true);
        }

        [Fact]
        public void DetectsWorkspaces_ArrayPattern()
        {
            var root = Path.Combine(Path.GetTempPath(), "analyzer_test_ws_array_" + Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                // create package.json with workspaces array
                var pkg = Path.Combine(root, "package.json");
                File.WriteAllText(pkg, "{ \"workspaces\": [ \"packages/*\" ] }");
                // create subdirs matching pattern
                var p1 = Path.Combine(root, "packages", "a");
                var p2 = Path.Combine(root, "packages", "b");
                Directory.CreateDirectory(p1);
                Directory.CreateDirectory(p2);
                File.WriteAllText(Path.Combine(p1, "package.json"), "{}");
                File.WriteAllText(Path.Combine(p2, "package.json"), "{}");

                var analyzer = new AnalyzerService(NullLogger<AnalyzerService>.Instance);
                var res = analyzer.Analyze(root);

                Assert.NotNull(res);
                Assert.True(res.Subprojects.Count >= 2, "Expected at least two subprojects detected");
                Assert.Contains(res.Subprojects, s => s.EndsWith(Path.Combine("packages", "a")));
                Assert.Contains(res.Subprojects, s => s.EndsWith(Path.Combine("packages", "b")));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [Fact]
        public void DetectsPnpmWorkspace_WithGlob()
        {
            var root = Path.Combine(Path.GetTempPath(), "analyzer_test_pnpm_" + Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                // create package.json (empty)
                File.WriteAllText(Path.Combine(root, "package.json"), "{}");
                // create pnpm-workspace.yaml
                var pnpm = Path.Combine(root, "pnpm-workspace.yaml");
                File.WriteAllText(pnpm, "packages:\n  - 'modules/**'\n  - 'services/*'");

                // create matching dirs
                var m1 = Path.Combine(root, "modules", "core");
                var s1 = Path.Combine(root, "services", "svc1");
                Directory.CreateDirectory(m1);
                Directory.CreateDirectory(s1);
                File.WriteAllText(Path.Combine(m1, "package.json"), "{}");
                File.WriteAllText(Path.Combine(s1, "package.json"), "{}");

                var analyzer = new AnalyzerService(NullLogger<AnalyzerService>.Instance);
                var res = analyzer.Analyze(root);

                Assert.NotNull(res);
                Assert.True(res.Subprojects.Count >= 2, "Expected at least two subprojects detected from pnpm-workspace.yaml");
                Assert.Contains(res.Subprojects, s => s.EndsWith(Path.Combine("modules", "core")));
                Assert.Contains(res.Subprojects, s => s.EndsWith(Path.Combine("services", "svc1")));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }
    }
}
