using System.Diagnostics;
using System.Net;
using System.Text;

namespace Ci_Cd.Services;

public class GitServices: IGitServices
{

public string CloneRepository(string repoUrl)
{
    // basic URL validation
    if (string.IsNullOrWhiteSpace(repoUrl)) throw new ArgumentException("repoUrl is empty");

    // allow common git URL schemes (ssh/git@, https, http, git)
    string? host = null;
    try
    {
        if (repoUrl.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
        {
            // git@github.com:owner/repo.git -> host after '@' and before ':'
            var at = repoUrl.IndexOf('@');
            var colon = repoUrl.IndexOf(':', at + 1);
            host = repoUrl.Substring(at + 1, (colon > at ? colon : repoUrl.Length) - (at + 1));
        }
        else
        {
            if (Uri.TryCreate(repoUrl, UriKind.Absolute, out var u)) host = u.Host;
        }
    }
    catch (Exception ex) { Console.Error.WriteLine($"Host parse failed: {ex.Message}"); host = null; }

    if (string.IsNullOrEmpty(host)) throw new ArgumentException("Could not determine host from repoUrl");

    // disallow obvious local hosts
    var hostLower = host.ToLowerInvariant();
    if (hostLower == "localhost" || hostLower == "127.0.0.1" || hostLower == "::1")
        throw new ArgumentException("Local hosts are not allowed");

    // resolve DNS and ensure no private IPs
    try
    {
        var ips = Dns.GetHostAddresses(host);
        foreach (var ip in ips)
        {
            if (IPAddress.IsLoopback(ip)) throw new ArgumentException("Resolved to loopback address");
            var bytes = ip.GetAddressBytes();
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                // IPv4 private ranges
                // 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, 169.254.0.0/16
                if (bytes[0] == 10) throw new ArgumentException("Resolved to private IPv4 address");
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) throw new ArgumentException("Resolved to private IPv4 address");
                if (bytes[0] == 192 && bytes[1] == 168) throw new ArgumentException("Resolved to private IPv4 address");
                if (bytes[0] == 169 && bytes[1] == 254) throw new ArgumentException("Resolved to link-local IPv4 address");
            }
            else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                // IPv6 unique/local addresses fc00::/7
                if ((bytes[0] & 0xfe) == 0xfc) throw new ArgumentException("Resolved to private IPv6 address");
            }
        }
    }
    catch (ArgumentException) { throw; }
    catch (Exception ex)
    {
        // DNS resolution may fail — be conservative and fail
        throw new ArgumentException($"Failed to resolve repo host: {ex.Message}");
    }

    var tempPath = Path.Combine(Path.GetTempPath(), "PipelineGen_" + Guid.NewGuid().ToString());
    Directory.CreateDirectory(tempPath);

    // prepare git clone command safely (quote repoUrl)
    string QuoteArg(string a)
    {
        if (string.IsNullOrEmpty(a)) return "\"\"";
        return '"' + a.Replace("\"", "\\\"") + '"';
    }

    var repoArg = QuoteArg(repoUrl);
    var args = $"clone --depth 1 {repoArg} .";

    var processInfo = new ProcessStartInfo("git")
    {
        Arguments = args,
        WorkingDirectory = tempPath,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    Console.WriteLine($"[Git] Запускаю клонирование: {repoUrl}");

    var process = new Process();
    process.StartInfo = processInfo;
    var stdout = new StringBuilder();
    var stderr = new StringBuilder();

    process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
    process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

    try
    {
        if (!process.Start()) throw new Exception("Failed to start git process");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // timeout (milliseconds)
        var timeoutMs = 2 * 60 * 1000; // 2 minutes
        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(true); } catch (Exception ex) { Console.Error.WriteLine($"Failed to kill process: {ex.Message}"); }
            throw new TimeoutException("Git clone timed out");
        }

        if (process.ExitCode != 0)
        {
            var err = stderr.ToString();
            // cleanup on failure
            try { SetAttributesNormal(new DirectoryInfo(tempPath)); Directory.Delete(tempPath, true); } catch (Exception ex) { Console.Error.WriteLine($"Cleanup failed: {ex.Message}"); }
            throw new Exception("Git clone failed (exit " + process.ExitCode + "): " + err);
        }

        Console.WriteLine("[Git] Клонирование завершено.");
        return tempPath;
    }
    catch
    {
        // ensure cleanup on any failure
        try { if (Directory.Exists(tempPath)) { SetAttributesNormal(new DirectoryInfo(tempPath)); Directory.Delete(tempPath, true); } } catch (Exception ex) { Console.Error.WriteLine($"Cleanup failed: {ex.Message}"); }
        throw;
    }
}
    
    public void DeleteRepository(string path)
    {
        if(Directory.Exists(path))
        {
            var direct = new DirectoryInfo(path);
            SetAttributesNormal(direct);
            direct.Delete( true);
        }
    }

    private void SetAttributesNormal(DirectoryInfo dir)//рекурсивно снимаем защиту с файлов
        {
            foreach (var subDir in dir.GetDirectories())
            {
                SetAttributesNormal(subDir);
            }

            foreach (var file in dir.GetFiles())
            {
                file.Attributes = FileAttributes.Normal;
            }
        }
}