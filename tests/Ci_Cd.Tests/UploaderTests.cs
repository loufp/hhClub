using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Ci_Cd.Services;
using Xunit;

class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) { _responder = responder; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_responder(request));
    }
}

namespace Ci_Cd.Tests
{
    public class UploaderTests
    {
        [Fact]
        public async Task UploadToGitHubRelease_CreatesAndUploads()
        {
            // fake create release response
            var createJson = "{ \"upload_url\": \"https://uploads.github.com/repos/owner/repo/releases/1/assets{?name,label}\" }";
            var uploadRespJson = "{ \"state\": \"uploaded\" }";

            var handler = new FakeHandler(req =>
            {
                if (req.Method == HttpMethod.Post && req.RequestUri.AbsolutePath.Contains("/releases"))
                    return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(createJson) };
                if (req.Method == HttpMethod.Get)
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
                if (req.Method == HttpMethod.Post && req.RequestUri.AbsoluteUri.Contains("uploads.github.com"))
                    return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(uploadRespJson) };
                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("bad") };
            });

            var client = new HttpClient(handler) { BaseAddress = new System.Uri("https://api.github.com/") };
            var uploader = new UploaderService(client);

            var tmp = System.IO.Path.GetTempFileName();
            System.IO.File.WriteAllText(tmp, "data");

            var res = await uploader.UploadToGitHubReleaseAsync(tmp, "owner/repo", "token", "v1");
            Assert.True(res.Success, res.Message);

            System.IO.File.Delete(tmp);
        }

        [Fact]
        public async Task UploadToArtifactory_PutsFile()
        {
            var handler = new FakeHandler(req =>
            {
                if (req.Method == HttpMethod.Put) return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("ok") };
                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("bad") };
            });

            var client = new HttpClient(handler);
            var uploader = new UploaderService(client);
            var tmp = System.IO.Path.GetTempFileName();
            System.IO.File.WriteAllText(tmp, "data");

            var res = await uploader.UploadToArtifactoryAsync(tmp, "https://art.example.com", "repo", "user", "pass");
            Assert.True(res.Success, res.Message);

            System.IO.File.Delete(tmp);
        }

        [Fact]
        public async Task UploadToNexus_PutsFile()
        {
            var handler = new FakeHandler(req =>
            {
                if (req.Method == HttpMethod.Put) return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("ok") };
                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("bad") };
            });

            var client = new HttpClient(handler);
            var uploader = new UploaderService(client);
            var tmp = System.IO.Path.GetTempFileName();
            System.IO.File.WriteAllText(tmp, "data");

            var res = await uploader.UploadToNexusAsync(tmp, "https://nexus.example.com", "repo", "user", "pass");
            Assert.True(res.Success, res.Message);

            System.IO.File.Delete(tmp);
        }

        [Fact]
        public async Task Upload_Retry_OnTransientError()
        {
            int call = 0;
            var handler = new FakeHandler(req =>
            {
                call++;
                if (call == 1) return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("err") };
                return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("ok") };
            });

            var client = new HttpClient(handler);
            var uploader = new UploaderService(client);
            var tmp = System.IO.Path.GetTempFileName();
            System.IO.File.WriteAllText(tmp, "data");

            var res = await uploader.UploadToNexusAsync(tmp, "https://nexus.example.com", "repo", "user", "pass");
            Assert.True(res.Success, res.Message);
            System.IO.File.Delete(tmp);
        }

        [Fact]
        public async Task Upload_SendsChecksumHeader()
        {
            HttpRequestMessage? captured = null;
            var handler = new FakeHandler(req =>
            {
                captured = req;
                return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("ok") };
            });

            var client = new HttpClient(handler);
            var uploader = new UploaderService(client);
            var tmp = System.IO.Path.GetTempFileName();
            System.IO.File.WriteAllText(tmp, "data-to-checksum");

            var res = await uploader.UploadToNexusAsync(tmp, "https://nexus.example.com", "repo", "user", "pass");
            Assert.True(res.Success, res.Message);
            Assert.NotNull(captured);
            Assert.True(captured.Headers.Contains("X-Checksum-Sha256"));

            var val = captured.Headers.GetValues("X-Checksum-Sha256").FirstOrDefault();
            Assert.False(string.IsNullOrEmpty(val));

            // compute local sha256
            using var fs = System.IO.File.OpenRead(tmp);
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hash = sha.ComputeHash(fs);
            var expected = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            Assert.Equal(expected, val);

            System.IO.File.Delete(tmp);
        }
    }
}
