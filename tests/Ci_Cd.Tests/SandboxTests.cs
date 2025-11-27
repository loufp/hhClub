using Ci_Cd.Services;
using Xunit;

namespace Ci_Cd.Tests
{
    public class SandboxTests
    {
        [Fact]
        public void Rejects_Localhost_Url()
        {
            var s = new SandboxService();
            Assert.False(s.ValidateRepositoryUrl("http://localhost:8080/repo.git", out var reason));
            Assert.False(string.IsNullOrEmpty(reason));
        }

        [Fact]
        public void Rejects_PrivateIp()
        {
            var s = new SandboxService();
            Assert.False(s.ValidateRepositoryUrl("https://192.168.0.1/repo.git", out var reason));
            Assert.False(string.IsNullOrEmpty(reason));
        }

        [Fact]
        public void Accepts_Public_Host()
        {
            var s = new SandboxService();
            Assert.True(s.ValidateRepositoryUrl("https://github.com/owner/repo.git", out var reason));
            Assert.Null(reason);
        }
    }
}

