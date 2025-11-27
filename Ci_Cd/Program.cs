using System.CommandLine;
using System.Text;
using Ci_Cd.Models;
using Ci_Cd.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.IO.Compression;
using System.IO.Compression;

var serviceProvider = new ServiceCollection()
    .AddLogging(configure => configure.AddConsole().SetMinimumLevel(LogLevel.Information))
    .AddSingleton<IAnalyzerService, AnalyzerService>()
    .AddSingleton<IGitServices, GitServices>()
    .AddSingleton<IVariableMapper, VariableMapper>()
    .AddSingleton<ITemplateEngine, TemplateEngine>()
    .AddSingleton<ITemplateService, TemplateService>()
    .AddSingleton<ISonarQubeService, SonarQubeService>()
    .AddSingleton<IExecutionService, ExecutionService>()
    .AddSingleton<ISandboxService, SandboxService>()
    .AddSingleton<IDockerfileGenerator, DockerfileGenerator>()
    .AddSingleton<IArtifactManager, ArtifactManager>()
    .AddSingleton<IPipelineValidator, PipelineValidator>()
    .AddSingleton<IReportGenerator, ReportGenerator>()
    .AddSingleton<IInteractiveCli, InteractiveCli>()
    .AddSingleton<IConfigurationService, ConfigurationService>()
    .AddSingleton<ISecurityService, SecurityService>()
    .AddSingleton<IE2ETestService, E2ETestService>()
    .AddSingleton<ILanguageSupportService, LanguageSupportService>()
    .BuildServiceProvider();

// Если нет аргументов - запускаем интерактивный режим
if (args.Length == 0)
{
    await RunInteractiveMode(serviceProvider);
    return 0;
}

var rootCommand = new RootCommand("CI/CD Pipeline Generator");

var repoOption = new Option<string>(
    name: "--repo",
    description: "URL of the Git repository to analyze.")
{
    IsRequired = true
};
repoOption.AddAlias("-r");

var outputOption = new Option<string>(
    name: "--output",
    description: "Directory to save the generated pipeline files.",
    getDefaultValue: () => Path.Combine(Directory.GetCurrentDirectory(), "output"));
outputOption.AddAlias("-o");

var executeOption = new Option<bool>(
    name: "--execute",
    description: "Execute the build and test commands after analysis.",
    getDefaultValue: () => false);
executeOption.AddAlias("-e");

var formatOption = new Option<string>(
    name: "--format",
    description: "Output format: dir (directory) or zip (single zip file)",
    getDefaultValue: () => "dir");
formatOption.AddAlias("-f");

rootCommand.AddOption(repoOption);
rootCommand.AddOption(outputOption);
rootCommand.AddOption(executeOption);
rootCommand.AddOption(formatOption);

