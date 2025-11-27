using System.Collections.Generic;
using System.Linq;

namespace Ci_Cd.Services
{
    public interface IVariableMapper
    {
        string MapToGitLab(string template);
        string MapToJenkins(string template);
    }

    public class VariableMapper : IVariableMapper
    {
        private readonly Dictionary<string, string> _gitlabVariables = new()
        {
            { "{{CI_COMMIT_REF_NAME}}", "$CI_COMMIT_REF_NAME" },
            { "{{CI_COMMIT_SHORT_SHA}}", "$CI_COMMIT_SHORT_SHA" },
            { "{{CI_COMMIT_SHA}}", "$CI_COMMIT_SHA" },
            { "{{CI_PROJECT_NAME}}", "$CI_PROJECT_NAME" },
            { "{{CI_PROJECT_PATH}}", "$CI_PROJECT_PATH" },
            { "{{CI_PIPELINE_ID}}", "$CI_PIPELINE_ID" },
            { "{{CI_JOB_ID}}", "$CI_JOB_ID" },
            { "{{CI_REGISTRY}}", "$CI_REGISTRY" },
            { "{{CI_REGISTRY_USER}}", "$CI_REGISTRY_USER" },
            { "{{CI_REGISTRY_PASSWORD}}", "$CI_REGISTRY_PASSWORD" },
            { "{{BUILD_NUMBER}}", "$CI_PIPELINE_IID" },
            { "{{JOB_NAME}}", "$CI_PROJECT_PATH" },
            { "{{BRANCH_NAME}}", "$CI_COMMIT_REF_NAME" },
            { "{{WORKSPACE}}", "$CI_PROJECT_DIR" }
        };

        private readonly Dictionary<string, string> _jenkinsVariables = new()
        {
            { "{{CI_COMMIT_REF_NAME}}", "${env.BRANCH_NAME}" },
            { "{{CI_COMMIT_SHORT_SHA}}", "${env.GIT_COMMIT.take(8)}" },
            { "{{CI_COMMIT_SHA}}", "${env.GIT_COMMIT}" },
            { "{{CI_PROJECT_NAME}}", "${env.JOB_BASE_NAME}" },
            { "{{CI_PROJECT_PATH}}", "${env.JOB_NAME}" },
            { "{{CI_PIPELINE_ID}}", "${env.BUILD_ID}" },
            { "{{CI_JOB_ID}}", "${env.BUILD_ID}" },
            { "{{CI_REGISTRY}}", "registry.example.com" },
            { "{{CI_REGISTRY_USER}}", "${REGISTRY_CREDENTIALS_USR}" },
            { "{{CI_REGISTRY_PASSWORD}}", "${REGISTRY_CREDENTIALS_PSW}" },
            { "{{BUILD_NUMBER}}", "${env.BUILD_NUMBER}" },
            { "{{JOB_NAME}}", "${env.JOB_NAME}" },
            { "{{BRANCH_NAME}}", "${env.BRANCH_NAME}" },
            { "{{WORKSPACE}}", "${env.WORKSPACE}" }
        };

        public string MapToGitLab(string template)
        {
            var result = template;
            foreach (var kvp in _gitlabVariables.OrderByDescending(x => x.Key.Length))
            {
                result = result.Replace(kvp.Key, kvp.Value);
            }
            return result;
        }

        public string MapToJenkins(string template)
        {
            var result = template;
            foreach (var kvp in _jenkinsVariables.OrderByDescending(x => x.Key.Length))
            {
                result = result.Replace(kvp.Key, kvp.Value);
            }
            return result;
        }
    }
}

