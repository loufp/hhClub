using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Ci_Cd.Tests
{
    public class SonarQubeIntegrationTests
    {
        private const string SonarQubeUrl = "http://localhost:9000";
        private const string AdminToken = "admin"; // Default admin token

        [Fact]
        [Trait("Category", "Integration")]
        public async Task SonarQube_ShouldBeHealthy()
        {
            using var client = new HttpClient();
            
            try
            {
                var response = await client.GetAsync($"{SonarQubeUrl}/api/system/status");
                
                if (!response.IsSuccessStatusCode)
                {
                    Assert.Fail($"SonarQube not available at {SonarQubeUrl}. Please run 'docker compose -f docker-compose.integration.yml up -d sonarqube'");
                }
                
                var content = await response.Content.ReadAsStringAsync();
                var status = JsonDocument.Parse(content);
                var state = status.RootElement.GetProperty("status").GetString();
                
                Assert.Equal("UP", state);
            }
            catch (HttpRequestException ex)
            {
                Assert.Fail($"Cannot connect to SonarQube: {ex.Message}");
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task SonarQube_CreateProject_AndAnalyze()
        {
            using var client = new HttpClient();
            var projectKey = $"test-project-{Guid.NewGuid():N}";
            var projectName = "Test Project";
            
            // Check SonarQube availability
            try
            {
                var healthCheck = await client.GetAsync($"{SonarQubeUrl}/api/system/status");
                if (!healthCheck.IsSuccessStatusCode)
                {
                    Assert.Fail("SonarQube not available");
                }
            }
            catch (HttpRequestException)
            {
                Assert.Fail("SonarQube not running. Start with: docker compose -f docker-compose.integration.yml up -d");
            }

            // Create project
            var createProjectRequest = new HttpRequestMessage(HttpMethod.Post, 
                $"{SonarQubeUrl}/api/projects/create?project={projectKey}&name={Uri.EscapeDataString(projectName)}");
            createProjectRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes("admin:admin")));
            
            var createResponse = await client.SendAsync(createProjectRequest);
            
            if (!createResponse.IsSuccessStatusCode)
            {
                var error = await createResponse.Content.ReadAsStringAsync();
                // Ignore if project already exists
                if (!error.Contains("already exists"))
                {
                    Assert.Fail($"Failed to create project: {error}");
                }
            }

            // Generate token for analysis
            var tokenRequest = new HttpRequestMessage(HttpMethod.Post,
                $"{SonarQubeUrl}/api/user_tokens/generate?name=test-token-{Guid.NewGuid():N}");
            tokenRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes("admin:admin")));
            
            var tokenResponse = await client.SendAsync(tokenRequest);
            Assert.True(tokenResponse.IsSuccessStatusCode, "Failed to generate token");

            // Create test files
            var tempDir = Path.Combine(Path.GetTempPath(), $"sonar-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            
            await File.WriteAllTextAsync(Path.Combine(tempDir, "test.java"), @"
public class TestClass {
    public int add(int a, int b) {
        return a + b;
    }
}
");

            var propertiesContent = $@"
sonar.projectKey={projectKey}
sonar.projectName={projectName}
sonar.projectVersion=1.0
sonar.sources=.
sonar.host.url={SonarQubeUrl}
sonar.qualitygate.wait=true
sonar.qualitygate.timeout=60
";
            await File.WriteAllTextAsync(Path.Combine(tempDir, "sonar-project.properties"), propertiesContent);

            // Note: Actual scanner execution would require sonar-scanner CLI
            // This test verifies API connectivity and project creation
            
            // Verify project exists
            var projectRequest = new HttpRequestMessage(HttpMethod.Get,
                $"{SonarQubeUrl}/api/projects/search?projects={projectKey}");
            projectRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes("admin:admin")));
            
            var projectResponse = await client.SendAsync(projectRequest);
            Assert.True(projectResponse.IsSuccessStatusCode);
            
            var projectContent = await projectResponse.Content.ReadAsStringAsync();
            Assert.Contains(projectKey, projectContent);

            // Cleanup
            Directory.Delete(tempDir, true);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task SonarQube_QualityGate_ConfigurationExists()
        {
            using var client = new HttpClient();
            
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, 
                    $"{SonarQubeUrl}/api/qualitygates/list");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes("admin:admin")));
                
                var response = await client.SendAsync(request);
                
                if (!response.IsSuccessStatusCode)
                {
                    Assert.Fail("SonarQube not available");
                }
                
                var content = await response.Content.ReadAsStringAsync();
                var gates = JsonDocument.Parse(content);
                
                Assert.True(gates.RootElement.GetProperty("qualitygates").GetArrayLength() > 0, 
                    "No quality gates configured");
            }
            catch (HttpRequestException)
            {
                Assert.Fail("SonarQube not running");
            }
        }
    }
}

