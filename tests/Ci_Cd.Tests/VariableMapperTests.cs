using Ci_Cd.Services;
using Xunit;

namespace Ci_Cd.Tests
{
    public class VariableMapperTests
    {
        [Fact]
        public void MapToGitLab_ShouldReplaceVariablesCorrectly()
        {
            var mapper = new VariableMapper();
            var template = "Image: {{CI_REGISTRY}}/{{CI_PROJECT_PATH}}:{{CI_COMMIT_SHORT_SHA}}";
            
            var result = mapper.MapToGitLab(template);
            
            Assert.Equal("Image: $CI_REGISTRY/$CI_PROJECT_PATH:$CI_COMMIT_SHORT_SHA", result);
        }
        
        [Fact]
        public void MapToJenkins_ShouldReplaceVariablesCorrectly()
        {
            var mapper = new VariableMapper();
            var template = "Image: {{CI_REGISTRY}}/{{JOB_NAME}}:{{BUILD_NUMBER}}";
            
            var result = mapper.MapToJenkins(template);
            
            Assert.Contains("${env.JOB_NAME}", result);
            Assert.Contains("${env.BUILD_NUMBER}", result);
            Assert.Contains("registry.example.com", result);
        }
        
        [Fact]
        public void MapToGitLab_ShouldHandleMultipleOccurrences()
        {
            var mapper = new VariableMapper();
            var template = "{{CI_PROJECT_PATH}} and {{CI_PROJECT_PATH}} again";
            
            var result = mapper.MapToGitLab(template);
            
            Assert.Equal("$CI_PROJECT_PATH and $CI_PROJECT_PATH again", result);
        }
        
        [Fact]
        public void MapToJenkins_ShouldMapBranchName()
        {
            var mapper = new VariableMapper();
            var template = "Branch: {{BRANCH_NAME}}";
            
            var result = mapper.MapToJenkins(template);
            
            Assert.Equal("Branch: ${env.BRANCH_NAME}", result);
        }
        
        [Fact]
        public void MapToGitLab_ShouldMapBuildNumber()
        {
            var mapper = new VariableMapper();
            var template = "Build: {{BUILD_NUMBER}}";
            
            var result = mapper.MapToGitLab(template);
            
            Assert.Equal("Build: $CI_PIPELINE_IID", result);
        }
    }
}

