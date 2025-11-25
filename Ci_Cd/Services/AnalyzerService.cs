using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Ci_Cd.Models;
using Microsoft.Extensions.Logging;

namespace Ci_Cd.Services
{
    public class AnalyzerService : IAnalyzerService
    {
        private readonly ILogger<AnalyzerService> _logger;
        private readonly List<DetectorRule> _rules;

        public AnalyzerService(ILogger<AnalyzerService> logger)
        {
            _logger = logger;
            try
            {
                var cfgPath = Path.Combine(Directory.GetCurrentDirectory(), "detectors.json");
                if (File.Exists(cfgPath))
                {
                    var txt = File.ReadAllText(cfgPath);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var cfg = JsonSerializer.Deserialize<DetectorConfig>(txt, options);
                    _rules = cfg?.Frameworks ?? new List<DetectorRule>();
                    _logger.LogInformation("Successfully loaded {RuleCount} detector rules.", _rules.Count);
                }
                else
                {
                    _logger.LogWarning("detectors.json not found. Analyzer will use fallback mechanisms.");
                    _rules = new List<DetectorRule>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load or parse detectors.json.");
                _rules = new List<DetectorRule>();
            }
        }

        public RepoAnalysisResult Analyze(string repoPath)
        {
            var result = new RepoAnalysisResult();
            try
            {
                var allFiles = Directory.EnumerateFiles(repoPath, "*", SearchOption.AllDirectories).ToList();
                
                DetectDockerfile(allFiles, result);

                var bestRule = FindBestRule(allFiles);

                if (bestRule != null)
                {
                    if (Enum.TryParse<RepoAnalysisResult.ProjectLanguage>(bestRule.Language, true, out var langEnum))
                    {
                        result.Language = langEnum;
                    }
                    result.Framework = bestRule.Name;
                    result.SuggestedBuildCommands.AddRange(bestRule.BuildCommands);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during repository analysis at {RepoPath}", repoPath);
            }
            
            return result;
        }

        private void DetectDockerfile(List<string> allFiles, RepoAnalysisResult result)
        {
            if (allFiles.Any(f => Path.GetFileName(f).StartsWith("Dockerfile", StringComparison.OrdinalIgnoreCase)))
            {
                result.HasDockerfile = true;
            }
        }

        private DetectorRule? FindBestRule(List<string> allFiles)
        {
            if (_rules == null || !_rules.Any()) return null;

            var fileContentsCache = new Dictionary<string, string>();
            var matches = new List<(DetectorRule rule, double score, int triggerCount)>();

            var fileNames = allFiles.Select(f => Path.GetFileName(f).ToLowerInvariant()).ToList();

            foreach (var rule in _rules)
            {
                // Fast check: does this rule even apply based on language triggers?
                if (rule.LanguageDetectionTriggers != null && rule.LanguageDetectionTriggers.Any())
                {
                    int triggerCount = 0;
                    
                    foreach (var trigger in rule.LanguageDetectionTriggers)
                    {
                        var lowerTrigger = trigger.ToLowerInvariant();
                        // Check if trigger is a file extension (starts with .)
                        if (lowerTrigger.StartsWith("."))
                        {
                            // Count how many files have this extension
                            var count = fileNames.Count(f => f.EndsWith(lowerTrigger));
                            triggerCount += count;
                        }
                        else
                        {
                            // Exact filename match
                            if (fileNames.Contains(lowerTrigger))
                            {
                                triggerCount += 1;
                            }
                        }
                    }

                    if (triggerCount > 0)
                    {
                        double score = CalculateScore(rule, allFiles, fileContentsCache);
                        // Add bonus based on how many trigger files were found
                        // This helps distinguish primary language from auxiliary files
                        score += Math.Log10(triggerCount + 1) * 2;
                        
                        if (score >= rule.Threshold)
                        {
                            matches.Add((rule, score, triggerCount));
                            _logger.LogInformation("Rule '{RuleName}' matched: score={Score:F2}, triggerCount={TriggerCount}", 
                                rule.Name, score, triggerCount);
                        }
                    }
                }
            }

            if (matches.Any())
            {
                // The highest score wins. This naturally prioritizes:
                // 1. Languages with more source files
                // 2. Specific frameworks over generic language rules
                var best = matches.OrderByDescending(m => m.score).ThenByDescending(m => m.triggerCount).First();
                _logger.LogInformation("Selected rule: '{RuleName}' with score {Score:F2}", best.rule.Name, best.score);
                return best.rule;
            }

            return null;
        }

        private double CalculateScore(DetectorRule rule, List<string> allFiles, Dictionary<string, string> fileCache)
        {
            double score = 0;
            
            // File patterns check
            score += (rule.FilePatterns ?? new List<string>())
                .Where(pattern => IsPatternPresent(pattern, allFiles))
                .Sum(pattern => rule.Weight * 0.6);

            // Dependency and Regex checks (require reading files)
            var relevantFiles = GetRelevantFilesForRules(rule, allFiles);
            foreach (var file in relevantFiles)
            {
                if (!fileCache.TryGetValue(file, out var content))
                {
                    try
                    {
                        content = File.ReadAllText(file);
                        fileCache[file] = content;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not read file {File} for analysis.", file);
                        content = string.Empty;
                    }
                }

                if (string.IsNullOrEmpty(content)) continue;

                score += (rule.DependencyNames ?? new List<string>())
                    .Where(dep => content.Contains(dep, StringComparison.OrdinalIgnoreCase))
                    .Sum(dep => rule.Weight * 1.0);

                score += (rule.FileRegex ?? new List<string>())
                    .Where(rx => Regex.IsMatch(content, rx, RegexOptions.IgnoreCase))
                    .Sum(rx => rule.Weight * 0.8);
            }

            return score;
        }
        
        private IEnumerable<string> GetRelevantFilesForRules(DetectorRule rule, List<string> allFiles)
        {
            // This method determines which files are worth reading based on the rule.
            // For example, for Node, it's package.json. For .NET, it's *.csproj, etc.
            var lang = rule.Language.ToLowerInvariant();
            switch (lang)
            {
                case "dotnet":
                    return allFiles.Where(f => f.EndsWith(".csproj"));
                case "nodejs":
                    return allFiles.Where(f => f.EndsWith("package.json"));
                case "python":
                    return allFiles.Where(f => f.EndsWith("requirements.txt") || f.EndsWith("pyproject.toml"));
                case "go":
                    return allFiles.Where(f => f.EndsWith("go.mod"));
                case "java":
                    return allFiles.Where(f => f.EndsWith("pom.xml") || f.EndsWith("build.gradle") || f.EndsWith("build.gradle.kts"));
                case "rust":
                    return allFiles.Where(f => f.EndsWith("cargo.toml"));
                case "php":
                    return allFiles.Where(f => f.EndsWith("composer.json"));
                case "ruby":
                    return allFiles.Where(f => f.EndsWith("Gemfile"));
                case "elixir":
                    return allFiles.Where(f => f.EndsWith("mix.exs"));
                default:
                    return Enumerable.Empty<string>();
            }
        }

        private bool IsPatternPresent(string pattern, List<string> allFiles)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            var pat = pattern.ToLowerInvariant();
            if (pat.Contains('*'))
            {
                var regexPattern = "^" + Regex.Escape(pat).Replace("\\*", ".*") + "$";
                return allFiles.Any(f => Regex.IsMatch(Path.GetFileName(f), regexPattern, RegexOptions.IgnoreCase));
            }
            return allFiles.Any(f => Path.GetFileName(f).Equals(pat, StringComparison.OrdinalIgnoreCase));
        }
    }
}
