using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Ci_Cd.Services;
using Xunit;

namespace Ci_Cd.Tests
{
    public class DockerRegistryE2ETests
    {
        private const string RegistryUrl = "http://localhost:5000";

        [Fact]
        [Trait("Category", "Integration")]
        public async Task Registry_ShouldBeHealthy()
        {
            using var client = new HttpClient();
            
            try
            {
                var response = await client.GetAsync($"{RegistryUrl}/v2/");
                Assert.True(response.IsSuccessStatusCode,
                    $"Registry not available. Run: ./scripts/ci/start-integration-services.sh");
            }
            catch (HttpRequestException ex)
            {
                Assert.Fail($"Cannot connect to Registry: {ex.Message}");
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task Registry_FullPushFlow_WithManifestVerification()
        {
            using var client = new HttpClient();
            var repository = $"test/app-{Guid.NewGuid():N}";
            var tag = "e2e-test";
            var layerFile = Path.GetTempFileName();
            
            // Create test layer content
            var layerContent = $"Layer data {Guid.NewGuid()}";
            await File.WriteAllTextAsync(layerFile, layerContent);
            
            try
            {
                // Perform full push
                var uploader = new DockerRegistryUploader(client, RegistryUrl, repository, token: null, tag: tag);
                var result = await uploader.Upload(layerFile);
                
                Assert.True(result.Success, $"Push failed: {result.Message}");
                
                // Verify manifest exists
                var manifestUrl = $"{RegistryUrl}/v2/{repository}/manifests/{tag}";
                var manifestRequest = new HttpRequestMessage(HttpMethod.Get, manifestUrl);
                manifestRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(
                    "application/vnd.docker.distribution.manifest.v2+json"));
                
                var manifestResponse = await client.SendAsync(manifestRequest);
                Assert.True(manifestResponse.IsSuccessStatusCode, "Manifest not found");
                
                // Verify manifest has Docker-Content-Digest header
                Assert.True(manifestResponse.Headers.Contains("Docker-Content-Digest"), 
                    "Docker-Content-Digest header missing");
                
                var manifestBody = await manifestResponse.Content.ReadAsStringAsync();
                var manifest = JsonDocument.Parse(manifestBody);
                
                // Verify manifest structure
                Assert.Equal(2, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
                Assert.Equal("application/vnd.docker.distribution.manifest.v2+json",
                    manifest.RootElement.GetProperty("mediaType").GetString());
                
                // Verify config exists
                var config = manifest.RootElement.GetProperty("config");
                Assert.True(config.TryGetProperty("digest", out var configDigest));
                Assert.StartsWith("sha256:", configDigest.GetString());
                Assert.True(config.TryGetProperty("size", out var configSize));
                Assert.True(configSize.GetInt64() > 0);
                
                // Verify layers
                var layers = manifest.RootElement.GetProperty("layers");
                Assert.Equal(1, layers.GetArrayLength());
                
                var layer = layers[0];
                Assert.True(layer.TryGetProperty("digest", out var layerDigest));
                Assert.StartsWith("sha256:", layerDigest.GetString());
                
                var expectedDigest = "sha256:" + HttpRetryHelper.ComputeSha256Hex(layerFile);
                Assert.Equal(expectedDigest, layerDigest.GetString());
                
                // Verify tags list
                var tagsUrl = $"{RegistryUrl}/v2/{repository}/tags/list";
                var tagsResponse = await client.GetAsync(tagsUrl);
                Assert.True(tagsResponse.IsSuccessStatusCode);
                
                var tagsBody = await tagsResponse.Content.ReadAsStringAsync();
                Assert.Contains(tag, tagsBody);
                
                // Verify blob exists (layer)
                var blobUrl = $"{RegistryUrl}/v2/{repository}/blobs/{layerDigest.GetString()}";
                var blobHeadRequest = new HttpRequestMessage(HttpMethod.Head, blobUrl);
                var blobResponse = await client.SendAsync(blobHeadRequest);
                Assert.True(blobResponse.IsSuccessStatusCode, "Layer blob not found");
                
                // Verify config blob exists
                var configBlobUrl = $"{RegistryUrl}/v2/{repository}/blobs/{configDigest.GetString()}";
                var configBlobRequest = new HttpRequestMessage(HttpMethod.Head, configBlobUrl);
                var configBlobResponse = await client.SendAsync(configBlobRequest);
                Assert.True(configBlobResponse.IsSuccessStatusCode, "Config blob not found");
            }
            finally
            {
                if (File.Exists(layerFile))
                    File.Delete(layerFile);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task Registry_ConditionalRequest_WithETag()
        {
            using var client = new HttpClient();
            var repository = $"test/etag-{Guid.NewGuid():N}";
            var tag = "latest";
            var layerFile = Path.GetTempFileName();
            
            await File.WriteAllTextAsync(layerFile, "ETag test data");
            
            try
            {
                // Push image
                var uploader = new DockerRegistryUploader(client, RegistryUrl, repository, tag: tag);
                var pushResult = await uploader.Upload(layerFile);
                Assert.True(pushResult.Success);
                
                // Get manifest with ETag
                var manifestUrl = $"{RegistryUrl}/v2/{repository}/manifests/{tag}";
                var firstRequest = new HttpRequestMessage(HttpMethod.Get, manifestUrl);
                firstRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(
                    "application/vnd.docker.distribution.manifest.v2+json"));
                
                var firstResponse = await client.SendAsync(firstRequest);
                Assert.True(firstResponse.IsSuccessStatusCode);
                
                var etag = firstResponse.Headers.ETag;
                Assert.NotNull(etag);
                
                // Conditional request with If-None-Match
                var conditionalRequest = new HttpRequestMessage(HttpMethod.Get, manifestUrl);
                conditionalRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(
                    "application/vnd.docker.distribution.manifest.v2+json"));
                conditionalRequest.Headers.IfNoneMatch.Add(etag);
                
                var conditionalResponse = await client.SendAsync(conditionalRequest);
                Assert.Equal(System.Net.HttpStatusCode.NotModified, conditionalResponse.StatusCode);
            }
            finally
            {
                if (File.Exists(layerFile))
                    File.Delete(layerFile);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task Registry_MultipleTagsForSameImage()
        {
            using var client = new HttpClient();
            var repository = $"test/multitag-{Guid.NewGuid():N}";
            var layerFile = Path.GetTempFileName();
            
            await File.WriteAllTextAsync(layerFile, "Multi-tag test");
            
            try
            {
                // Push with tag "v1"
                var uploader1 = new DockerRegistryUploader(client, RegistryUrl, repository, tag: "v1");
                var result1 = await uploader1.Upload(layerFile);
                Assert.True(result1.Success);
                
                // Push same content with tag "v2"
                var uploader2 = new DockerRegistryUploader(client, RegistryUrl, repository, tag: "v2");
                var result2 = await uploader2.Upload(layerFile);
                Assert.True(result2.Success);
                
                // Verify both tags exist
                var tagsUrl = $"{RegistryUrl}/v2/{repository}/tags/list";
                var tagsResponse = await client.GetAsync(tagsUrl);
                var tagsBody = await tagsResponse.Content.ReadAsStringAsync();
                
                Assert.Contains("v1", tagsBody);
                Assert.Contains("v2", tagsBody);
                
                // Verify both point to same digest
                var getDigest = async (string tag) =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Head, 
                        $"{RegistryUrl}/v2/{repository}/manifests/{tag}");
                    req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(
                        "application/vnd.docker.distribution.manifest.v2+json"));
                    var resp = await client.SendAsync(req);
                    return resp.Headers.GetValues("Docker-Content-Digest").FirstOrDefault();
                };
                
                var digest1 = await getDigest("v1");
                var digest2 = await getDigest("v2");
                
                Assert.Equal(digest1, digest2); // Same content = same digest
            }
            finally
            {
                if (File.Exists(layerFile))
                    File.Delete(layerFile);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task Registry_DeleteManifest()
        {
            using var client = new HttpClient();
            var repository = $"test/delete-{Guid.NewGuid():N}";
            var tag = "deleteme";
            var layerFile = Path.GetTempFileName();
            
            await File.WriteAllTextAsync(layerFile, "Delete test");
            
            try
            {
                // Push image
                var uploader = new DockerRegistryUploader(client, RegistryUrl, repository, tag: tag);
                var pushResult = await uploader.Upload(layerFile);
                Assert.True(pushResult.Success);
                
                // Get digest
                var manifestUrl = $"{RegistryUrl}/v2/{repository}/manifests/{tag}";
                var headRequest = new HttpRequestMessage(HttpMethod.Head, manifestUrl);
                headRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(
                    "application/vnd.docker.distribution.manifest.v2+json"));
                
                var headResponse = await client.SendAsync(headRequest);
                var digest = headResponse.Headers.GetValues("Docker-Content-Digest").First();
                
                // Delete by digest (not tag, as per Docker Registry API)
                var deleteUrl = $"{RegistryUrl}/v2/{repository}/manifests/{digest}";
                var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, deleteUrl);
                var deleteResponse = await client.SendAsync(deleteRequest);
                
                // Should be 202 Accepted or 404 if delete is disabled
                Assert.True(deleteResponse.StatusCode == System.Net.HttpStatusCode.Accepted ||
                           deleteResponse.StatusCode == System.Net.HttpStatusCode.NotFound ||
                           deleteResponse.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed);
            }
            finally
            {
                if (File.Exists(layerFile))
                    File.Delete(layerFile);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task Registry_BlobExists_Returns200()
        {
            using var client = new HttpClient();
            var repository = $"test/blob-check-{Guid.NewGuid():N}";
            var layerFile = Path.GetTempFileName();
            
            await File.WriteAllTextAsync(layerFile, "Blob exists test");
            var expectedDigest = "sha256:" + HttpRetryHelper.ComputeSha256Hex(layerFile);
            
            try
            {
                // Upload image
                var uploader = new DockerRegistryUploader(client, RegistryUrl, repository);
                var result = await uploader.Upload(layerFile);
                Assert.True(result.Success);
                
                // Check if blob exists
                var blobUrl = $"{RegistryUrl}/v2/{repository}/blobs/{expectedDigest}";
                var headRequest = new HttpRequestMessage(HttpMethod.Head, blobUrl);
                
                var headResponse = await client.SendAsync(headRequest);
                Assert.Equal(HttpStatusCode.OK, headResponse.StatusCode);
                
                // Verify Content-Length
                Assert.True(headResponse.Content.Headers.ContentLength.HasValue);
                Assert.Equal(new FileInfo(layerFile).Length, headResponse.Content.Headers.ContentLength.Value);
            }
            finally
            {
                if (File.Exists(layerFile))
                    File.Delete(layerFile);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task Registry_BlobNotExists_Returns404()
        {
            using var client = new HttpClient();
            var repository = $"test/blob-404-{Guid.NewGuid():N}";
            var fakeDigest = "sha256:0000000000000000000000000000000000000000000000000000000000000000";
            
            var blobUrl = $"{RegistryUrl}/v2/{repository}/blobs/{fakeDigest}";
            var headRequest = new HttpRequestMessage(HttpMethod.Head, blobUrl);
            
            var headResponse = await client.SendAsync(headRequest);
            Assert.Equal(HttpStatusCode.NotFound, headResponse.StatusCode);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task Registry_ManifestNotFound_Returns404()
        {
            using var client = new HttpClient();
            var repository = $"test/manifest-404-{Guid.NewGuid():N}";
            var tag = "nonexistent";
            
            var manifestUrl = $"{RegistryUrl}/v2/{repository}/manifests/{tag}";
            var request = new HttpRequestMessage(HttpMethod.Get, manifestUrl);
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(
                "application/vnd.docker.distribution.manifest.v2+json"));
            
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task Registry_InvalidManifestMediaType_Returns400Or415()
        {
            using var client = new HttpClient();
            var repository = $"test/invalid-manifest-{Guid.NewGuid():N}";
            var tag = "test";
            var layerFile = Path.GetTempFileName();
            
            await File.WriteAllTextAsync(layerFile, "Invalid manifest test");
            
            try
            {
                // First upload valid image
                var uploader = new DockerRegistryUploader(client, RegistryUrl, repository, tag: tag);
                var result = await uploader.Upload(layerFile);
                Assert.True(result.Success);
                
                // Try to upload invalid manifest with wrong media type
                var manifestUrl = $"{RegistryUrl}/v2/{repository}/manifests/{tag}-invalid";
                var invalidManifest = "{ \"invalid\": \"manifest\" }";
                var request = new HttpRequestMessage(HttpMethod.Put, manifestUrl)
                {
                    Content = new StringContent(invalidManifest, Encoding.UTF8, "application/json") // Wrong media type
                };
                
                var response = await client.SendAsync(request);
                // Registry should reject with 400 Bad Request or 415 Unsupported Media Type
                Assert.True(response.StatusCode == HttpStatusCode.BadRequest ||
                           response.StatusCode == HttpStatusCode.UnsupportedMediaType ||
                           !response.IsSuccessStatusCode);
            }
            finally
            {
                if (File.Exists(layerFile))
                    File.Delete(layerFile);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task Registry_CatalogAPI_ListsRepositories()
        {
            using var client = new HttpClient();
            var repository = $"test/catalog-{Guid.NewGuid():N}";
            var layerFile = Path.GetTempFileName();
            
            await File.WriteAllTextAsync(layerFile, "Catalog test");
            
            try
            {
                // Upload image
                var uploader = new DockerRegistryUploader(client, RegistryUrl, repository);
                var result = await uploader.Upload(layerFile);
                Assert.True(result.Success);
                
                // Query catalog
                var catalogUrl = $"{RegistryUrl}/v2/_catalog";
                var response = await client.GetAsync(catalogUrl);
                Assert.True(response.IsSuccessStatusCode);
                
                var body = await response.Content.ReadAsStringAsync();
                var catalog = JsonDocument.Parse(body);
                
                Assert.True(catalog.RootElement.TryGetProperty("repositories", out var repos));
                var repoList = repos.EnumerateArray().Select(r => r.GetString()).ToList();
                
                Assert.Contains(repository, repoList);
            }
            finally
            {
                if (File.Exists(layerFile))
                    File.Delete(layerFile);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task Registry_RangeRequest_PartialContent()
        {
            using var client = new HttpClient();
            var repository = $"test/range-{Guid.NewGuid():N}";
            var layerFile = Path.GetTempFileName();
            var content = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            
            await File.WriteAllTextAsync(layerFile, content);
            
            try
            {
                // Upload image
                var uploader = new DockerRegistryUploader(client, RegistryUrl, repository);
                var result = await uploader.Upload(layerFile);
                Assert.True(result.Success);
                
                var digest = "sha256:" + HttpRetryHelper.ComputeSha256Hex(layerFile);
                var blobUrl = $"{RegistryUrl}/v2/{repository}/blobs/{digest}";
                
                // Request bytes 0-9 (first 10 bytes)
                var rangeRequest = new HttpRequestMessage(HttpMethod.Get, blobUrl);
                rangeRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 9);
                
                var rangeResponse = await client.SendAsync(rangeRequest);
                
                // Registry may support partial content (206) or return full content (200)
                Assert.True(rangeResponse.StatusCode == HttpStatusCode.PartialContent ||
                           rangeResponse.StatusCode == HttpStatusCode.OK);
                
                if (rangeResponse.StatusCode == HttpStatusCode.PartialContent)
                {
                    var partialContent = await rangeResponse.Content.ReadAsStringAsync();
                    Assert.Equal("0123456789", partialContent);
                    Assert.Equal(10, rangeResponse.Content.Headers.ContentLength.Value);
                }
            }
            finally
            {
                if (File.Exists(layerFile))
                    File.Delete(layerFile);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task Registry_CacheHeaders_Validation()
        {
            using var client = new HttpClient();
            var repository = $"test/cache-{Guid.NewGuid():N}";
            var layerFile = Path.GetTempFileName();
            
            await File.WriteAllTextAsync(layerFile, "Cache headers test");
            
            try
            {
                // Upload image
                var uploader = new DockerRegistryUploader(client, RegistryUrl, repository);
                var result = await uploader.Upload(layerFile);
                Assert.True(result.Success);
                
                var manifestUrl = $"{RegistryUrl}/v2/{repository}/manifests/latest";
                var request = new HttpRequestMessage(HttpMethod.Get, manifestUrl);
                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(
                    "application/vnd.docker.distribution.manifest.v2+json"));
                
                var response = await client.SendAsync(request);
                Assert.True(response.IsSuccessStatusCode);
                
                // Check for Docker-specific headers
                Assert.True(response.Headers.Contains("Docker-Content-Digest"), 
                    "Docker-Content-Digest header missing");
                
                // Check ETag for caching
                Assert.True(response.Headers.ETag != null, "ETag missing for manifest");
                
                // Check Docker-Distribution-API-Version
                if (response.Headers.Contains("Docker-Distribution-API-Version"))
                {
                    var version = response.Headers.GetValues("Docker-Distribution-API-Version").First();
                    Assert.Equal("registry/2.0", version);
                }
            }
            finally
            {
                if (File.Exists(layerFile))
                    File.Delete(layerFile);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task Registry_LargeLayer_Upload()
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(5);
            
            var repository = $"test/large-layer-{Guid.NewGuid():N}";
            var layerFile = Path.GetTempFileName();
            
            // Create 100MB layer
            var largeContent = new byte[100 * 1024 * 1024];
            new Random().NextBytes(largeContent);
            await File.WriteAllBytesAsync(layerFile, largeContent);
            
            try
            {
                var uploader = new DockerRegistryUploader(client, RegistryUrl, repository,
                    options: new UploaderOptions { RetryAttempts = 3 });
                
                var result = await uploader.Upload(layerFile);
                
                if (result.Success)
                {
                    // Verify blob size
                    var digest = "sha256:" + HttpRetryHelper.ComputeSha256Hex(layerFile);
                    var blobUrl = $"{RegistryUrl}/v2/{repository}/blobs/{digest}";
                    var headRequest = new HttpRequestMessage(HttpMethod.Head, blobUrl);
                    
                    var headResponse = await client.SendAsync(headRequest);
                    Assert.True(headResponse.IsSuccessStatusCode);
                    Assert.Equal(largeContent.Length, headResponse.Content.Headers.ContentLength.Value);
                }
            }
            finally
            {
                if (File.Exists(layerFile))
                    File.Delete(layerFile);
            }
        }
    }
}

