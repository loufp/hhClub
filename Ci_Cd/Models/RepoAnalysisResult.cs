// csharp
using System.Collections.Generic;

namespace Ci_Cd.Models
{
    public class RepoAnalysisResult
    {
        public enum ProjectLanguage
        {
            Unknown,
            DotNet,
            NodeJs,
            Go,
            Python,
            Java,
            Rust,
            Cpp,
            PHP,
            Ruby,
            Elixir
        }

        public ProjectLanguage Language { get; set; } = ProjectLanguage.Unknown;
        public string Framework { get; set; } = string.Empty;
        public bool HasDockerfile { get; set; } = false;
        public List<string> SuggestedBuildCommands { get; } = new();
    }
}