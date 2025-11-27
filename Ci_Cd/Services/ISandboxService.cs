namespace Ci_Cd.Services
{
    public interface ISandboxService
    {
        Task<ExecutionResult> RunInSandbox(string workingDirectory, IEnumerable<string> commands, string image, TimeSpan timeout, SandboxOptions? options = null);
        bool ValidateRepositoryUrl(string repoUrl, out string? reason);
    }

    public class SandboxOptions
    {
        public double Cpus { get; set; } = 0.5; // default half CPU
        public string Memory { get; set; } = "256m"; // default memory limit
        public int PidsLimit { get; set; } = 64;
        public bool NetworkNone { get; set; } = true;
    }
}
