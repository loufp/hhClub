using System.Collections.Generic;

namespace Ci_Cd.Models
{
    public class DetectorConfig
    {
        public List<DetectorRule> Frameworks { get; set; } = new();
    }

    public class DetectorRule
    {
        public string Name { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public List<string> LanguageDetectionTriggers { get; set; } = new();
        public List<string> FilePatterns { get; set; } = new();
        public List<string> DependencyNames { get; set; } = new();
        public List<string> FileRegex { get; set; } = new();
        public List<string> BuildCommands { get; set; } = new();
        public double Weight { get; set; }
        public double Threshold { get; set; }
        public string Rationale { get; set; } = string.Empty;
    }
}
