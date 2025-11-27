using System.Text;
using Ci_Cd.Models;

namespace Ci_Cd.Services
{
    public interface ILanguageSupportService
    {
        LanguageSupport GetLanguageSupport(string language);
        List<LanguageSupport> GetAllSupportedLanguages();
        LanguageJustification GenerateLanguageJustification(string language);
    }

    public class LanguageSupportService : ILanguageSupportService
    {
        public LanguageSupport GetLanguageSupport(string language)
        {
            return GetAllSupportedLanguages().FirstOrDefault(l => l.Language.Equals(language, StringComparison.OrdinalIgnoreCase))
                ?? new LanguageSupport { Language = language, Status = "UNSUPPORTED" };
        }

        public List<LanguageSupport> GetAllSupportedLanguages()
        {
            return new List<LanguageSupport>
            {
                // Core languages (fully supported)
                new()
                {
                    Language = "Java",
                    Status = "FULLY_SUPPORTED",
                    BuildTools = new[] { "Maven", "Gradle" },
                    TestFrameworks = new[] { "JUnit", "TestNG", "Spock" },
                    CoverageTools = new[] { "JaCoCo", "Cobertura" },
                    Frameworks = new[] { "Spring Boot", "Quarkus", "Micronaut", "Play Framework" },
                    RecommendedDependencyManager = "Maven/Gradle"
                },
                new()
                {
                    Language = "Go",
                    Status = "FULLY_SUPPORTED",
                    BuildTools = new[] { "go build", "go mod" },
                    TestFrameworks = new[] { "testing", "testify", "gotest" },
                    CoverageTools = new[] { "go cover", "codecov" },
                    Frameworks = new[] { "Gin", "Echo", "Fiber", "net/http" },
                    RecommendedDependencyManager = "Go Modules"
                },
                new()
                {
                    Language = "Node.js",
                    Status = "FULLY_SUPPORTED",
                    BuildTools = new[] { "npm", "yarn", "pnpm" },
                    TestFrameworks = new[] { "Jest", "Mocha", "Jasmine", "Vitest" },
                    CoverageTools = new[] { "Istanbul", "nyc", "Jest Coverage" },
                    Frameworks = new[] { "Express", "NestJS", "Next.js", "React" },
                    RecommendedDependencyManager = "npm/yarn/pnpm"
                },
                new()
                {
                    Language = "Python",
                    Status = "FULLY_SUPPORTED",
                    BuildTools = new[] { "pip", "Poetry", "Setuptools", "Flit" },
                    TestFrameworks = new[] { "pytest", "unittest", "nose", "hypothesis" },
                    CoverageTools = new[] { "coverage.py", "pytest-cov", "Codecov" },
                    Frameworks = new[] { "Django", "FastAPI", "Flask", "Pyramid" },
                    RecommendedDependencyManager = "pip/Poetry"
                },
                new()
                {
                    Language = "Rust",
                    Status = "FULLY_SUPPORTED",
                    BuildTools = new[] { "Cargo" },
                    TestFrameworks = new[] { "cargo test", "criterion", "proptest" },
                    CoverageTools = new[] { "tarpaulin", "kcov" },
                    Frameworks = new[] { "Actix-web", "Tokio", "Warp", "Rocket" },
                    RecommendedDependencyManager = "Cargo"
                },

                // Additional languages (with justification)
                new()
                {
                    Language = "Kotlin",
                    Status = "SUPPORTED",
                    Justification = new LanguageJustification
                    {
                        Reason = "JVM language with first-class Spring Boot support",
                        UseCases = new[] { "Backend services", "Android apps", "Cross-platform" },
                        Recommendation = "Use Maven/Gradle (same as Java), JUnit/Spock tests, KotlinTest framework",
                        EcosystemComponents = new[] { "Gradle (primary)", "Maven (secondary)", "JUnit 5", "Kotest", "kotlinx-coroutines" }
                    },
                    BuildTools = new[] { "Gradle", "Maven" },
                    TestFrameworks = new[] { "JUnit", "Kotest", "Spock" },
                    CoverageTools = new[] { "JaCoCo", "Kover" },
                    Frameworks = new[] { "Spring Boot", "Ktor", "Quarkus" }
                },
                new()
                {
                    Language = "C#",
                    Status = "SUPPORTED",
                    Justification = new LanguageJustification
                    {
                        Reason = ".NET ecosystem for backend/desktop applications",
                        UseCases = new[] { "Enterprise applications", "Cloud services", "Desktop apps" },
                        Recommendation = "Use .NET CLI, MSTest/xUnit/NUnit, SonarAnalyzer",
                        EcosystemComponents = new[] { ".NET CLI", "NuGet", "xUnit", "Moq", "Entity Framework" }
                    },
                    BuildTools = new[] { "dotnet cli", "MSBuild" },
                    TestFrameworks = new[] { "xUnit", "NUnit", "MSTest" },
                    CoverageTools = new[] { "OpenCover", "Coverlet" },
                    Frameworks = new[] { "ASP.NET Core", "Entity Framework", "MAUI" }
                },
                new()
                {
                    Language = "TypeScript",
                    Status = "SUPPORTED",
                    Justification = new LanguageJustification
                    {
                        Reason = "Superset of JavaScript with type safety",
                        UseCases = new[] { "Full-stack applications", "Type-safe APIs", "Large projects" },
                        Recommendation = "Use npm/yarn/pnpm, tsc compiler, Jest/Mocha tests",
                        EcosystemComponents = new[] { "TypeScript compiler", "npm", "Jest", "Prettier", "ESLint" }
                    },
                    BuildTools = new[] { "tsc", "webpack", "vite" },
                    TestFrameworks = new[] { "Jest", "Mocha", "Vitest" },
                    CoverageTools = new[] { "Istanbul", "Jest Coverage" },
                    Frameworks = new[] { "NestJS", "Next.js", "React" }
                },
                new()
                {
                    Language = "Ruby",
                    Status = "SUPPORTED",
                    Justification = new LanguageJustification
                    {
                        Reason = "Rails framework for rapid web development",
                        UseCases = new[] { "Web applications", "MVCs", "Prototyping" },
                        Recommendation = "Use Bundler, RSpec/Minitest, SimpleCov",
                        EcosystemComponents = new[] { "Bundler", "Rails", "RSpec", "Capybara", "Sidekiq" }
                    },
                    BuildTools = new[] { "Bundler", "Rake" },
                    TestFrameworks = new[] { "RSpec", "Minitest", "Cucumber" },
                    CoverageTools = new[] { "SimpleCov", "CodeClimate" },
                    Frameworks = new[] { "Rails", "Sinatra", "Hanami" }
                },
                new()
                {
                    Language = "PHP",
                    Status = "SUPPORTED",
                    Justification = new LanguageJustification
                    {
                        Reason = "Server-side web development with Laravel/Symfony",
                        UseCases = new[] { "Web applications", "CMS", "REST APIs" },
                        Recommendation = "Use Composer, PHPUnit, Xdebug for coverage",
                        EcosystemComponents = new[] { "Composer", "Laravel", "Symfony", "PHPUnit", "PHP-CS-Fixer" }
                    },
                    BuildTools = new[] { "Composer" },
                    TestFrameworks = new[] { "PHPUnit", "PHPSpec", "Behat" },
                    CoverageTools = new[] { "Xdebug", "PCOV" },
                    Frameworks = new[] { "Laravel", "Symfony", "Drupal" }
                },
                new()
                {
                    Language = "Scala",
                    Status = "SUPPORTED",
                    Justification = new LanguageJustification
                    {
                        Reason = "JVM language combining functional and OOP paradigms",
                        UseCases = new[] { "Big data processing", "Stream processing", "Distributed systems" },
                        Recommendation = "Use SBT or Maven, Scalatest, JaCoCo",
                        EcosystemComponents = new[] { "SBT", "Maven", "ScalaTest", "Spark", "Akka" }
                    },
                    BuildTools = new[] { "SBT", "Maven" },
                    TestFrameworks = new[] { "ScalaTest", "Specs2", "JUnit" },
                    CoverageTools = new[] { "JaCoCo", "Scoverage" },
                    Frameworks = new[] { "Play Framework", "Akka", "Spark" }
                },
                new()
                {
                    Language = "Elixir",
                    Status = "SUPPORTED",
                    Justification = new LanguageJustification
                    {
                        Reason = "Functional language for distributed/concurrent systems",
                        UseCases = new[] { "Real-time applications", "Chat apps", "IoT" },
                        Recommendation = "Use Mix, ExUnit, ExCoveralls",
                        EcosystemComponents = new[] { "Mix", "Phoenix", "ExUnit", "Ecto", "Supervision trees" }
                    },
                    BuildTools = new[] { "Mix" },
                    TestFrameworks = new[] { "ExUnit", "Hound" },
                    CoverageTools = new[] { "ExCoveralls" },
                    Frameworks = new[] { "Phoenix", "Ecto" }
                },
                new()
                {
                    Language = "Swift",
                    Status = "SUPPORTED",
                    Justification = new LanguageJustification
                    {
                        Reason = "Native iOS/macOS development with modern language",
                        UseCases = new[] { "iOS apps", "macOS apps", "Server-side (Linux)" },
                        Recommendation = "Use SPM (Swift Package Manager), XCTest, CodeCov",
                        EcosystemComponents = new[] { "SPM", "Xcode", "XCTest", "SwiftUI", "Combine" }
                    },
                    BuildTools = new[] { "SPM", "Xcode" },
                    TestFrameworks = new[] { "XCTest", "Quick" },
                    CoverageTools = new[] { "xcov", "Codecov" },
                    Frameworks = new[] { "SwiftUI", "Vapor" }
                }
            };
        }

