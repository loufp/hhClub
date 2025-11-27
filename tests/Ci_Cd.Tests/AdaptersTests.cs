using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Ci_Cd.Services;
using Xunit;

class FakeHandler2 : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    public FakeHandler2(Func<HttpRequestMessage, HttpResponseMessage> responder) { _responder = responder; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_responder(request));
    }
}

namespace Ci_Cd.Tests
{
    public class AdaptersTests
    {
        [Fact]
        public async Task NexusUploader_SendsChecksumAndAuth()
        {
            HttpRequestMessage? captured = null;
            var handler = new FakeHandler2(req => { captured = req; return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("ok") }; });
            var client = new HttpClient(handler);
            var tmp = System.IO.Path.GetTempFileName();
            System.IO.File.WriteAllText(tmp, "payload-data");

            var uploader = new NexusUploader(client, "https://nexus.example.com", "repo", "user", "pass");
            var res = await uploader.Upload(tmp);
            Assert.True(res.Success, res.Message);
            Assert.NotNull(captured);
            Assert.True(captured.Headers.Authorization != null);
            Assert.Equal("Basic", captured.Headers.Authorization.Scheme);
            Assert.True(captured.Headers.Contains("X-Checksum-Sha256"));
            var val = captured.Headers.GetValues("X-Checksum-Sha256").FirstOrDefault();
            using var fs = System.IO.File.OpenRead(tmp);
            using var sha = System.Security.Cryptography.SHA256.Create();
            var expected = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-","",StringComparison.Ordinal).ToLowerInvariant();
            Assert.Equal(expected, val);

            System.IO.File.Delete(tmp);
        }

        [Fact]
        public async Task ArtifactoryUploader_RetriesOnTransientError()
        {
            int calls = 0;
            var handler = new FakeHandler2(req => { calls++; if (calls == 1) return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("err") }; return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("ok") }; });
            var client = new HttpClient(handler);
            var tmp = System.IO.Path.GetTempFileName();
            System.IO.File.WriteAllText(tmp, "payload");

            var uploader = new ArtifactoryUploader(client, "https://art.example.com", "repo", "u","p");
            var res = await uploader.Upload(tmp);
            Assert.True(res.Success);
            Assert.True(calls >= 2, "Expected at least one retry on transient error");

            System.IO.File.Delete(tmp);
        }

        [Fact]
        public async Task GitHubReleasesUploader_CreatesReleaseAndUploads()
        {
            var createJson = "{ \"upload_url\": \"https://uploads.github.com/repos/owner/repo/releases/1/assets{?name,label}\" }";
            var handler = new FakeHandler2(req =>
            {
                if (req.Method == HttpMethod.Post && req.RequestUri.AbsolutePath.Contains("/releases"))
                    return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(createJson) };
                if (req.Method == HttpMethod.Post && req.RequestUri.Host.Contains("uploads.github.com"))
                    return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("uploaded") };
                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("bad") };
            });

            var client = new HttpClient(handler) { BaseAddress = new System.Uri("https://api.github.com/") };
            var tmp = System.IO.Path.GetTempFileName();
            System.IO.File.WriteAllText(tmp, "filedata");

            var uploader = new GitHubReleasesUploader(client, "owner/repo", "token123", "v1");
            var res = await uploader.Upload(tmp);
            Assert.True(res.Success, res.Message);

            System.IO.File.Delete(tmp);
        }

        [Fact]
        public async Task DockerRegistryUploader_Flow_Mock()
        {
            int step = 0;
            var handler = new FakeHandler2(req =>
            {
                // init
                if (req.Method == HttpMethod.Post && req.RequestUri.AbsolutePath.EndsWith("/blobs/uploads/"))
                {
                    step = 1;
                    var resp = new HttpResponseMessage(HttpStatusCode.Accepted);
                    resp.Headers.Location = new System.Uri("/v2/ns/repo/blobs/uploads/uuid1");
                    return resp;
                }
                // patch
                if (req.Method.Method == "PATCH" && req.RequestUri.AbsolutePath.Contains("/blobs/uploads/uuid1"))
                {
                    step = 2;
                    var resp = new HttpResponseMessage(HttpStatusCode.Accepted);
                    resp.Headers.Location = new System.Uri("/v2/ns/repo/blobs/uploads/uuid1");
                    return resp;
                }
                // commit
                if (req.Method == HttpMethod.Put && req.RequestUri.AbsolutePath.Contains("/blobs/uploads/uuid1"))
                {
                    step = 3;
                    return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("committed") };
                }
                // manifest
                if (req.Method == HttpMethod.Put && req.RequestUri.AbsolutePath.Contains("/manifests/"))
                {
                    step = 4;
                    return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("manifest") };
                }
                // tags/list
                if (req.Method == HttpMethod.Get && req.RequestUri.AbsolutePath.Contains("/tags/list"))
                {
                    step = 5;
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"tags\":[\"latest\"]}") };
                }

                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("bad") };
            });

            var client = new HttpClient(handler) { BaseAddress = new System.Uri("https://registry.test") };
            var tmp = System.IO.Path.GetTempFileName();
            System.IO.File.WriteAllText(tmp, "layerdata");
            var uploader = new DockerRegistryUploader(client, "https://registry.test", "ns/repo", token: "tkn", tag: "latest");
            var res = await uploader.Upload(tmp);
            Assert.True(res.Success, res.Message);
            Assert.True(step >= 5, "Flow should reach tag check");
            System.IO.File.Delete(tmp);
        }
    }
}
