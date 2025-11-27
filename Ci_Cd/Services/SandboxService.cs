using System.Net;
using System.Text;
using System.Diagnostics;

namespace Ci_Cd.Services
{
    public class SandboxService : ISandboxService
    {
        public bool ValidateRepositoryUrl(string repoUrl, out string? reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(repoUrl)) { reason = "Empty URL"; return false; }
            try
            {
                // allow ssh git@... and https urls
                string host = repoUrl;
                if (repoUrl.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
                {
                    var at = repoUrl.IndexOf('@');
                    var colon = repoUrl.IndexOf(':', at + 1);
                    host = repoUrl.Substring(at + 1, (colon > at ? colon : repoUrl.Length) - (at + 1));
                }
                else if (Uri.TryCreate(repoUrl, UriKind.Absolute, out var u))
                {
                    host = u.Host;
                }

                if (string.IsNullOrEmpty(host)) { reason = "Could not determine host"; return false; }
                if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.Equals("127.0.0.1") || host.Equals("::1")) { reason = "Localhost not allowed"; return false; }

                var ips = Dns.GetHostAddresses(host);
                foreach (var ip in ips)
                {
                    if (IPAddress.IsLoopback(ip)) { reason = "Resolved to loopback"; return false; }
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        var b = ip.GetAddressBytes();
                        if (b[0] == 10) { reason = "Private IPv4 10.x.x.x not allowed"; return false; }
                        if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) { reason = "Private IPv4 172.16.0.0/12 not allowed"; return false; }
                        if (b[0] == 192 && b[1] == 168) { reason = "Private IPv4 192.168.x.x not allowed"; return false; }
                        if (b[0] == 169 && b[1] == 254) { reason = "Link-local 169.254.x.x not allowed"; return false; }
                    }
                    else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                    {
                        var b = ip.GetAddressBytes();
                        if ((b[0] & 0xfe) == 0xfc) { reason = "Unique local IPv6 fc00::/7 not allowed"; return false; }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        public async Task<ExecutionResult> RunInSandbox(string workingDirectory, IEnumerable<string> commands, string image, TimeSpan timeout, SandboxOptions? options = null)
        {
            options ??= new SandboxOptions();
            var result = new ExecutionResult();
            var sbOut = new StringBuilder();
            var sbErr = new StringBuilder();

            // Check docker availability
            try
            {
                var check = new ProcessStartInfo("docker", "version --format '{{.Client.Version}}'")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var pc = Process.Start(check);
                if (pc == null) { result.ExitCode = -2; result.StdErr = "Docker not available"; return result; }
                pc.WaitForExit(2000);
                if (pc.ExitCode != 0) { result.ExitCode = -2; result.StdErr = pc.StandardError.ReadToEnd(); return result; }
            }
            catch (Exception ex)
            {
                result.ExitCode = -2; result.StdErr = ex.Message; return result;
            }

            // compose docker run arguments with resource limits and network isolation
            var cpuArg = $"--cpus={options.Cpus.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            var memArg = $"--memory={options.Memory}";
            var pidsArg = $"--pids-limit={options.PidsLimit}";
            var netArg = options.NetworkNone ? "--network=none" : string.Empty;
            var volumeArg = $"-v \"{workingDirectory}:/work:rw\" -w /work";

            // join commands into single script
            var script = string.Join(" && ", commands.Select(c => c.Replace("\"", "\\\"").Replace("$", "\\$")));
            var dockerArgs = $"run --rm {cpuArg} {memArg} {pidsArg} {netArg} {volumeArg} {image} /bin/sh -c \"{script}\"";

            var psi = new ProcessStartInfo("docker", dockerArgs)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = new Process { StartInfo = psi };
            p.OutputDataReceived += (_, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };

            try
            {
                p.Start();
            }
            catch (Exception ex)
            {
                result.ExitCode = -1; result.StdErr = ex.Message; return result;
            }

            p.BeginOutputReadLine(); p.BeginErrorReadLine();

            var exited = await Task.Run(() => p.WaitForExit((int)timeout.TotalMilliseconds));
            if (!exited)
            {
                try { p.Kill(); } catch { }
                sbErr.AppendLine("Sandbox command timed out");
                result.ExitCode = -1; result.StdOut = sbOut.ToString(); result.StdErr = sbErr.ToString(); return result;
            }

            result.ExitCode = p.ExitCode;
            result.StdOut = sbOut.ToString();
            result.StdErr = sbErr.ToString();
            return result;
        }
    }
}