        public LanguageJustification GenerateLanguageJustification(string language)
        {
            var support = GetLanguageSupport(language);
            
            if (support.Justification != null)
                return support.Justification;

            // Generate default justification for unsupported language
            return new LanguageJustification
            {
                Reason = $"Language {language} is not officially supported",
                UseCases = new[] { "Custom implementation" },
                Recommendation = "Analyze project structure and configure manually",
                EcosystemComponents = new[] { "Not determined" }
            };
        }
    }

    public class LanguageSupport
    {
        public string Language { get; set; } = "";
        public string Status { get; set; } = "";
        public string[] BuildTools { get; set; } = Array.Empty<string>();
        public string[] TestFrameworks { get; set; } = Array.Empty<string>();
        public string[] CoverageTools { get; set; } = Array.Empty<string>();
        public string[] Frameworks { get; set; } = Array.Empty<string>();
        public string RecommendedDependencyManager { get; set; } = "";
        public LanguageJustification? Justification { get; set; }
    }

    public class LanguageJustification
    {
        public string Reason { get; set; } = "";
        public string[] UseCases { get; set; } = Array.Empty<string>();
        public string Recommendation { get; set; } = "";
        public string[] EcosystemComponents { get; set; } = Array.Empty<string>();

        public string GenerateReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Language Justification Report");
            sb.AppendLine();
            sb.AppendLine($"## Reason");
            sb.AppendLine(Reason);
            sb.AppendLine();
            sb.AppendLine($"## Use Cases");
            foreach (var useCase in UseCases)
                sb.AppendLine($"- {useCase}");
            sb.AppendLine();
            sb.AppendLine($"## Recommendation");
            sb.AppendLine(Recommendation);
            sb.AppendLine();
            sb.AppendLine($"## Ecosystem Components");
            foreach (var component in EcosystemComponents)
                sb.AppendLine($"- {component}");

            return sb.ToString();
        }
    }
}

