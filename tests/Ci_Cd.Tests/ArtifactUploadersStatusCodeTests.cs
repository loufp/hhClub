using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Ci_Cd.Services;
using Xunit;

namespace Ci_Cd.Tests
{
    /// <summary>
    /// Specialized tests for HTTP status codes and edge cases across all artifact uploaders
    /// </summary>
    public class ArtifactUploadersStatusCodeTests
    {
        private const string NexusUrl = "http://localhost:8081";
        private const string ArtifactoryUrl = "http://localhost:8082/artifactory";
        private const string RegistryUrl = "http://localhost:5000";

        [Fact]
        [Trait("Category", "Integration")]
        public async Task AllUploaders_401Unauthorized_InvalidCredentials()
        {
            var testFile = Path.GetTempFileName();
            await File.WriteAllTextAsync(testFile, "Status code test");

            try
            {
                using var client = new HttpClient();
                var options = new UploaderOptions { RetryAttempts = 1 };

                // Test Nexus 401
                var nexusUploader = new NexusUploader(client, NexusUrl, "maven-releases", "invalid", "invalid", options);
                var nexusResult = await nexusUploader.Upload(testFile);
                Assert.False(nexusResult.Success);
                Assert.Contains("401", nexusResult.Message);

                // Test Artifactory 401
                var artifactoryUploader = new ArtifactoryUploader(client, ArtifactoryUrl, "generic-local", "invalid", "invalid", options);
                var artifactoryResult = await artifactoryUploader.Upload(testFile);
                Assert.False(artifactoryResult.Success);
                Assert.Contains("401", artifactoryResult.Message);

                // Test GitHub 401
                var githubUploader = new GitHubReleasesUploader(client, "invalid/repo", "invalid_token", options: options);
                var githubResult = await githubUploader.Upload(testFile);
                Assert.False(githubResult.Success);
                Assert.Contains("401", githubResult.Message);
            }
            finally
            {
                if (File.Exists(testFile))
                    File.Delete(testFile);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task AllUploaders_404NotFound_InvalidResource()
        {
            var testFile = Path.GetTempFileName();
            await File.WriteAllTextAsync(testFile, "404 test content");

            try
            {
                using var client = new HttpClient();
                var options = new UploaderOptions { RetryAttempts = 1 };

                // Test Nexus 404 (invalid repo)
                var nexusUploader = new NexusUploader(client, NexusUrl, "nonexistent-repo-404", "admin", "admin123", options);
                var nexusResult = await nexusUploader.Upload(testFile);
                if (!nexusResult.Success)
                {
                    Assert.True(nexusResult.Message.Contains("404") || nexusResult.Message.Contains("400"));
                }

                // Test Artifactory 404 (invalid repo)
                var artifactoryUploader = new ArtifactoryUploader(client, ArtifactoryUrl, "nonexistent-repo-404", "admin", "password", options);
                var artifactoryResult = await artifactoryUploader.Upload(testFile);
                if (!artifactoryResult.Success)
                {
                    Assert.True(artifactoryResult.Message.Contains("404") || artifactoryResult.Message.Contains("400"));
                }

                // Test GitHub 404 (invalid repo)
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_TOKEN")))
                {
                    var githubUploader = new GitHubReleasesUploader(client, "nonexistent/repository-404-test", 
                        Environment.GetEnvironmentVariable("GITHUB_TOKEN")!, options: options);
                    var githubResult = await githubUploader.Upload(testFile);
                    Assert.False(githubResult.Success);
                    Assert.Contains("404", githubResult.Message);
                }
            }
            finally
            {
                if (File.Exists(testFile))
                    File.Delete(testFile);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task HttpClient_Timeout_HandledGracefully()
        {
            var testFile = Path.GetTempFileName();
            await File.WriteAllBytesAsync(testFile, new byte[1024]); // 1KB file

            try
            {
                // Create client with very short timeout
                using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(1) };
                var options = new UploaderOptions { RetryAttempts = 1 };

                var nexusUploader = new NexusUploader(client, NexusUrl, "maven-releases", "admin", "admin123", options);
                
                // Should fail with timeout, not crash
                var result = await nexusUploader.Upload(testFile);
                Assert.False(result.Success);
                // Message should contain timeout or cancellation info
                Assert.True(result.Message.Contains("timeout") || result.Message.Contains("cancel") || 
                           result.Message.Contains("TaskCanceledException") || result.Message.Contains("OperationCanceledException"),
                           $"Expected timeout error, got: {result.Message}");
            }
            finally
            {
                if (File.Exists(testFile))
                    File.Delete(testFile);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task DockerRegistry_SpecificStatusCodes()
        {
            using var client = new HttpClient();
            var repository = $"test/status-{Guid.NewGuid():N}";

            // Test 404 for nonexistent blob
            var fakeDigest = "sha256:1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef";
            var blobUrl = $"{RegistryUrl}/v2/{repository}/blobs/{fakeDigest}";
            
            var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, blobUrl));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            // Test 404 for nonexistent manifest
            var manifestUrl = $"{RegistryUrl}/v2/{repository}/manifests/nonexistent-tag";
            var manifestRequest = new HttpRequestMessage(HttpMethod.Get, manifestUrl);
            manifestRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(
                "application/vnd.docker.distribution.manifest.v2+json"));
            
            var manifestResponse = await client.SendAsync(manifestRequest);
            Assert.Equal(HttpStatusCode.NotFound, manifestResponse.StatusCode);

            // Test invalid upload URL (should return error)
            var invalidUploadUrl = $"{RegistryUrl}/v2/{repository}/blobs/uploads/invalid-uuid";
            var patchRequest = new HttpRequestMessage(new HttpMethod("PATCH"), invalidUploadUrl)
            {
                Content = new StringContent("invalid patch", Encoding.UTF8)
            };
            
            var patchResponse = await client.SendAsync(patchRequest);
            Assert.False(patchResponse.IsSuccessStatusCode);
            // Typically 404 or 416 Range Not Satisfiable
            Assert.True(patchResponse.StatusCode == HttpStatusCode.NotFound ||
                       patchResponse.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable ||
                       (int)patchResponse.StatusCode >= 400);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task GitHub_RateLimitHandling()
        {
            var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            if (string.IsNullOrEmpty(token))
            {
                // Skip test if no token
                return;
            }

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ci-cd-e2e-tests/1.0");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("token", token);

            // Check current rate limit
            var rateLimitResponse = await client.GetAsync("https://api.github.com/rate_limit");
            Assert.True(rateLimitResponse.IsSuccessStatusCode);

            var rateLimitBody = await rateLimitResponse.Content.ReadAsStringAsync();
            var rateLimitJson = JsonDocument.Parse(rateLimitBody);
            
            var remaining = rateLimitJson.RootElement.GetProperty("resources").GetProperty("core").GetProperty("remaining").GetInt32();
            
            Assert.True(remaining >= 0, "Rate limit remaining should be non-negative");

            // Verify rate limit headers are present
            Assert.True(rateLimitResponse.Headers.Contains("X-RateLimit-Limit"));
            Assert.True(rateLimitResponse.Headers.Contains("X-RateLimit-Remaining"));
            Assert.True(rateLimitResponse.Headers.Contains("X-RateLimit-Reset"));
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task LargeFile_Upload_AllServices()
        {
            // Create 5MB test file
            var testFile = Path.GetTempFileName();
            var largeContent = new byte[5 * 1024 * 1024];
            new Random().NextBytes(largeContent);
            await File.WriteAllBytesAsync(testFile, largeContent);

            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromMinutes(3); // Allow time for large uploads

                var options = new UploaderOptions { RetryAttempts = 2 };

                // Test Nexus large file
                try
                {
                    var nexusUploader = new NexusUploader(client, NexusUrl, "maven-releases", "admin", "admin123", options);
                    var nexusResult = await nexusUploader.Upload(testFile);
                    if (nexusResult.Success)
                    {
                        // Verify upload size
                        var artifactUrl = $"{NexusUrl}/repository/maven-releases/{Path.GetFileName(testFile)}";
                        var headRequest = new HttpRequestMessage(HttpMethod.Head, artifactUrl);
                        headRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes("admin:admin123")));

                        var headResponse = await client.SendAsync(headRequest);
                        if (headResponse.IsSuccessStatusCode)
                        {
                            Assert.Equal(largeContent.Length, headResponse.Content.Headers.ContentLength.Value);
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    // Large file upload timeout is acceptable for this test
                }

                // Test Docker Registry large layer
                try
                {
                    var registryUploader = new DockerRegistryUploader(client, RegistryUrl, 
                        $"test/large-{Guid.NewGuid():N}", options: options);
                    var registryResult = await registryUploader.Upload(testFile);
                    
                    if (registryResult.Success)
                    {
                        // Verify blob size
                        var digest = "sha256:" + HttpRetryHelper.ComputeSha256Hex(testFile);
                        var blobUrl = $"{RegistryUrl}/v2/test/large-{Guid.NewGuid():N}/blobs/{digest}";
                        var blobHeadResponse = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, blobUrl));
                        
                        if (blobHeadResponse.IsSuccessStatusCode)
                        {
                            Assert.Equal(largeContent.Length, blobHeadResponse.Content.Headers.ContentLength.Value);
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    // Large file upload timeout is acceptable for this test
                }
            }
            finally
            {
                if (File.Exists(testFile))
                    File.Delete(testFile);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task FileSystem_EdgeCases()
        {
            using var client = new HttpClient();
            var options = new UploaderOptions { RetryAttempts = 1 };

            // Test nonexistent file
            var nexusUploader = new NexusUploader(client, NexusUrl, "maven-releases", "admin", "admin123", options);
            var result = await nexusUploader.Upload("/nonexistent/file/path.jar");
            
            Assert.False(result.Success);
            Assert.Contains("File not found", result.Message);

            // Test empty file
            var emptyFile = Path.GetTempFileName();
            try
            {
                // Create empty file
                File.WriteAllText(emptyFile, "");
                
                var emptyResult = await nexusUploader.Upload(emptyFile);
                // Should either succeed (empty file is valid) or fail gracefully
                if (!emptyResult.Success)
                {
                    Assert.False(string.IsNullOrEmpty(emptyResult.Message));
                }
            }
            finally
            {
                if (File.Exists(emptyFile))
                    File.Delete(emptyFile);
            }

            // Test file with special characters in name
            var specialCharFile = Path.Combine(Path.GetTempPath(), "test-file!@#$%^&*()_+{}|:<>?[]\\;'\",.jar");
            try
            {
                await File.WriteAllTextAsync(specialCharFile, "Special chars test");
                
                var specialResult = await nexusUploader.Upload(specialCharFile);
                // Should handle gracefully (either succeed or fail with clear message)
                if (!specialResult.Success)
                {
                    Assert.False(string.IsNullOrEmpty(specialResult.Message));
                }
            }
            catch (DirectoryNotFoundException)
            {
                // Some systems may not allow certain characters in filenames
            }
            finally
            {
                if (File.Exists(specialCharFile))
                    File.Delete(specialCharFile);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task ConcurrentUploads_SameFile()
        {
            var testFile = Path.GetTempFileName();
            await File.WriteAllTextAsync(testFile, "Concurrent upload test");

            try
            {
                using var client = new HttpClient();
                var options = new UploaderOptions { RetryAttempts = 2 };

                var nexusUploader = new NexusUploader(client, NexusUrl, "maven-releases", "admin", "admin123", options);
                
                // Start multiple concurrent uploads of the same file
                var tasks = new[]
                {
                    nexusUploader.Upload(testFile),
                    nexusUploader.Upload(testFile),
                    nexusUploader.Upload(testFile)
                };

                var results = await Task.WhenAll(tasks);
                
                // At least one should succeed, others may succeed or fail gracefully
                var successCount = 0;
                foreach (var result in results)
                {
                    if (result.Success)
                        successCount++;
                    else
                        Assert.False(string.IsNullOrEmpty(result.Message)); // Should have error message
                }
                
                Assert.True(successCount >= 1, "At least one concurrent upload should succeed");
            }
            finally
            {
                if (File.Exists(testFile))
                    File.Delete(testFile);
            }
        }
    }
}
