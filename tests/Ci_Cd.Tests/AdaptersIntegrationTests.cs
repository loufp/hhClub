using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Ci_Cd.Services;
using Xunit;
using System.IO;
using System;

namespace Ci_Cd.Tests
{
    public class AdaptersIntegrationTests
    {
        [Fact]
        public async Task NexusUploader_Integration_HeadVerificationAndRetryAfter()
        {
            var builder = new WebHostBuilder().Configure(app =>
            {
                int putCalls = 0;
                app.Run(async ctx =>
                {
                    var path = ctx.Request.Path.Value ?? "";
                    if (ctx.Request.Method == HttpMethod.Put.Method)
                    {
                        putCalls++;
                        if (putCalls == 1)
                        {
                            ctx.Response.StatusCode = 500; // transient
                            await ctx.Response.WriteAsync("err");
                            return;
                        }

                        // accept and return created
                        ctx.Response.StatusCode = 201;
                        await ctx.Response.WriteAsync("ok");
                        return;
                    }

                    if (ctx.Request.Method == HttpMethod.Head.Method)
                    {
                        // return content-length equal to content-length of previous upload, for test just 11
                        ctx.Response.ContentLength = 11;
                        ctx.Response.StatusCode = 200;
                        return;
                    }

                    ctx.Response.StatusCode = 404;
                });
            });

            using var server = new TestServer(builder);
            var client = server.CreateClient();
            var tmp = System.IO.Path.GetTempFileName();
            System.IO.File.WriteAllText(tmp, "hello world");

            var uploader = new NexusUploader(client, server.BaseAddress.ToString().TrimEnd('/'), "repo", "u", "p", new UploaderOptions { RetryAttempts = 3, BaseDelaySeconds = 1 }, null);
            var res = await uploader.Upload(tmp);
            Assert.True(res.Success, res.Message);

            System.IO.File.Delete(tmp);
        }

        [Fact]
        public async Task GitHubUploader_Integration_UploadFlow()
        {
            var builder = new WebHostBuilder().Configure(app =>
            {
                app.Run(async ctx =>
                {
                    var path = ctx.Request.Path.Value ?? "";
                    if (ctx.Request.Method == HttpMethod.Post.Method && path.Contains("/releases"))
                    {
                        var json = "{ \"upload_url\": \"https://uploads.github.test/repos/owner/repo/releases/1/assets{?name,label}\" }";
                        ctx.Response.StatusCode = 201;
                        await ctx.Response.WriteAsync(json);
                        return;
                    }
                    if (ctx.Request.Method == HttpMethod.Post.Method && ctx.Request.Host.Host.Contains("uploads.github.test"))
                    {
                        ctx.Response.StatusCode = 201;
                        await ctx.Response.WriteAsync("uploaded");
                        return;
                    }
                    ctx.Response.StatusCode = 404;
                });
            });

            using var server = new TestServer(builder);
            var client = server.CreateClient();
            client.BaseAddress = server.BaseAddress; // set base
            var tmp = System.IO.Path.GetTempFileName();
            System.IO.File.WriteAllText(tmp, "filedata");

            var uploader = new GitHubReleasesUploader(client, "owner/repo", "token", "v1", new UploaderOptions { RetryAttempts = 2 }, null);
            var res = await uploader.Upload(tmp);
            Assert.True(res.Success, res.Message);

            System.IO.File.Delete(tmp);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task DockerRegistryUploader_RealIntegration_PushToLocalRegistry()
        {
            // This test requires the local registry from docker-compose.integration.yml to be running
            var registryUrl = "http://localhost:5000";
            var repository = "test/my-image";
            var tag = "integration-test";
            var client = new HttpClient();

            // 1. Check if registry is available
            try
            {
                var healthResp = await client.GetAsync($"{registryUrl}/v2/");
                if (!healthResp.IsSuccessStatusCode)
                {
                    Assert.Fail($"Local registry at {registryUrl} is not available. Please run 'docker compose -f docker-compose.integration.yml up -d'. Status: {healthResp.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Assert.Fail($"Failed to connect to local registry at {registryUrl}. Please run 'docker compose -f docker-compose.integration.yml up -d'. Error: {ex.Message}");
            }

            // 2. Create a dummy layer file
            var tmpFile = Path.GetTempFileName();
            await File.WriteAllTextAsync(tmpFile, $"dummy layer content {Guid.NewGuid()}");

            // 3. Perform the upload
            var uploader = new DockerRegistryUploader(client, registryUrl, repository, tag: tag);
            var result = await uploader.Upload(tmpFile);

            // 4. Assert upload success
            Assert.True(result.Success, $"Upload failed: {result.Message}");

            // 5. Verify by pulling the manifest
            var manifestUrl = $"{registryUrl}/v2/{repository}/manifests/{tag}";
            var verifyReq = new HttpRequestMessage(HttpMethod.Get, manifestUrl);
            verifyReq.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.docker.distribution.manifest.v2+json"));
            var verifyResp = await client.SendAsync(verifyReq);
            var manifestBody = await verifyResp.Content.ReadAsStringAsync();

            Assert.True(verifyResp.IsSuccessStatusCode, $"Failed to pull manifest: {manifestBody}");
            Assert.Contains("application/vnd.docker.distribution.manifest.v2+json", manifestBody, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(HttpRetryHelper.ComputeSha256Hex(tmpFile), manifestBody, StringComparison.OrdinalIgnoreCase);

            // 6. Clean up
            File.Delete(tmpFile);
        }
    }
}
