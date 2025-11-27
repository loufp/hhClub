using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Ci_Cd.Models;
using System.Collections.Generic;

namespace Ci_Cd.Services
{
    public interface IArtifactUploader
    {
        Task<UploaderResult> Upload(string filePath, CancellationToken cancellationToken = default);
    }

    public class NexusUploader : IArtifactUploader
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string _repo;
        private readonly string _user;
        private readonly string _pass;
        private readonly UploaderOptions _options;
        private readonly ILogger? _logger;

        public NexusUploader(HttpClient http, string baseUrl, string repo, string user, string pass, UploaderOptions? options = null, ILogger? logger = null)
        {
            _http = http ?? new HttpClient();
            _baseUrl = baseUrl.TrimEnd('/');
            _repo = repo;
            _user = user;
            _pass = pass;
            _options = options ?? new UploaderOptions();
            _logger = logger;
        }

        public async Task<UploaderResult> Upload(string filePath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath)) return new UploaderResult{ Success = false, Message = "File not found" };
            var attempts = _options.RetryAttempts;
            var baseDelay = _options.BaseDelaySeconds;
            return await HttpRetryHelper.RetryAsync<UploaderResult>(async ct =>
            {
                using var fs = File.OpenRead(filePath);
                var target = $"{_baseUrl}/repository/{_repo}/{Path.GetFileName(filePath)}";
                var req = new HttpRequestMessage(HttpMethod.Put, target) { Content = new StreamContent(fs) };
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_user}:{_pass}")));
                var checksum = HttpRetryHelper.ComputeSha256Hex(filePath);
                req.Headers.Remove("X-Checksum-Sha256");
                req.Headers.Add("X-Checksum-Sha256", checksum);

                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (resp.IsSuccessStatusCode)
                {
                    // verify via HEAD
                    try
                    {
                        var headReq = new HttpRequestMessage(HttpMethod.Head, target);
                        headReq.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_user}:{_pass}")));
                        using var headResp = await _http.SendAsync(headReq, HttpCompletionOption.ResponseHeadersRead, ct);
                        if (headResp.IsSuccessStatusCode)
                        {
                            if (headResp.Content.Headers.ContentLength.HasValue)
                            {
                                var remoteSize = headResp.Content.Headers.ContentLength.Value;
                                var localSize = new FileInfo(filePath).Length;
                                if (remoteSize != localSize)
                                {
                                    _logger?.LogWarning("Uploaded size mismatch: local={Local} remote={Remote}", localSize, remoteSize);
                                    return new UploaderResult{ Success = false, Message = "Uploaded size mismatch" };
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to verify upload via HEAD");
                    }

                    return new UploaderResult{ Success = true, Message = body };
                }

                if ((int)resp.StatusCode == 429)
                {
                    if (resp.Headers.TryGetValues("Retry-After", out var vals))
                    {
                        if (int.TryParse(vals.FirstOrDefault(), out var seconds))
                        {
                            _logger?.LogInformation("Server asked to retry after {Sec}s", seconds);
                            await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
                        }
                    }
                }

                if (HttpRetryHelper.IsTransientStatus(resp.StatusCode)) throw new Exception($"Transient HTTP {(int)resp.StatusCode}: {body}");
                return new UploaderResult{ Success = false, Message = $"HTTP {(int)resp.StatusCode}: {body}" };
            }, attempts: attempts, baseDelaySeconds: baseDelay);
        }
    }

    public class ArtifactoryUploader : IArtifactUploader
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string _repo;
        private readonly string _user;
        private readonly string _pass;
        private readonly UploaderOptions _options;
        private readonly ILogger? _logger;

        public ArtifactoryUploader(HttpClient http, string baseUrl, string repo, string user, string pass, UploaderOptions? options = null, ILogger? logger = null)
        {
            _http = http ?? new HttpClient();
            _baseUrl = baseUrl.TrimEnd('/');
            _repo = repo;
            _user = user;
            _pass = pass;
            _options = options ?? new UploaderOptions();
            _logger = logger;
        }

        public async Task<UploaderResult> Upload(string filePath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath)) return new UploaderResult{ Success = false, Message = "File not found" };
            var attempts = _options.RetryAttempts;
            var baseDelay = _options.BaseDelaySeconds;
            return await HttpRetryHelper.RetryAsync<UploaderResult>(async ct =>
            {
                using var fs = File.OpenRead(filePath);
                var target = $"{_baseUrl}/{_repo}/{Path.GetFileName(filePath)}";
                var req = new HttpRequestMessage(HttpMethod.Put, target) { Content = new StreamContent(fs) };
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_user}:{_pass}")));
                var checksum = HttpRetryHelper.ComputeSha256Hex(filePath);
                req.Headers.Remove("X-Checksum-Sha256");
                req.Headers.Add("X-Checksum-Sha256", checksum);

                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (resp.IsSuccessStatusCode)
                {
                    // verify via HEAD
                    try
                    {
                        var headReq = new HttpRequestMessage(HttpMethod.Head, target);
                        headReq.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_user}:{_pass}")));
                        using var headResp = await _http.SendAsync(headReq, HttpCompletionOption.ResponseHeadersRead, ct);
                        if (headResp.IsSuccessStatusCode)
                        {
                            if (headResp.Content.Headers.ContentLength.HasValue)
                            {
                                var remoteSize = headResp.Content.Headers.ContentLength.Value;
                                var localSize = new FileInfo(filePath).Length;
                                if (remoteSize != localSize)
                                {
                                    _logger?.LogWarning("Uploaded size mismatch: local={Local} remote={Remote}", localSize, remoteSize);
                                    return new UploaderResult{ Success = false, Message = "Uploaded size mismatch" };
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to verify upload via HEAD");
                    }

                    return new UploaderResult{ Success = true, Message = body };
                }

                if ((int)resp.StatusCode == 429)
                {
                    if (resp.Headers.TryGetValues("Retry-After", out var vals))
                    {
                        if (int.TryParse(vals.FirstOrDefault(), out var seconds))
                        {
                            _logger?.LogInformation("Server asked to retry after {Sec}s", seconds);
                            await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
                        }
                    }
                }

                if (HttpRetryHelper.IsTransientStatus(resp.StatusCode)) throw new Exception($"Transient HTTP {(int)resp.StatusCode}: {body}");
                return new UploaderResult{ Success = false, Message = $"HTTP {(int)resp.StatusCode}: {body}" };
            }, attempts: attempts, baseDelaySeconds: baseDelay);
        }
    }

    public class GitHubReleasesUploader : IArtifactUploader
    {
        private readonly HttpClient _http;
        private readonly string _ownerRepo;
        private readonly string _token;
        private readonly string? _tag;
        private readonly UploaderOptions _options;
        private readonly ILogger? _logger;

        public GitHubReleasesUploader(HttpClient http, string ownerRepo, string token, string? tag = null, UploaderOptions? options = null, ILogger? logger = null)
        {
            _http = http ?? new HttpClient();
            _ownerRepo = ownerRepo;
            _token = token;
            _tag = tag;
            _options = options ?? new UploaderOptions();
            _logger = logger;
        }

        public async Task<UploaderResult> Upload(string filePath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath)) return new UploaderResult{ Success = false, Message = "File not found" };
            var tag = _tag ?? ("v" + DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var attempts = _options.RetryAttempts;
            var baseDelay = _options.BaseDelaySeconds;

            return await HttpRetryHelper.RetryAsync<UploaderResult>(async ct =>
            {
                // create release
                var payload = new { tag_name = tag, name = tag, draft = false, prerelease = false };
                var createReq = new HttpRequestMessage(HttpMethod.Post, $"https://api.github.com/repos/{_ownerRepo}/releases")
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                };
                createReq.Headers.UserAgent.ParseAdd("ci-cd-uploader/1.0");
                createReq.Headers.Authorization = new AuthenticationHeaderValue("token", _token);
                using var createResp = await _http.SendAsync(createReq, HttpCompletionOption.ResponseContentRead, ct);
                var createBody = await createResp.Content.ReadAsStringAsync(ct);
                string? uploadUrl = null;
                if (createResp.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(createBody);
                    if (doc.RootElement.TryGetProperty("upload_url", out var u)) uploadUrl = u.GetString();
                }

                if (string.IsNullOrEmpty(uploadUrl))
                {
                    var getReq = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{_ownerRepo}/releases/tags/{tag}");
                    getReq.Headers.UserAgent.ParseAdd("ci-cd-uploader/1.0");
                    getReq.Headers.Authorization = new AuthenticationHeaderValue("token", _token);
                    using var respGet = await _http.SendAsync(getReq, HttpCompletionOption.ResponseContentRead, ct);
                    if (respGet.IsSuccessStatusCode)
                    {
                        var getBody = await respGet.Content.ReadAsStringAsync(ct);
                        using var doc = JsonDocument.Parse(getBody);
                        if (doc.RootElement.TryGetProperty("upload_url", out var u2)) uploadUrl = u2.GetString();
                    }
                }

                if (string.IsNullOrEmpty(uploadUrl))
                {
                    var listReq = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{_ownerRepo}/releases");
                    listReq.Headers.UserAgent.ParseAdd("ci-cd-uploader/1.0");
                    listReq.Headers.Authorization = new AuthenticationHeaderValue("token", _token);
                    using var listResp = await _http.SendAsync(listReq, HttpCompletionOption.ResponseContentRead, ct);
                    if (listResp.IsSuccessStatusCode)
                    {
                        var listBody = await listResp.Content.ReadAsStringAsync(ct);
                        using var doc = JsonDocument.Parse(listBody);
                        if (doc.RootElement.GetArrayLength() > 0)
                        {
                            var first = doc.RootElement[0];
                            if (first.TryGetProperty("upload_url", out var u3)) uploadUrl = u3.GetString();
                        }
                    }
                }

                if (string.IsNullOrEmpty(uploadUrl)) return new UploaderResult{ Success = false, Message = "Could not determine upload_url" };
                uploadUrl = uploadUrl.Replace("{?name,label}", "");
                var uploadEndpoint = uploadUrl + "?name=" + Uri.EscapeDataString(Path.GetFileName(filePath));

                using var fs = File.OpenRead(filePath);
                var req = new HttpRequestMessage(HttpMethod.Post, uploadEndpoint) { Content = new StreamContent(fs) };
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                req.Headers.UserAgent.ParseAdd("ci-cd-uploader/1.0");
                req.Headers.Authorization = new AuthenticationHeaderValue("token", _token);
                req.Headers.Remove("X-Checksum-Sha256");
                req.Headers.Add("X-Checksum-Sha256", HttpRetryHelper.ComputeSha256Hex(filePath));

                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (resp.IsSuccessStatusCode) return new UploaderResult{ Success = true, Message = body };
                if ((int)resp.StatusCode == 429)
                {
                    if (resp.Headers.TryGetValues("Retry-After", out var vals))
                    {
                        if (int.TryParse(vals.FirstOrDefault(), out var seconds))
                        {
                            _logger?.LogInformation("Server asked to retry after {Sec}s", seconds);
                            await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
                        }
                    }
                }

                if (HttpRetryHelper.IsTransientStatus(resp.StatusCode)) throw new Exception($"Transient HTTP {(int)resp.StatusCode}: {body}");
                return new UploaderResult{ Success = false, Message = $"HTTP {(int)resp.StatusCode}: {body}" };
            }, attempts: attempts, baseDelaySeconds: baseDelay);
        }
    }

    public class DockerRegistryUploader : IArtifactUploader
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string _repository; // e.g. namespace/repo
        private readonly string? _token; // bearer token
        private readonly UploaderOptions _options;
        private readonly ILogger? _logger;
        private readonly string _tag;

        public DockerRegistryUploader(HttpClient http, string baseUrl, string repository, string? token = null, string tag = "latest", UploaderOptions? options = null, ILogger? logger = null)
        {
            _http = http ?? new HttpClient();
            _baseUrl = baseUrl.TrimEnd('/');
            _repository = repository;
            _token = token;
            _tag = string.IsNullOrWhiteSpace(tag) ? "latest" : tag;
            _options = options ?? new UploaderOptions();
            _logger = logger;
        }

        public async Task<UploaderResult> Upload(string filePath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath)) return new UploaderResult { Success = false, Message = "File not found" };
            var attempts = _options.RetryAttempts;
            var baseDelay = _options.BaseDelaySeconds;
            var layerDigestHex = HttpRetryHelper.ComputeSha256Hex(filePath);
            var layerDigest = "sha256:" + layerDigestHex;
            var layerSize = new FileInfo(filePath).Length;

            return await HttpRetryHelper.RetryAsync<UploaderResult>(async ct =>
            {
                // helper local function to init/patch/commit upload
                async Task<string> UploadBlobAsync(Stream content, CancellationToken innerCt)
                {
                    var initUrl = $"{_baseUrl}/v2/{_repository}/blobs/uploads/";
                    var initReq = new HttpRequestMessage(HttpMethod.Post, initUrl);
                    if (!string.IsNullOrEmpty(_token)) initReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                    using var initResp = await _http.SendAsync(initReq, HttpCompletionOption.ResponseHeadersRead, innerCt);
                    if (!initResp.IsSuccessStatusCode)
                    {
                        var body = await initResp.Content.ReadAsStringAsync(innerCt);
                        if (HttpRetryHelper.IsTransientStatus(initResp.StatusCode)) throw new Exception($"Transient init {(int)initResp.StatusCode}: {body}");
                        throw new Exception($"Init failed {(int)initResp.StatusCode}: {body}");
                    }
                    var location = initResp.Headers.Location?.ToString() ?? (initResp.Headers.TryGetValues("Location", out var locVals) ? locVals.FirstOrDefault() : null);
                    if (string.IsNullOrEmpty(location)) throw new Exception("No upload location returned");
                    if (!location.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        location = _baseUrl.TrimEnd('/') + (location.StartsWith("/") ? "" : "/") + location;

                    var patchReq = new HttpRequestMessage(new HttpMethod("PATCH"), location) { Content = new StreamContent(content) };
                    patchReq.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    if (!string.IsNullOrEmpty(_token)) patchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                    using var patchResp = await _http.SendAsync(patchReq, HttpCompletionOption.ResponseHeadersRead, innerCt);
                    if (!patchResp.IsSuccessStatusCode)
                    {
                        var body = await patchResp.Content.ReadAsStringAsync(innerCt);
                        if (HttpRetryHelper.IsTransientStatus(patchResp.StatusCode)) throw new Exception($"Transient patch {(int)patchResp.StatusCode}: {body}");
                        throw new Exception($"Patch failed {(int)patchResp.StatusCode}: {body}");
                    }
                    var commitLocation = patchResp.Headers.Location?.ToString() ?? location;
                    if (!commitLocation.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        commitLocation = _baseUrl.TrimEnd('/') + (commitLocation.StartsWith("/") ? "" : "/") + commitLocation;

                    return commitLocation;
                }

                // upload layer blob
                using var fsLayer = File.OpenRead(filePath);
                var commitLocationLayer = await UploadBlobAsync(fsLayer, ct);
                var commitUrlLayer = commitLocationLayer;
                if (!commitUrlLayer.Contains("?")) commitUrlLayer += "?";
                commitUrlLayer += (commitUrlLayer.EndsWith("?") ? "" : "&") + "digest=" + Uri.EscapeDataString(layerDigest);
                var putReqLayer = new HttpRequestMessage(HttpMethod.Put, commitUrlLayer);
                if (!string.IsNullOrEmpty(_token)) putReqLayer.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                using var putRespLayer = await _http.SendAsync(putReqLayer, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!putRespLayer.IsSuccessStatusCode)
                {
                    var body = await putRespLayer.Content.ReadAsStringAsync(ct);
                    if (HttpRetryHelper.IsTransientStatus(putRespLayer.StatusCode)) throw new Exception($"Transient commit layer {(int)putRespLayer.StatusCode}: {body}");
                    return new UploaderResult { Success = false, Message = $"Commit layer failed {(int)putRespLayer.StatusCode}: {body}" };
                }

                // build minimal config json
                var configObj = new
                {
                    created = DateTime.UtcNow.ToString("o"),
                    architecture = "amd64",
                    os = "linux",
                    config = new { Cmd = new[] { "/bin/sh" } },
                    rootfs = new { type = "layers", diff_ids = new[] { layerDigest } }
                };
                var configJson = JsonSerializer.Serialize(configObj);
                var configBytes = Encoding.UTF8.GetBytes(configJson);
                var configDigestHex = BitConverter.ToString(System.Security.Cryptography.SHA256.Create().ComputeHash(configBytes)).Replace("-", "").ToLowerInvariant();
                var configDigest = "sha256:" + configDigestHex;
                var configSize = configBytes.Length;

                // upload config blob
                using var msConfig = new MemoryStream(configBytes);
                var commitLocationConfig = await UploadBlobAsync(msConfig, ct);
                var commitUrlConfig = commitLocationConfig;
                if (!commitUrlConfig.Contains("?")) commitUrlConfig += "?";
                commitUrlConfig += (commitUrlConfig.EndsWith("?") ? "" : "&") + "digest=" + Uri.EscapeDataString(configDigest);
                var putReqConfig = new HttpRequestMessage(HttpMethod.Put, commitUrlConfig);
                if (!string.IsNullOrEmpty(_token)) putReqConfig.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                using var putRespConfig = await _http.SendAsync(putReqConfig, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!putRespConfig.IsSuccessStatusCode)
                {
                    var body = await putRespConfig.Content.ReadAsStringAsync(ct);
                    if (HttpRetryHelper.IsTransientStatus(putRespConfig.StatusCode)) throw new Exception($"Transient commit config {(int)putRespConfig.StatusCode}: {body}");
                    return new UploaderResult { Success = false, Message = $"Commit config failed {(int)putRespConfig.StatusCode}: {body}" };
                }

                // publish strict manifest referencing config+layer
                var manifestUrl = $"{_baseUrl}/v2/{_repository}/manifests/{_tag}";
                var manifest = new
                {
                    schemaVersion = 2,
                    mediaType = "application/vnd.docker.distribution.manifest.v2+json",
                    config = new
                    {
                        mediaType = "application/vnd.docker.container.image.v1+json",
                        size = configSize,
                        digest = configDigest
                    },
                    layers = new[]
                    {
                        new {
                            mediaType = "application/vnd.docker.image.rootfs.diff.tar.gzip",
                            size = layerSize,
                            digest = layerDigest
                        }
                    }
                };
                var manifestJson = JsonSerializer.Serialize(manifest);
                var mReq = new HttpRequestMessage(HttpMethod.Put, manifestUrl)
                {
                    Content = new StringContent(manifestJson, Encoding.UTF8, "application/vnd.docker.distribution.manifest.v2+json")
                };
                if (!string.IsNullOrEmpty(_token)) mReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                using var mResp = await _http.SendAsync(mReq, HttpCompletionOption.ResponseContentRead, ct);
                var mBody = await mResp.Content.ReadAsStringAsync(ct);
                if (!mResp.IsSuccessStatusCode)
                {
                    if (HttpRetryHelper.IsTransientStatus(mResp.StatusCode)) throw new Exception($"Transient manifest {(int)mResp.StatusCode}: {mBody}");
                    return new UploaderResult { Success = false, Message = $"Manifest failed {(int)mResp.StatusCode}: {mBody}" };
                }

                // verify tag
                var tagUrl = $"{_baseUrl}/v2/{_repository}/tags/list";
                var tReq = new HttpRequestMessage(HttpMethod.Get, tagUrl);
                if (!string.IsNullOrEmpty(_token)) tReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                using var tResp = await _http.SendAsync(tReq, HttpCompletionOption.ResponseContentRead, ct);
                var tBody = await tResp.Content.ReadAsStringAsync(ct);
                if (tResp.IsSuccessStatusCode && tBody.Contains(_tag, StringComparison.Ordinal))
                {
                    return new UploaderResult { Success = true, Message = "Pushed and tag available" };
                }

                return new UploaderResult { Success = true, Message = "Pushed manifest" };
            }, attempts: attempts, baseDelaySeconds: baseDelay);
        }
    }

    public class UploaderResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class UploaderOptions
    {
        public int RetryAttempts { get; set; } = 5;
        public int BaseDelaySeconds { get; set; } = 1;
    }
}
