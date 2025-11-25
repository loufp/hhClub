using System.Text.Json;
using Ci_Cd.Models;
using Ci_Cd.Services;
using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;

namespace Ci_Cd.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PipelineController : ControllerBase
    {
        private readonly IGitServices _gitService;
        private readonly IAnalyzerService _analyzer;
        private readonly ITemplateService _template;
        private readonly IExecutionService _execService;

        public PipelineController(
            IGitServices gitService,
            IAnalyzerService analyzer,
            ITemplateService template,
            IExecutionService execService)
        {
            _gitService = gitService;
            _analyzer = analyzer;
            _template = template;
            _execService = execService;
        }

        [HttpPost("generate")]
        public async Task<IActionResult> GeneratePipeline()
        {
            var q = Request.Query;
            var outFormat = q.TryGetValue("format", out var f) ? f.ToString() : "json";
            var execFromQuery = q.TryGetValue("execute", out var exq) && (exq.ToString() == "1" || exq.ToString().Equals("true", StringComparison.OrdinalIgnoreCase));
            var repoQuery = q.TryGetValue("repoUrl", out var rq) && !string.IsNullOrWhiteSpace(rq) ? rq.ToString() : (q.TryGetValue("RepoUrl", out var rq2) ? rq2.ToString() : string.Empty);

            string rawBody;
            using (var r = new StreamReader(Request.Body)) rawBody = await r.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(rawBody) && string.IsNullOrWhiteSpace(repoQuery)) return BadRequest("Provide repoUrl in query or JSON body");

            var (repo, shouldExec) = ParseBody(rawBody, repoQuery, execFromQuery);
            if (string.IsNullOrWhiteSpace(repo)) return BadRequest("repoUrl is required");

            string repoPath = string.Empty;
            try
            {
                repoPath = _gitService.CloneRepository(repo);
                var analysis = _analyzer.Analyze(repoPath);
                var gitlab = _template.GenerateGitLabCi(analysis);
                var jenkins = _template.GenerateJenkinsfile(analysis);

                ExecutionResult? execRes = null;
                if (shouldExec)
                {
                    var key = Environment.GetEnvironmentVariable("PIPELINE_API_KEY");
                    var provided = Request.Headers.ContainsKey("X-API-KEY") ? Request.Headers["X-API-KEY"].ToString() : string.Empty;
                    if (!string.IsNullOrEmpty(key) && provided != key) return Unauthorized("Invalid or missing API key for execute");

                    var commands = analysis.SuggestedBuildCommands.ToList();
                    if (analysis.HasDockerfile)
                    {
                        commands.Add("docker build -t myapp:latest .");
                        commands.Add("echo 'Skipping push in execute mode unless registry configured'");
                    }

                    string? dockerImage = analysis.Language switch
                    {
                        RepoAnalysisResult.ProjectLanguage.DotNet => "mcr.microsoft.com/dotnet/sdk:8.0",
                        RepoAnalysisResult.ProjectLanguage.NodeJs => "node:18-alpine",
                        RepoAnalysisResult.ProjectLanguage.Go => "golang:1.21",
                        RepoAnalysisResult.ProjectLanguage.Python => "python:3.10",
                        RepoAnalysisResult.ProjectLanguage.Java => "maven:3.8.6-jdk-17",
                        _ => null
                    };

                    if (!string.IsNullOrEmpty(dockerImage))
                    {
                        try
                        {
                            execRes = await _execService.RunInContainer(repoPath, commands, dockerImage, TimeSpan.FromMinutes(10));
                            if (execRes != null && execRes.ExitCode == -2) execRes = await _execService.Run(repoPath, commands, TimeSpan.FromMinutes(10));
                        }
                        catch
                        {
                            try
                            {
                                execRes = await _execService.Run(repoPath, commands, TimeSpan.FromMinutes(10));
                            }
                            catch (Exception e)
                            {
                                execRes = new ExecutionResult { ExitCode = -1, StdOut = string.Empty, StdErr = e.Message };
                            }
                        }
                    }
                    else
                    {
                        execRes = await _execService.Run(repoPath, commands, TimeSpan.FromMinutes(10));
                    }
                }

                if (outFormat == "zip")
                {
                    using var ms = new MemoryStream();
                    using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
                    {
                        var e1 = archive.CreateEntry(".gitlab-ci.yml");
                        using (var s = e1.Open()) using (var sw = new StreamWriter(s)) sw.Write(gitlab);

                        var e2 = archive.CreateEntry("Jenkinsfile");
                        using (var s = e2.Open()) using (var sw = new StreamWriter(s)) sw.Write(jenkins);

                        if (execRes != null)
                        {
                            var e3 = archive.CreateEntry("execution.log");
                            using (var s = e3.Open()) using (var sw = new StreamWriter(s)) sw.Write($"Exit: {execRes.ExitCode}\nStdOut:\n{execRes.StdOut}\nStdErr:\n{execRes.StdErr}");
                        }
                    }
                    ms.Seek(0, SeekOrigin.Begin);
                    return File(ms.ToArray(), "application/zip", "pipelines.zip");
                }

                var resp = new
                {
                    analysis = new
                    {
                        language = analysis.Language.ToString(),
                        framework = analysis.Framework,
                        hasDockerfile = analysis.HasDockerfile,
                        buildCommands = analysis.SuggestedBuildCommands
                    },
                    GitLabCI = gitlab,
                    Jenkinsfile = jenkins,
                    Execution = execRes
                };

                return Ok(resp);
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(repoPath)) _gitService.DeleteRepository(repoPath);
                return StatusCode(500, ex.Message);
            }
        }

        private static (string, bool) ParseBody(string raw, string repoFromQuery, bool execFromQuery)
        {
            var repo = repoFromQuery;
            var exec = execFromQuery;
            if (string.IsNullOrWhiteSpace(raw)) return (repo, exec);

            try
            {
                var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.String) return (root.GetString() ?? repo, exec);

                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("repoUrl", out var p) && p.ValueKind == JsonValueKind.String) repo = p.GetString() ?? repo;
                    else if (root.TryGetProperty("RepoUrl", out var p2) && p2.ValueKind == JsonValueKind.String) repo = p2.GetString() ?? repo;

                    if (!exec && root.TryGetProperty("execute", out var pe) && (pe.ValueKind == JsonValueKind.True || (pe.ValueKind == JsonValueKind.String && pe.GetString() == "true"))) exec = true;
                }

                return (repo, exec);
            }
            catch
            {
                return (repo, exec);
            }
        }
    }
}