rootCommand.SetHandler(async (repoUrl, outputDir, shouldExecute, format) =>
    {
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        var gitService = serviceProvider.GetRequiredService<IGitServices>();
        var analyzer = serviceProvider.GetRequiredService<IAnalyzerService>();
        var templateService = serviceProvider.GetRequiredService<ITemplateService>();

        string? repoPath = null;
        try
        {
            repoPath = gitService.CloneRepository(repoUrl);
            var analysis = analyzer.Analyze(repoPath);

            if (analysis.Language == RepoAnalysisResult.ProjectLanguage.Unknown)
            {
                logger.LogWarning("Analysis failed: Could not determine project language.");
                return;
            }

            var gitlabCi = templateService.GenerateGitLabCi(analysis);
            var jenkinsfile = templateService.GenerateJenkinsfile(analysis);

            // Генерация Docker файлов
            var dockerGenerator = serviceProvider.GetRequiredService<IDockerfileGenerator>();
            var dockerfile = dockerGenerator.GenerateDockerfile(analysis);
            var dockerignore = dockerGenerator.GenerateDockerignore(analysis);

            Directory.CreateDirectory(outputDir);
            
            // Основные CI/CD файлы
            await File.WriteAllTextAsync(Path.Combine(outputDir, ".gitlab-ci.yml"), gitlabCi);
            await File.WriteAllTextAsync(Path.Combine(outputDir, "Jenkinsfile"), jenkinsfile);
            await File.WriteAllTextAsync(Path.Combine(outputDir, "Dockerfile"), dockerfile);
            await File.WriteAllTextAsync(Path.Combine(outputDir, ".dockerignore"), dockerignore);
            
            // Генерация отчётов
            var artifactManager = serviceProvider.GetRequiredService<IArtifactManager>();
            var reportGenerator = serviceProvider.GetRequiredService<IReportGenerator>();
            var validator = serviceProvider.GetRequiredService<IPipelineValidator>();
            
            var artifactInfo = artifactManager.DetectArtifactType(analysis);
            var jsonReport = reportGenerator.GenerateJsonReport(analysis, artifactInfo);
            var yamlReport = reportGenerator.GenerateYamlReport(analysis, artifactInfo);
            var recommendations = reportGenerator.GenerateRecommendations(analysis);
            var depTree = reportGenerator.GenerateDependencyTree(analysis);
            
            await File.WriteAllTextAsync(Path.Combine(outputDir, "analysis-report.json"), jsonReport);
            await File.WriteAllTextAsync(Path.Combine(outputDir, "analysis-report.yaml"), yamlReport);
            
            // Валидация генерируемых конфигов
            var gitlabValidation = validator.ValidateGitLabCi(gitlabCi);
            var jenkinsValidation = validator.ValidateJenkinsfile(jenkinsfile);
            
            var validationReport = new StringBuilder();
            validationReport.AppendLine("═══════════════════════════════════════════════════");
            validationReport.AppendLine("VALIDATION REPORT");
            validationReport.AppendLine("═══════════════════════════════════════════════════");
            validationReport.AppendLine();
            validationReport.AppendLine("GitLab CI Validation:");
            validationReport.AppendLine($"  Valid: {gitlabValidation.IsValid}");
            if (gitlabValidation.Errors.Any())
            {
                validationReport.AppendLine("  Errors:");
                foreach (var err in gitlabValidation.Errors)
                    validationReport.AppendLine($"    - {err}");
            }
            if (gitlabValidation.Warnings.Any())
            {
                validationReport.AppendLine("  Warnings:");
                foreach (var warn in gitlabValidation.Warnings)
                    validationReport.AppendLine($"    - {warn}");
            }
            validationReport.AppendLine();
            
            validationReport.AppendLine("Jenkinsfile Validation:");
            validationReport.AppendLine($"  Valid: {jenkinsValidation.IsValid}");
            if (jenkinsValidation.Errors.Any())
            {
                validationReport.AppendLine("  Errors:");
                foreach (var err in jenkinsValidation.Errors)
                    validationReport.AppendLine($"    - {err}");
            }
            if (jenkinsValidation.Warnings.Any())
            {
                validationReport.AppendLine("  Warnings:");
                foreach (var warn in jenkinsValidation.Warnings)
                    validationReport.AppendLine($"    - {warn}");
            }
            validationReport.AppendLine();
            
            await File.WriteAllTextAsync(Path.Combine(outputDir, "validation-report.txt"), validationReport.ToString());
            
            // Dry-run preview
            var dryRunPath = Path.Combine(outputDir, "DRY_RUN_PREVIEW.md");
            var dryRunPreview = new StringBuilder();
            dryRunPreview.AppendLine("# CI/CD Configuration Preview (Dry-Run)");
            dryRunPreview.AppendLine();
            dryRunPreview.AppendLine("## Generated Files");
            dryRunPreview.AppendLine("- ✅ .gitlab-ci.yml");
            dryRunPreview.AppendLine("- ✅ Jenkinsfile");
            dryRunPreview.AppendLine("- ✅ Dockerfile");
            dryRunPreview.AppendLine("- ✅ .dockerignore");
            dryRunPreview.AppendLine();
            dryRunPreview.AppendLine("## Artifact Configuration");
            dryRunPreview.AppendLine($"- Type: {artifactInfo.ArtifactType}");
            dryRunPreview.AppendLine($"- Path: {artifactInfo.ArtifactPath}");
            dryRunPreview.AppendLine($"- Repository: {artifactInfo.RepositoryType}");
            dryRunPreview.AppendLine();
            dryRunPreview.AppendLine("## Recommendations");
            dryRunPreview.Append(recommendations);
            dryRunPreview.AppendLine();
            dryRunPreview.AppendLine();
            dryRunPreview.AppendLine("## Dependency Tree");
            dryRunPreview.Append(depTree);
            
            await File.WriteAllTextAsync(dryRunPath, dryRunPreview.ToString());
            
            // If format == zip, create zip archive next to outputDir
            if (!string.IsNullOrEmpty(format) && format.Equals("zip", StringComparison.OrdinalIgnoreCase))
            {
                var parent = Path.GetDirectoryName(outputDir) ?? Directory.GetCurrentDirectory();
                var baseName = Path.GetFileName(outputDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var zipPath = Path.Combine(parent, baseName + ".zip");
                if (File.Exists(zipPath)) File.Delete(zipPath);
                ZipFile.CreateFromDirectory(outputDir, zipPath, CompressionLevel.Optimal, false);
                logger.LogInformation($"Created archive: {zipPath} ({new FileInfo(zipPath).Length} bytes)");
            }

            if (shouldExecute)
            {
                logger.LogInformation("\n--- Starting Execution ---");
                var sandbox = serviceProvider.GetRequiredService<ISandboxService>();
                var exec = serviceProvider.GetRequiredService<IExecutionService>();

                // prefer sandbox if docker available and URL validated
                if (sandbox.ValidateRepositoryUrl(repoUrl, out var reason))
                {
                    logger.LogInformation("Running build+tests in sandbox container");
                    var image = analysis.GetBestImage();
                    var sbOptions = new SandboxOptions { Cpus = 0.5, Memory = "512m", PidsLimit = 64, NetworkNone = true };
                    var res = await sandbox.RunInSandbox(repoPath, analysis.BuildCommands.Concat(analysis.TestCommands), image, TimeSpan.FromMinutes(15), sbOptions);
                    var logPath = Path.Combine(outputDir, "execution.log");
                    await File.WriteAllTextAsync(logPath, $"Exit Code: {res.ExitCode}\n\nSTDOUT:\n{res.StdOut}\n\nSTDERR:\n{res.StdErr}");
                    if (res.ExitCode == 0) logger.LogInformation("Sandbox execution succeeded"); else logger.LogError("Sandbox execution failed. See execution.log");
                }
                else
                {
                    logger.LogWarning("Sandbox rejected repo URL: {reason}. Falling back to local execution", reason);
                    var result = await exec.Run(repoPath, analysis.BuildCommands.Concat(analysis.TestCommands), TimeSpan.FromMinutes(10));
                    var logPath = Path.Combine(outputDir, "execution.log");
                    await File.WriteAllTextAsync(logPath, $"Exit Code: {result.ExitCode}\n\nSTDOUT:\n{result.StdOut}\n\nSTDERR:\n{result.StdErr}");
                    if (result.ExitCode == 0) logger.LogInformation("Execution completed successfully. See 'execution.log' for details."); else logger.LogError("Execution failed. See 'execution.log' for details.");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred.");
        }
        finally
        {
            if (!string.IsNullOrEmpty(repoPath))
            {
                gitService.DeleteRepository(repoPath);
            }
        }
    },
    repoOption, outputOption, executeOption, formatOption);

return await rootCommand.InvokeAsync(args);

void PrintReport(RepoAnalysisResult analysis, string outputDir, ILogger logger)
{
    var report = BuildReportString(analysis, outputDir);
    logger.LogInformation(report);
}

string BuildReportString(RepoAnalysisResult analysis, string outputDir)
{
    var report = new StringBuilder();
    report.AppendLine("--- Repository Analysis Report ---");
    report.AppendLine($"Project Name: {analysis.ProjectName}");
    report.AppendLine($"Version: {analysis.ProjectVersion}");
    report.AppendLine($"Language: {analysis.Language}");
    report.AppendLine($"Framework: {analysis.Framework}");
    report.AppendLine($"Dockerfile Found: {analysis.HasDockerfile} {(analysis.DockerfileGenerated ? "(Generated)" : "")}");
    report.AppendLine();
    if (analysis.Dependencies.Any())
    {
        report.AppendLine("Dependencies:");
        foreach (var d in analysis.Dependencies)
            report.AppendLine($"  - {d.Name} ({d.Version})");
    }
    report.AppendLine();
    report.AppendLine("Generated Files:");
    report.AppendLine("  - .gitlab-ci.yml");
    report.AppendLine("  - Jenkinsfile");
    report.AppendLine("  - Dockerfile");
    report.AppendLine();
    report.AppendLine($"All files saved to: {Path.GetFullPath(outputDir)}");
    report.AppendLine();
    report.AppendLine("Rationale:");
    report.AppendLine(analysis.Rationale);

    return report.ToString();
}

void CopyDirectory(string sourceDir, string targetDir)
{
    Directory.CreateDirectory(targetDir);
    foreach (var file in Directory.GetFiles(sourceDir))
    {
        var dest = Path.Combine(targetDir, Path.GetFileName(file));
        File.Copy(file, dest, true);
    }
    foreach (var dir in Directory.GetDirectories(sourceDir))
    {
        var dest = Path.Combine(targetDir, Path.GetFileName(dir));
        CopyDirectory(dir, dest);
    }
}

async Task<int> RunInteractiveMode(ServiceProvider serviceProvider)
{
    var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
    
    Console.Clear();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║                                                            ║");
    Console.WriteLine("║         🚀 CI/CD Pipeline Generator                       ║");
    Console.WriteLine("║         Автоматическое создание CI/CD конфигураций        ║");
    Console.WriteLine("║                                                            ║");
    Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
    Console.ResetColor();
    Console.WriteLine();
    
    // Запрос URL репозитория
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.Write("📁 Введите URL вашего GitHub репозитория: ");
    Console.ResetColor();
    var repoUrl = Console.ReadLine()?.Trim();
    
    if (string.IsNullOrWhiteSpace(repoUrl))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("❌ Ошибка: URL репозитория не может быть пустым!");
        Console.ResetColor();
        return 1;
    }
    
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("✅ Начинаю анализ...");
    Console.ResetColor();
    Console.WriteLine();
    
    var gitService = serviceProvider.GetRequiredService<IGitServices>();
    var analyzer = serviceProvider.GetRequiredService<IAnalyzerService>();
    var templateService = serviceProvider.GetRequiredService<ITemplateService>();
    
    string? repoPath = null;
    try
    {
        // Клонирование репозитория
        Console.WriteLine("📥 Клонирование репозитория...");
        repoPath = gitService.CloneRepository(repoUrl);
        
        // Анализ проекта
        Console.WriteLine("🔍 Анализ проекта...");
        var analysis = analyzer.Analyze(repoPath);
        
        if (analysis.Language == RepoAnalysisResult.ProjectLanguage.Unknown)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("❌ Не удалось определить язык проекта!");
            Console.ResetColor();
            return 1;
        }
        
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✅ Обнаружен проект: {analysis.Language}");
        if (!string.IsNullOrEmpty(analysis.Framework))
            Console.WriteLine($"   Framework: {analysis.Framework}");
        Console.ResetColor();
        Console.WriteLine();
        
        // Генерация конфигураций
        Console.WriteLine("⚙️  Генерация CI/CD конфигураций...");
        var gitlabCi = templateService.GenerateGitLabCi(analysis);
        var jenkinsfile = templateService.GenerateJenkinsfile(analysis);
        
        // Генерация Docker файлов
        Console.WriteLine("🐳 Генерация Docker файлов...");
        var dockerGenerator = serviceProvider.GetRequiredService<IDockerfileGenerator>();
        var dockerfile = dockerGenerator.GenerateDockerfile(analysis);
        var dockerignore = dockerGenerator.GenerateDockerignore(analysis);
        
        // Создание выходной директории
        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "output");
        Directory.CreateDirectory(outputDir);
        
        // Сохранение файлов
        Console.WriteLine("💾 Сохранение файлов...");
        await File.WriteAllTextAsync(Path.Combine(outputDir, ".gitlab-ci.yml"), gitlabCi);
        await File.WriteAllTextAsync(Path.Combine(outputDir, "Jenkinsfile"), jenkinsfile);
        await File.WriteAllTextAsync(Path.Combine(outputDir, "Dockerfile"), dockerfile);
        await File.WriteAllTextAsync(Path.Combine(outputDir, ".dockerignore"), dockerignore);
        
        // Генерация отчётов
        Console.WriteLine("📊 Генерация отчётов...");
        var artifactManager = serviceProvider.GetRequiredService<IArtifactManager>();
        var reportGenerator = serviceProvider.GetRequiredService<IReportGenerator>();
        var validator = serviceProvider.GetRequiredService<IPipelineValidator>();
        
        var artifactInfo = artifactManager.DetectArtifactType(analysis);
        var jsonReport = reportGenerator.GenerateJsonReport(analysis, artifactInfo);
        var yamlReport = reportGenerator.GenerateYamlReport(analysis, artifactInfo);
        
        await File.WriteAllTextAsync(Path.Combine(outputDir, "analysis-report.json"), jsonReport);
        await File.WriteAllTextAsync(Path.Combine(outputDir, "analysis-report.yaml"), yamlReport);
        
        // Создание ZIP-архива (автоматически)
        Console.WriteLine("📦 Создание ZIP-архива...");
        var zipPath = Path.Combine(Directory.GetCurrentDirectory(), "output.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);
        ZipFile.CreateFromDirectory(outputDir, zipPath, CompressionLevel.Optimal, false);
        
        var zipSize = new FileInfo(zipPath).Length;
        var zipSizeKb = zipSize / 1024.0;
        
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                     ✅ ГОТОВО!                            ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
        
        Console.WriteLine("📁 Созданные файлы:");
        Console.WriteLine($"   ✓ .gitlab-ci.yml");
        Console.WriteLine($"   ✓ Jenkinsfile");
        Console.WriteLine($"   ✓ Dockerfile");
        Console.WriteLine($"   ✓ .dockerignore");
        Console.WriteLine($"   ✓ analysis-report.json");
        Console.WriteLine($"   ✓ analysis-report.yaml");
        Console.WriteLine();
        
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"📦 ZIP-архив создан: output.zip ({zipSizeKb:F2} KB)");
        Console.WriteLine($"📂 Папка с файлами: {Path.GetFullPath(outputDir)}");
        Console.ResetColor();
        Console.WriteLine();
        
        Console.WriteLine("💡 Что делать дальше:");
        Console.WriteLine("   1. Распакуйте output.zip");
        Console.WriteLine("   2. Скопируйте файлы в ваш проект");
        Console.WriteLine("   3. Загрузите изменения в Git");
        Console.WriteLine();
        
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("🎉 Желаем успешных деплоев!");
        Console.ResetColor();
        
        return 0;
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"❌ Ошибка: {ex.Message}");
        Console.ResetColor();
        logger.LogError(ex, "Ошибка в интерактивном режиме");
        return 1;
    }
    finally
    {
        if (!string.IsNullOrEmpty(repoPath))
        {
            gitService.DeleteRepository(repoPath);
        }
    }
}
