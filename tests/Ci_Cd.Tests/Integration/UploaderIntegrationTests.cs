using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Ci_Cd.Services;
using Ci_Cd.Tests.Integration;
using Xunit;

namespace Ci_Cd.Tests.Integration
{
    public class UploaderIntegrationTests
    {
        [Fact]
        public async Task NexusUpload_Integration()
        {
            var prefix = "http://localhost:5005/";
            using var server = new MockHttpServer(prefix);
            var tmp = Path.GetTempFileName();
            File.WriteAllText(tmp, "data");

            var client = new HttpClient();
            var uploader = new UploaderService(client);

            var res = await uploader.UploadToNexusAsync(tmp, "http://localhost:5005", "repo", "user", "pass");
            Assert.True(res.Success, res.Message);

            File.Delete(tmp);
        }
    }
}

