using System.Text.RegularExpressions;
using Ci_Cd.Models;

namespace Ci_Cd.Services
{
    public interface ITemplateEngine
    {
        string Render(string template, RepoAnalysisResult analysis);
        string RenderForGitLab(string template, RepoAnalysisResult analysis);
        string RenderForJenkins(string template, RepoAnalysisResult analysis);
    }

    public class TemplateEngine : ITemplateEngine
    {
        private readonly IVariableMapper _variableMapper;

        public TemplateEngine(IVariableMapper variableMapper)
        {
            _variableMapper = variableMapper;
        }

        public string Render(string template, RepoAnalysisResult analysis)
        {
            var result = template;
            
            result = result.Replace("{{PROJECT_NAME}}", analysis.ProjectName);
            result = result.Replace("{{PROJECT_VERSION}}", analysis.ProjectVersion);
            result = result.Replace("{{LANGUAGE}}", analysis.Language.ToString());
            result = result.Replace("{{FRAMEWORK}}", analysis.Framework);
            result = result.Replace("{{DOCKER_IMAGE}}", analysis.GetBestImage());
            result = result.Replace("{{BUILD_COMMANDS}}", string.Join("\n    - ", analysis.BuildCommands));
            result = result.Replace("{{TEST_COMMANDS}}", string.Join("\n    - ", analysis.TestCommands));
            
            // Handle conditional blocks
            result = ProcessConditionals(result, analysis);
            
            // Handle loops for subprojects
            result = ProcessLoops(result, analysis);
            
            return result;
        }
        
        public string RenderForGitLab(string template, RepoAnalysisResult analysis)
        {
            var rendered = Render(template, analysis);
            return _variableMapper.MapToGitLab(rendered);
        }
        
        public string RenderForJenkins(string template, RepoAnalysisResult analysis)
        {
            var rendered = Render(template, analysis);
            return _variableMapper.MapToJenkins(rendered);
        }

        private string ProcessConditionals(string template, RepoAnalysisResult analysis)
        {
            // {{#if HAS_DOCKERFILE}}...{{/if}}
            var ifPattern = @"\{\{#if\s+(\w+)\}\}(.*?)\{\{/if\}\}";
            return Regex.Replace(template, ifPattern, match =>
            {
                var condition = match.Groups[1].Value;
                var content = match.Groups[2].Value;
                
                var shouldInclude = condition switch
                {
                    "HAS_DOCKERFILE" => analysis.HasDockerfile,
                    "HAS_SUBPROJECTS" => analysis.Subprojects.Count > 0,
                    _ => false
                };
                
                return shouldInclude ? content : "";
            }, RegexOptions.Singleline);
        }

        private string ProcessLoops(string template, RepoAnalysisResult analysis)
        {
            // {{#each SUBPROJECTS}}...{{/each}}
            var eachPattern = @"\{\{#each\s+(\w+)\}\}(.*?)\{\{/each\}\}";
            return Regex.Replace(template, eachPattern, match =>
            {
                var collection = match.Groups[1].Value;
                var itemTemplate = match.Groups[2].Value;
                
                if (collection == "SUBPROJECTS" && analysis.Subprojects.Count > 0)
                {
                    return string.Join("\n", analysis.Subprojects.Select(sp => 
                        itemTemplate.Replace("{{SUBPROJECT}}", sp)));
                }
                
                return "";
            }, RegexOptions.Singleline);
        }
    }
}

