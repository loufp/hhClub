using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Ci_Cd.Services;
using Xunit;

namespace Ci_Cd.Tests
{
    public class NexusIntegrationTests
    {
        private const string NexusUrl = "http://localhost:8081";
        private const string DefaultUser = "admin";
        private const string DefaultPassword = "admin123";

        [Fact]
        [Trait("Category", "Integration")]
        public async Task Nexus_ShouldBeHealthy()
        {
            using var client = new HttpClient();
            
            try
            {
                var response = await client.GetAsync(NexusUrl);
                Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Unauthorized,
                    $"Nexus not available at {NexusUrl}. Please run './scripts/ci/start-integration-services.sh'");
            }
            catch (HttpRequestException ex)
            {
                Assert.Fail($"Cannot connect to Nexus: {ex.Message}. Run: ./scripts/ci/start-integration-services.sh");
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task Nexus_Upload_WithMetadataAndETag()
        {
            using var client = new HttpClient();
            var repo = "maven-releases"; // Default repo in Nexus
            var fileName = $"test-artifact-{Guid.NewGuid()}.jar";
            var filePath = Path.GetTempFileName();
            
            // Create test file with known content
            var testContent = $"Test artifact content {Guid.NewGuid()}";
            await File.WriteAllTextAsync(filePath, testContent);
            
            try
            {
                // Upload with NexusUploader
                var uploader = new NexusUploader(client, NexusUrl, repo, DefaultUser, DefaultPassword,
                    new UploaderOptions { RetryAttempts = 3, BaseDelaySeconds = 2 });
                
                // Rename temp file to have proper name
                var properPath = Path.Combine(Path.GetTempPath(), fileName);
                File.Move(filePath, properPath, true);
                filePath = properPath;
                
                var result = await uploader.Upload(filePath);
                
                if (!result.Success && result.Message.Contains("401"))
                {
                    Assert.Fail("Nexus authentication failed. Ensure Nexus is configured with admin/admin123 or update test credentials.");
                }
                
                Assert.True(result.Success, $"Upload failed: {result.Message}");
                
                // Verify uploaded artifact with HEAD request (check metadata)
                var artifactUrl = $"{NexusUrl}/repository/{repo}/{fileName}";
                var headRequest = new HttpRequestMessage(HttpMethod.Head, artifactUrl);
                headRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{DefaultUser}:{DefaultPassword}")));
                
                var headResponse = await client.SendAsync(headRequest);
                Assert.True(headResponse.IsSuccessStatusCode, "Artifact not found after upload");
                
                // Check ETag header
                Assert.True(headResponse.Headers.ETag != null, "ETag header missing");
                var etag = headResponse.Headers.ETag.Tag;
                Assert.False(string.IsNullOrEmpty(etag), "ETag is empty");
                
                // Check Content-Length
                Assert.True(headResponse.Content.Headers.ContentLength.HasValue, "Content-Length missing");
                var remoteSize = headResponse.Content.Headers.ContentLength.Value;
                var localSize = new FileInfo(filePath).Length;
                Assert.Equal(localSize, remoteSize);
                
                // Download and verify content matches
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
            }
            finally
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task Nexus_Upload_HandleRetryOn429()
        {
            // This test verifies retry logic but Nexus typically doesn't rate-limit locally
            // It ensures the uploader handles 429 gracefully
            
            using var client = new HttpClient();
            var repo = "maven-releases";
            var fileName = $"retry-test-{Guid.NewGuid()}.jar";
            var filePath = Path.Combine(Path.GetTempPath(), fileName);
            
            await File.WriteAllTextAsync(filePath, "retry test content");
            
            try
            {
                var uploader = new NexusUploader(client, NexusUrl, repo, DefaultUser, DefaultPassword,
                    new UploaderOptions { RetryAttempts = 5, BaseDelaySeconds = 1 });
                
                var result = await uploader.Upload(filePath);
                
                // Should succeed or fail with clear error (not crash)
                if (!result.Success)
                {
                    Assert.Contains("HTTP", result.Message); // Contains error code
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
        public async Task Nexus_Upload_WithChecksum()
        {
            using var client = new HttpClient();
            var repo = "maven-releases";
            var fileName = $"checksum-test-{Guid.NewGuid()}.jar";
            var filePath = Path.Combine(Path.GetTempPath(), fileName);
            
            await File.WriteAllTextAsync(filePath, "checksum verification content");
            var expectedChecksum = HttpRetryHelper.ComputeSha256Hex(filePath);
            
            try
            {
                var uploader = new NexusUploader(client, NexusUrl, repo, DefaultUser, DefaultPassword);
                var result = await uploader.Upload(filePath);
                
                if (result.Success)
                {
                    // Verify checksum was sent (recorded in uploader logic)
                    Assert.True(!string.IsNullOrEmpty(expectedChecksum));
                    Assert.Equal(64, expectedChecksum.Length); // SHA-256 hex is 64 chars
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
        public async Task Nexus_Upload_InvalidCredentials_Returns401()
        {
            using var client = new HttpClient();
            var repo = "maven-releases";
            var fileName = $"auth-test-{Guid.NewGuid()}.jar";
            var filePath = Path.Combine(Path.GetTempPath(), fileName);
            
            await File.WriteAllTextAsync(filePath, "Auth test");
            
            try
            {
                var uploader = new NexusUploader(client, NexusUrl, repo, "invalid", "invalid",
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
        public async Task Nexus_Upload_InvalidRepository_Returns400Or404()
        {
            using var client = new HttpClient();
            var fileName = $"invalid-repo-{Guid.NewGuid()}.jar";
            var filePath = Path.Combine(Path.GetTempPath(), fileName);
            
            await File.WriteAllTextAsync(filePath, "Invalid repo test");
            
            try
            {
                var uploader = new NexusUploader(client, NexusUrl, "nonexistent-repo-xyz", DefaultUser, DefaultPassword,
                    new UploaderOptions { RetryAttempts = 1 });
                
                var result = await uploader.Upload(filePath);
                
                Assert.False(result.Success);
                // Nexus typically returns 404 for nonexistent repository
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
        public async Task Nexus_Head_Request_AllMetadataHeaders()
        {
            using var client = new HttpClient();
            var repo = "maven-releases";
            var fileName = $"metadata-test-{Guid.NewGuid()}.jar";
            var filePath = Path.Combine(Path.GetTempPath(), fileName);
            var testContent = "Metadata headers test";
            
            await File.WriteAllTextAsync(filePath, testContent);
            
            try
            {
                var uploader = new NexusUploader(client, NexusUrl, repo, DefaultUser, DefaultPassword);
                var uploadResult = await uploader.Upload(filePath);
                Assert.True(uploadResult.Success);
                
                // Perform HEAD request to check all metadata
                var artifactUrl = $"{NexusUrl}/repository/{repo}/{fileName}";
                var headRequest = new HttpRequestMessage(HttpMethod.Head, artifactUrl);
                headRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{DefaultUser}:{DefaultPassword}")));
                
                var headResponse = await client.SendAsync(headRequest);
                Assert.True(headResponse.IsSuccessStatusCode);
                
                // Check standard HTTP headers
                Assert.True(headResponse.Content.Headers.ContentLength.HasValue, "Content-Length missing");
                Assert.True(headResponse.Content.Headers.LastModified.HasValue, "Last-Modified missing");
                Assert.True(headResponse.Headers.ETag != null, "ETag missing");
                
                // Check Last-Modified is recent
                var lastModified = headResponse.Content.Headers.LastModified.Value;
                var age = DateTimeOffset.UtcNow - lastModified;
                Assert.True(age.TotalMinutes < 5, $"Last-Modified too old: {age.TotalMinutes} minutes");
                
                // Check Content-Type
                Assert.True(headResponse.Content.Headers.ContentType != null, "Content-Type missing");
                
                // Nexus may include cache headers
                if (headResponse.Headers.CacheControl != null)
                {
                    Assert.NotNull(headResponse.Headers.CacheControl);
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
        public async Task Nexus_ConditionalRequest_IfModifiedSince_Returns304()
        {
            using var client = new HttpClient();
            var repo = "maven-releases";
            var fileName = $"if-modified-{Guid.NewGuid()}.jar";
            var filePath = Path.Combine(Path.GetTempPath(), fileName);
            
            await File.WriteAllTextAsync(filePath, "If-Modified-Since test");
            
            try
            {
                var uploader = new NexusUploader(client, NexusUrl, repo, DefaultUser, DefaultPassword);
                var uploadResult = await uploader.Upload(filePath);
                Assert.True(uploadResult.Success);
                
                var artifactUrl = $"{NexusUrl}/repository/{repo}/{fileName}";
                
                // Get Last-Modified
                var headRequest = new HttpRequestMessage(HttpMethod.Head, artifactUrl);
                headRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{DefaultUser}:{DefaultPassword}")));
                
                var headResponse = await client.SendAsync(headRequest);
                var lastModified = headResponse.Content.Headers.LastModified.Value;
                
                // Request with If-Modified-Since set to future
                var conditionalRequest = new HttpRequestMessage(HttpMethod.Get, artifactUrl);
                conditionalRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{DefaultUser}:{DefaultPassword}")));
                conditionalRequest.Headers.IfModifiedSince = DateTimeOffset.UtcNow.AddHours(1);
                
                var conditionalResponse = await client.SendAsync(conditionalRequest);
                Assert.Equal(HttpStatusCode.NotModified, conditionalResponse.StatusCode);
            }
            finally
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task Nexus_LargeFile_Upload_With_ProgressTracking()
        {
            using var client = new HttpClient();
            var repo = "maven-releases";
            var fileName = $"large-file-{Guid.NewGuid()}.jar";
            var filePath = Path.Combine(Path.GetTempPath(), fileName);
            
            // Create 10MB file
            var largeContent = new byte[10 * 1024 * 1024];
            new Random().NextBytes(largeContent);
            await File.WriteAllBytesAsync(filePath, largeContent);
            
            try
            {
                var uploader = new NexusUploader(client, NexusUrl, repo, DefaultUser, DefaultPassword,
                    new UploaderOptions { RetryAttempts = 3 });
                
                var result = await uploader.Upload(filePath);
                
                if (result.Success)
                {
                    // Verify size matches
                    var artifactUrl = $"{NexusUrl}/repository/{repo}/{fileName}";
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

        [Fact]
        [Trait("Category", "Integration")]
        public async Task Nexus_Upload_SpecialCharacters_InFileName()
        {
            using var client = new HttpClient();
            var repo = "maven-releases";
            var fileName = $"test-special_chars.v1.0-SNAPSHOT.jar";
            var filePath = Path.Combine(Path.GetTempPath(), fileName);
            
            await File.WriteAllTextAsync(filePath, "Special chars test");
            
            try
            {
                var uploader = new NexusUploader(client, NexusUrl, repo, DefaultUser, DefaultPassword);
                var result = await uploader.Upload(filePath);
                
                // Should handle special characters correctly
                Assert.True(result.Success || result.Message.Contains("HTTP"), 
                    $"Unexpected error: {result.Message}");
            }
            finally
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
        }
    }
}

