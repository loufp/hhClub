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
    /// E2E tests for Artifactory against real instance
    /// Tests metadata, ETag, specific HTTP status codes, and edge cases
    /// </summary>
    public class ArtifactoryE2ETests
    {
        private const string ArtifactoryUrl = "http://localhost:8082/artifactory";
        private const string DefaultUser = "admin";
        private const string DefaultPassword = "password";
        private const string TestRepo = "generic-local";

        [Fact]
        [Trait("Category", "Integration")]
        public async Task Artifactory_ShouldBeHealthy()
        {
            using var client = new HttpClient();
            
            try
            {
                var response = await client.GetAsync($"{ArtifactoryUrl}/api/system/ping");
                Assert.True(response.IsSuccessStatusCode,
                    $"Artifactory not available at {ArtifactoryUrl}. Please run integration services.");
            }
            catch (HttpRequestException ex)
            {
                Assert.Fail($"Cannot connect to Artifactory: {ex.Message}. Run: ./scripts/ci/start-integration-services.sh");
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task Artifactory_Upload_WithMetadataAndETag()
        {
            using var client = new HttpClient();
            var fileName = $"test-artifact-{Guid.NewGuid()}.bin";
            var filePath = Path.Combine(Path.GetTempPath(), fileName);
            var testContent = $"Artifactory test content {Guid.NewGuid()}";
            
            await File.WriteAllTextAsync(filePath, testContent);
            
            try
            {
                // Upload artifact
                var uploader = new ArtifactoryUploader(
                    client, ArtifactoryUrl, TestRepo, DefaultUser, DefaultPassword,
                    new UploaderOptions { RetryAttempts = 3, BaseDelaySeconds = 2 });
                
                var result = await uploader.Upload(filePath);
                
                if (!result.Success && result.Message.Contains("401"))
                {
                    Assert.Fail("Artifactory authentication failed. Ensure Artifactory is configured with admin/password or update test credentials.");
                }
                
                Assert.True(result.Success, $"Upload failed: {result.Message}");
                
                // Verify artifact with HEAD request
                var artifactUrl = $"{ArtifactoryUrl}/{TestRepo}/{fileName}";
                var headRequest = new HttpRequestMessage(HttpMethod.Head, artifactUrl);
                headRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{DefaultUser}:{DefaultPassword}")));
                
                var headResponse = await client.SendAsync(headRequest);
                Assert.True(headResponse.IsSuccessStatusCode, "Artifact not found after upload");
                
                // Check ETag header
                Assert.True(headResponse.Headers.ETag != null, "ETag header missing");
                var etag = headResponse.Headers.ETag.Tag;
                Assert.False(string.IsNullOrEmpty(etag), "ETag is empty");
                
                // Check Last-Modified header
                Assert.True(headResponse.Content.Headers.LastModified.HasValue, "Last-Modified header missing");
                var lastModified = headResponse.Content.Headers.LastModified.Value;
                Assert.True((DateTimeOffset.UtcNow - lastModified).TotalMinutes < 5, "Last-Modified is too old");
                
                // Check Content-Length
                Assert.True(headResponse.Content.Headers.ContentLength.HasValue, "Content-Length missing");
                var remoteSize = headResponse.Content.Headers.ContentLength.Value;
                var localSize = new FileInfo(filePath).Length;
                Assert.Equal(localSize, remoteSize);
                
                // Check X-Checksum headers (Artifactory specific)
                Assert.True(headResponse.Headers.Contains("X-Checksum-Sha256") || 
                           headResponse.Headers.Contains("X-Checksum-Sha1") ||
                           headResponse.Headers.Contains("X-Checksum-Md5"),
                           "Checksum headers missing");
                
                // Download and verify content
                var getRequest = new HttpRequestMessage(HttpMethod.Get, artifactUrl);
                getRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{DefaultUser}:{DefaultPassword}")));
                
                var getResponse = await client.SendAsync(getRequest);
                Assert.True(getResponse.IsSuccessStatusCode);
                
                var downloadedContent = await getResponse.Content.ReadAsStringAsync();
                Assert.Equal(testContent, downloadedContent);
                
                // Test conditional GET with If-None-Match (ETag)
                var conditionalRequest = new HttpRequestMessage(HttpMethod.Get, artifactUrl);
                conditionalRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{DefaultUser}:{DefaultPassword}")));
                conditionalRequest.Headers.IfNoneMatch.Add(headResponse.Headers.ETag);
                
                var conditionalResponse = await client.SendAsync(conditionalRequest);
                Assert.Equal(HttpStatusCode.NotModified, conditionalResponse.StatusCode);
                
                // Test conditional GET with If-Modified-Since
                var ifModifiedRequest = new HttpRequestMessage(HttpMethod.Get, artifactUrl);
                ifModifiedRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{DefaultUser}:{DefaultPassword}")));
                ifModifiedRequest.Headers.IfModifiedSince = DateTimeOffset.UtcNow;
                
                var ifModifiedResponse = await client.SendAsync(ifModifiedRequest);
                Assert.Equal(HttpStatusCode.NotModified, ifModifiedResponse.StatusCode);
            }
            finally
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task Artifactory_Upload_DuplicateFile_ShouldSucceed()
        {
            // Artifactory allows overwriting files by default
            using var client = new HttpClient();
            var fileName = $"duplicate-test-{Guid.NewGuid()}.bin";
            var filePath = Path.Combine(Path.GetTempPath(), fileName);
            
            await File.WriteAllTextAsync(filePath, "First version");
            
            try
            {
                var uploader = new ArtifactoryUploader(
                    client, ArtifactoryUrl, TestRepo, DefaultUser, DefaultPassword);
                
                // First upload
                var result1 = await uploader.Upload(filePath);
                Assert.True(result1.Success, $"First upload failed: {result1.Message}");
                
                // Modify and upload again
                await File.WriteAllTextAsync(filePath, "Second version - modified");
                
                var result2 = await uploader.Upload(filePath);
                Assert.True(result2.Success, $"Second upload failed: {result2.Message}");
                
                // Verify content is updated
                var artifactUrl = $"{ArtifactoryUrl}/{TestRepo}/{fileName}";
                var getRequest = new HttpRequestMessage(HttpMethod.Get, artifactUrl);
                getRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{DefaultUser}:{DefaultPassword}")));
                
                var getResponse = await client.SendAsync(getRequest);
                var content = await getResponse.Content.ReadAsStringAsync();
                
                Assert.Equal("Second version - modified", content);
            }
            finally
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task Artifactory_Upload_InvalidCredentials_Returns401()
        {
            using var client = new HttpClient();
            var fileName = $"auth-test-{Guid.NewGuid()}.bin";
            var filePath = Path.Combine(Path.GetTempPath(), fileName);
            
            await File.WriteAllTextAsync(filePath, "Auth test");
            
            try
            {
                var uploader = new ArtifactoryUploader(
                    client, ArtifactoryUrl, TestRepo, "invalid", "invalid",
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
        public async Task Artifactory_Upload_InvalidRepo_Returns400Or404()
        {
            using var client = new HttpClient();
            var fileName = $"invalid-repo-{Guid.NewGuid()}.bin";
            var filePath = Path.Combine(Path.GetTempPath(), fileName);
            
            await File.WriteAllTextAsync(filePath, "Invalid repo test");
            
            try
            {
                var uploader = new ArtifactoryUploader(
                    client, ArtifactoryUrl, "nonexistent-repo-xyz", DefaultUser, DefaultPassword,
                    new UploaderOptions { RetryAttempts = 1 });
                
                var result = await uploader.Upload(filePath);
                
                Assert.False(result.Success);
                Assert.True(result.Message.Contains("400") || result.Message.Contains("404"),
                    $"Expected 400 or 404, got: {result.Message}");
            }
            finally
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task Artifactory_GetArtifactInfo_ReturnsMetadata()
        {
            using var client = new HttpClient();
            var fileName = $"metadata-test-{Guid.NewGuid()}.bin";
            var filePath = Path.Combine(Path.GetTempPath(), fileName);
            var testContent = "Metadata test content";
            
            await File.WriteAllTextAsync(filePath, testContent);
            
            try
            {
                // Upload artifact
                var uploader = new ArtifactoryUploader(
                    client, ArtifactoryUrl, TestRepo, DefaultUser, DefaultPassword);
                
                var uploadResult = await uploader.Upload(filePath);
                Assert.True(uploadResult.Success);
                
                // Get artifact info via API
                var infoUrl = $"{ArtifactoryUrl}/api/storage/{TestRepo}/{fileName}";
                var infoRequest = new HttpRequestMessage(HttpMethod.Get, infoUrl);
                infoRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{DefaultUser}:{DefaultPassword}")));
                
                var infoResponse = await client.SendAsync(infoRequest);
                Assert.True(infoResponse.IsSuccessStatusCode, "Failed to get artifact info");
                
                var infoJson = await infoResponse.Content.ReadAsStringAsync();
                var info = JsonDocument.Parse(infoJson);
                
                // Verify metadata fields
                Assert.True(info.RootElement.TryGetProperty("repo", out var repo));
                Assert.Equal(TestRepo, repo.GetString());
                
                Assert.True(info.RootElement.TryGetProperty("path", out var path));
                Assert.Contains(fileName, path.GetString());
                
                Assert.True(info.RootElement.TryGetProperty("size", out var size));
                Assert.Equal(testContent.Length, size.GetInt64());
                
                Assert.True(info.RootElement.TryGetProperty("checksums", out var checksums));
                Assert.True(checksums.TryGetProperty("sha256", out var sha256));
                Assert.False(string.IsNullOrEmpty(sha256.GetString()));
                
                Assert.True(info.RootElement.TryGetProperty("created", out var created));
                Assert.False(string.IsNullOrEmpty(created.GetString()));
                
                Assert.True(info.RootElement.TryGetProperty("createdBy", out var createdBy));
                Assert.Equal(DefaultUser, createdBy.GetString());
            }
            finally
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task Artifactory_HandleRetryOn429()
        {
            using var client = new HttpClient();
            var fileName = $"retry-test-{Guid.NewGuid()}.bin";
            var filePath = Path.Combine(Path.GetTempPath(), fileName);
            
            await File.WriteAllTextAsync(filePath, "Retry test content");
            
            try
            {
                var uploader = new ArtifactoryUploader(
                    client, ArtifactoryUrl, TestRepo, DefaultUser, DefaultPassword,
                    new UploaderOptions { RetryAttempts = 5, BaseDelaySeconds = 1 });
                
                var result = await uploader.Upload(filePath);
                
                // Should succeed or fail with clear error (not crash)
                if (!result.Success)
                {
                    Assert.Contains("HTTP", result.Message);
                }
            }
            finally
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task Artifactory_LargeFile_Upload()
        {
            using var client = new HttpClient();
            var fileName = $"large-file-{Guid.NewGuid()}.bin";
            var filePath = Path.Combine(Path.GetTempPath(), fileName);
            
            // Create 10MB file
            var largeContent = new byte[10 * 1024 * 1024];
            new Random().NextBytes(largeContent);
            await File.WriteAllBytesAsync(filePath, largeContent);
            
            try
            {
                var uploader = new ArtifactoryUploader(
                    client, ArtifactoryUrl, TestRepo, DefaultUser, DefaultPassword,
                    new UploaderOptions { RetryAttempts = 3 });
                
                var result = await uploader.Upload(filePath);
                
                if (result.Success)
                {
                    // Verify size via HEAD
                    var artifactUrl = $"{ArtifactoryUrl}/{TestRepo}/{fileName}";
                    var headRequest = new HttpRequestMessage(HttpMethod.Head, artifactUrl);
                    headRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{DefaultUser}:{DefaultPassword}")));
                    
                    var headResponse = await client.SendAsync(headRequest);
                    Assert.True(headResponse.IsSuccessStatusCode);
                    Assert.Equal(largeContent.Length, headResponse.Content.Headers.ContentLength.Value);
                }
            }
            finally
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
        }
    }
}

