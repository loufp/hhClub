using System.Security.Cryptography;
using System.Text;

namespace Ci_Cd.Services
{
    internal static class HttpRetryHelper
    {
        public static bool IsTransientStatus(System.Net.HttpStatusCode code)
        {
            var c = (int)code;
            if (c >= 500) return true;
            if (c == 429) return true;
            return false;
        }

        public static string ComputeSha256Hex(string filePath)
        {
            using var stream = System.IO.File.OpenRead(filePath);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        public static async Task<T> RetryAsync<T>(Func<CancellationToken, Task<T>> action, int attempts = 4, int baseDelaySeconds = 2, CancellationToken cancellationToken = default)
        {
            Exception? lastEx = null;
            var rnd = new Random();
            for (int i = 0; i < attempts; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await action(cancellationToken);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    lastEx = ex;
                    var backoff = baseDelaySeconds * Math.Pow(2, i);
                    var jitter = rnd.NextDouble() * baseDelaySeconds;
                    var delay = TimeSpan.FromSeconds(backoff + jitter);
                    try { await Task.Delay(delay, cancellationToken); } catch (OperationCanceledException) { throw; }
                }
            }
            throw lastEx ?? new Exception("Retry policy exhausted");
        }
    }
}

