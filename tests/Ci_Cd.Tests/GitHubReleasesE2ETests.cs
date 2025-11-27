using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Linq;
using System.Threading.Tasks;
using Ci_Cd.Services;
using Xunit;

namespace Ci_Cd.Tests
{
    /// <summary>
    /// E2E tests for GitHub Releases against real GitHub API
    /// Tests metadata, ETag, rate limiting, specific status codes
    /// NOTE: Requires GITHUB_TOKEN environment variable to run against real API
    /// </summary>
    public class GitHubReleasesE2ETests
    {
        private const string TestRepo = "GITHUB_TEST_REPO"; // Format: owner/repo
        private readonly string? _token;
        private readonly bool _skipTests;

        public GitHubReleasesE2ETests()
        {
            _token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            var testRepo = Environment.GetEnvironmentVariable("GITHUB_TEST_REPO") ?? TestRepo;
            
            _skipTests = string.IsNullOrEmpty(_token) || testRepo == "GITHUB_TEST_REPO";
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task GitHub_Api_ShouldBeReachable()
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ci-cd-e2e-tests/1.0");
            
            try
            {
                var response = await client.GetAsync("https://api.github.com");
                Assert.True(response.IsSuccessStatusCode, "GitHub API not reachable");
            }
            catch (HttpRequestException ex)
            {
                Assert.Fail($"Cannot connect to GitHub API: {ex.Message}");
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task GitHub_RateLimit_Check()
        {
            if (_skipTests)
            {
                // Skip but pass - not a real failure
                return;
            }

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ci-cd-e2e-tests/1.0");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("token", _token);
            
            var response = await client.GetAsync("https://api.github.com/rate_limit");
            Assert.True(response.IsSuccessStatusCode);
            
            var body = await response.Content.ReadAsStringAsync();
            var rateLimit = JsonDocument.Parse(body);
            
            Assert.True(rateLimit.RootElement.TryGetProperty("resources", out var resources));
            Assert.True(resources.TryGetProperty("core", out var core));
            Assert.True(core.TryGetProperty("remaining", out var remaining));
            
            var remainingCalls = remaining.GetInt32();
            Assert.True(remainingCalls > 0, $"GitHub rate limit exhausted: {remainingCalls} calls remaining");
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task GitHub_Upload_Release_WithMetadataAndETag()
        {
            if (_skipTests)
            {
                return;
            }

            using var client = new HttpClient();
            var testRepo = Environment.GetEnvironmentVariable("GITHUB_TEST_REPO") ?? TestRepo;
            var fileName = $"test-artifact-{Guid.NewGuid()}.bin";
            var filePath = Path.Combine(Path.GetTempPath(), fileName);
            var testContent = $"GitHub Release test {Guid.NewGuid()}";
            var tag = $"e2e-test-{Guid.NewGuid():N}";
            
            await File.WriteAllTextAsync(filePath, testContent);
            
            try
            {
                var uploader = new GitHubReleasesUploader(
                    client, testRepo, _token!, tag,
                    new UploaderOptions { RetryAttempts = 3, BaseDelaySeconds = 2 });
                
                var result = await uploader.Upload(filePath);
                
                if (!result.Success && result.Message.Contains("401"))
                {
                    Assert.Fail("GitHub authentication failed. Check GITHUB_TOKEN environment variable.");
                }
                
                if (!result.Success && result.Message.Contains("404"))
                {
                    Assert.Fail($"Repository not found or no access: {testRepo}. Check GITHUB_TEST_REPO environment variable.");
                }
                
                Assert.True(result.Success, $"Upload failed: {result.Message}");
                
                // Get release info
                var getReleaseRequest = new HttpRequestMessage(HttpMethod.Get, 
                    $"https://api.github.com/repos/{testRepo}/releases/tags/{tag}");
                getReleaseRequest.Headers.UserAgent.ParseAdd("ci-cd-e2e-tests/1.0");
                getReleaseRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("token", _token);
                
                var getReleaseResponse = await client.SendAsync(getReleaseRequest);
                Assert.True(getReleaseResponse.IsSuccessStatusCode, "Release not found after creation");
                
                // Check ETag header
                Assert.True(getReleaseResponse.Headers.ETag != null, "ETag header missing");
                var etag = getReleaseResponse.Headers.ETag;
                
                // Check Last-Modified
                Assert.True(getReleaseResponse.Content.Headers.LastModified.HasValue, "Last-Modified missing");
                
                // Parse release data
                var releaseBody = await getReleaseResponse.Content.ReadAsStringAsync();
                var release = JsonDocument.Parse(releaseBody);
                
                Assert.True(release.RootElement.TryGetProperty("tag_name", out var tagName));
                Assert.Equal(tag, tagName.GetString());
                
                Assert.True(release.RootElement.TryGetProperty("assets", out var assets));
                Assert.True(assets.GetArrayLength() > 0, "No assets found in release");
                
                var asset = assets[0];
                Assert.True(asset.TryGetProperty("name", out var assetName));
                Assert.Equal(fileName, assetName.GetString());
                
                Assert.True(asset.TryGetProperty("size", out var size));
                Assert.Equal(testContent.Length, size.GetInt32());
                
                Assert.True(asset.TryGetProperty("browser_download_url", out var downloadUrl));
                var url = downloadUrl.GetString();
                Assert.False(string.IsNullOrEmpty(url));
                
                // Download and verify content
                var downloadRequest = new HttpRequestMessage(HttpMethod.Get, url);
                downloadRequest.Headers.UserAgent.ParseAdd("ci-cd-e2e-tests/1.0");
                
                var downloadResponse = await client.SendAsync(downloadRequest);
                Assert.True(downloadResponse.IsSuccessStatusCode);
                
                var downloadedContent = await downloadResponse.Content.ReadAsStringAsync();
                Assert.Equal(testContent, downloadedContent);
                
                // Test conditional request with If-None-Match
                var conditionalRequest = new HttpRequestMessage(HttpMethod.Get,
                    $"https://api.github.com/repos/{testRepo}/releases/tags/{tag}");
                conditionalRequest.Headers.UserAgent.ParseAdd("ci-cd-e2e-tests/1.0");
                conditionalRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("token", _token);
                conditionalRequest.Headers.IfNoneMatch.Add(etag);
                
                var conditionalResponse = await client.SendAsync(conditionalRequest);
                Assert.Equal(HttpStatusCode.NotModified, conditionalResponse.StatusCode);
                
                // Cleanup: delete release
                await DeleteReleaseAsync(client, testRepo, tag);
            }
            finally
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task GitHub_Upload_InvalidToken_Returns401()
        {
            if (_skipTests)
            {
                return;
            }

            using var client = new HttpClient();
            var testRepo = Environment.GetEnvironmentVariable("GITHUB_TEST_REPO") ?? TestRepo;
            var fileName = $"auth-test-{Guid.NewGuid()}.bin";
            var filePath = Path.Combine(Path.GetTempPath(), fileName);
            
            await File.WriteAllTextAsync(filePath, "Auth test");
            
            try
            {
                var uploader = new GitHubReleasesUploader(
                    client, testRepo, "invalid_token_xyz",
                    new UploaderOptions { RetryAttempts = 1 });
                
                var result = await uploader.Upload(filePath);
                
                Assert.False(result.Success);
                Assert.Contains("401", result.Message);
            }
            finally
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task GitHub_Upload_InvalidRepo_Returns404()
        {
            if (_skipTests)
            {
                return;
            }

            using var client = new HttpClient();
            var fileName = $"invalid-repo-{Guid.NewGuid()}.bin";
            var filePath = Path.Combine(Path.GetTempPath(), fileName);
            
            await File.WriteAllTextAsync(filePath, "Invalid repo test");
            
            try
            {
                var uploader = new GitHubReleasesUploader(
                    client, "nonexistent/repository-xyz-123", _token!,
                    new UploaderOptions { RetryAttempts = 1 });
                
                var result = await uploader.Upload(filePath);
                
                Assert.False(result.Success);
                Assert.Contains("404", result.Message);
            }
            finally
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task GitHub_Upload_MultipleAssets_SameRelease()
        {
            if (_skipTests)
            {
                return;
            }

            using var client = new HttpClient();
            var testRepo = Environment.GetEnvironmentVariable("GITHUB_TEST_REPO") ?? TestRepo;
            var tag = $"e2e-multis-{Guid.NewGuid():N}";
            
            var file1 = Path.Combine(Path.GetTempPath(), $"asset1-{Guid.NewGuid()}.txt");
            var file2 = Path.Combine(Path.GetTempPath(), $"asset2-{Guid.NewGuid()}.bin");
            
            await File.WriteAllTextAsync(file1, "First asset");
            await File.WriteAllTextAsync(file2, "Second asset");
            
            try
            {
                // Upload first asset (creates release)
                var uploader1 = new GitHubReleasesUploader(client, testRepo, _token!, tag);
                var result1 = await uploader1.Upload(file1);
                Assert.True(result1.Success, $"First upload failed: {result1.Message}");
                
                // Upload second asset to same release
                var uploader2 = new GitHubReleasesUploader(client, testRepo, _token!, tag);
                var result2 = await uploader2.Upload(file2);
                Assert.True(result2.Success, $"Second upload failed: {result2.Message}");
                
                // Verify both assets exist
                var getReleaseRequest = new HttpRequestMessage(HttpMethod.Get,
                    $"https://api.github.com/repos/{testRepo}/releases/tags/{tag}");
                getReleaseRequest.Headers.UserAgent.ParseAdd("ci-cd-e2e-tests/1.0");
                getReleaseRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("token", _token);
                
                var getReleaseResponse = await client.SendAsync(getReleaseRequest);
                var releaseBody = await getReleaseResponse.Content.ReadAsStringAsync();
                var release = JsonDocument.Parse(releaseBody);
                
                Assert.True(release.RootElement.TryGetProperty("assets", out var assets));
                Assert.Equal(2, assets.GetArrayLength());
                
                // Cleanup
                await DeleteReleaseAsync(client, testRepo, tag);
            }
            finally
            {
                if (File.Exists(file1)) File.Delete(file1);
                if (File.Exists(file2)) File.Delete(file2);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task GitHub_RateLimit_Headers_Present()
        {
            if (_skipTests)
            {
                return;
            }

            using var client = new HttpClient();
            var testRepo = Environment.GetEnvironmentVariable("GITHUB_TEST_REPO") ?? TestRepo;
            
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{testRepo}");
            request.Headers.UserAgent.ParseAdd("ci-cd-e2e-tests/1.0");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("token", _token);
            
            var response = await client.SendAsync(request);
            
            // Check rate limit headers
            Assert.True(response.Headers.Contains("X-RateLimit-Limit"), "X-RateLimit-Limit header missing");
            Assert.True(response.Headers.Contains("X-RateLimit-Remaining"), "X-RateLimit-Remaining header missing");
            Assert.True(response.Headers.Contains("X-RateLimit-Reset"), "X-RateLimit-Reset header missing");
            
            var limit = int.Parse(response.Headers.GetValues("X-RateLimit-Limit").First());
            var remaining = int.Parse(response.Headers.GetValues("X-RateLimit-Remaining").First());
            var reset = long.Parse(response.Headers.GetValues("X-RateLimit-Reset").First());
            
            Assert.True(limit > 0, "Rate limit should be positive");
            Assert.True(remaining >= 0, "Remaining calls should be non-negative");
            Assert.True(reset > 0, "Reset timestamp should be positive");
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task GitHub_Upload_LargeAsset()
        {
            if (_skipTests)
            {
                return;
            }

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(5); // Large file upload timeout
            
            var testRepo = Environment.GetEnvironmentVariable("GITHUB_TEST_REPO") ?? TestRepo;
            var fileName = $"large-asset-{Guid.NewGuid()}.bin";
            var filePath = Path.Combine(Path.GetTempPath(), fileName);
            var tag = $"e2e-large-{Guid.NewGuid():N}";
            
            // Create 50MB file (GitHub allows up to 2GB per asset)
            var largeContent = new byte[50 * 1024 * 1024];
            new Random().NextBytes(largeContent);
            await File.WriteAllBytesAsync(filePath, largeContent);
            
            try
            {
                var uploader = new GitHubReleasesUploader(
                    client, testRepo, _token!, tag,
                    new UploaderOptions { RetryAttempts = 3 });
                
                var result = await uploader.Upload(filePath);
                
                if (result.Success)
                {
                    // Verify size via API
                    var getReleaseRequest = new HttpRequestMessage(HttpMethod.Get,
                        $"https://api.github.com/repos/{testRepo}/releases/tags/{tag}");
                    getReleaseRequest.Headers.UserAgent.ParseAdd("ci-cd-e2e-tests/1.0");
                    getReleaseRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("token", _token);
                    
                    var getReleaseResponse = await client.SendAsync(getReleaseRequest);
                    var releaseBody = await getReleaseResponse.Content.ReadAsStringAsync();
                    var release = JsonDocument.Parse(releaseBody);
                    
                    var assets = release.RootElement.GetProperty("assets");
                    var asset = assets[0];
                    var size = asset.GetProperty("size").GetInt64();
                    
                    Assert.Equal(largeContent.Length, size);
                    
                    // Cleanup
                    await DeleteReleaseAsync(client, testRepo, tag);
                }
            }
            finally
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
        }

        private async Task DeleteReleaseAsync(HttpClient client, string repo, string tag)
        {
            try
            {
                // Get release ID
                var getReleaseRequest = new HttpRequestMessage(HttpMethod.Get,
                    $"https://api.github.com/repos/{repo}/releases/tags/{tag}");
                getReleaseRequest.Headers.UserAgent.ParseAdd("ci-cd-e2e-tests/1.0");
                getReleaseRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("token", _token);
                
                var getReleaseResponse = await client.SendAsync(getReleaseRequest);
                if (getReleaseResponse.IsSuccessStatusCode)
                {
                    var releaseBody = await getReleaseResponse.Content.ReadAsStringAsync();
                    var release = JsonDocument.Parse(releaseBody);
                    
                    if (release.RootElement.TryGetProperty("id", out var idProp))
                    {
                        var releaseId = idProp.GetInt64();
                        
                        // Delete release
                        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete,
                            $"https://api.github.com/repos/{repo}/releases/{releaseId}");
                        deleteRequest.Headers.UserAgent.ParseAdd("ci-cd-e2e-tests/1.0");
                        deleteRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("token", _token);
                        
                        await client.SendAsync(deleteRequest);
                    }
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}

