using System.Text;
using Ci_Cd.Models;

namespace Ci_Cd.Services
{
    public interface ISonarQubeService
    {
        string GenerateSonarProperties(RepoAnalysisResult analysis);
        string GenerateQualityGateScript(string projectKey);
    }

    public class SonarQubeService : ISonarQubeService
    {
        public string GenerateSonarProperties(RepoAnalysisResult analysis)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"sonar.projectKey={analysis.ProjectName}");
            sb.AppendLine($"sonar.projectName={analysis.ProjectName}");
            sb.AppendLine($"sonar.projectVersion={analysis.ProjectVersion}");
            sb.AppendLine();
            
            // Language specific settings
            var lang = analysis.Language.ToString().ToLowerInvariant();
            switch (lang)
            {
                case "java":
                case "kotlin":
                    sb.AppendLine("sonar.sources=src/main/java,src/main/kotlin");
                    sb.AppendLine("sonar.tests=src/test/java,src/test/kotlin");
                    sb.AppendLine("sonar.java.binaries=build/classes,target/classes");
                    sb.AppendLine("sonar.java.libraries=build/libs,target/lib");
                    sb.AppendLine("sonar.junit.reportPaths=build/test-results,target/surefire-reports");
                    sb.AppendLine("sonar.coverage.jacoco.xmlReportPaths=build/reports/jacoco/test/jacocoTestReport.xml,target/site/jacoco/jacoco.xml");
                    break;
                    
                case "go":
                    sb.AppendLine("sonar.sources=.");
                    sb.AppendLine("sonar.tests=.");
                    sb.AppendLine("sonar.test.inclusions=**/*_test.go");
                    sb.AppendLine("sonar.exclusions=**/*_test.go,vendor/**");
                    sb.AppendLine("sonar.go.coverage.reportPaths=coverage.out");
                    sb.AppendLine("sonar.go.tests.reportPaths=test-report.json");
                    break;
                    
                case "nodejs":
                case "javascript":
                case "typescript":
                    sb.AppendLine("sonar.sources=src");
                    sb.AppendLine("sonar.tests=src,test");
                    sb.AppendLine("sonar.test.inclusions=**/*.test.ts,**/*.test.js,**/*.spec.ts,**/*.spec.js");
                    sb.AppendLine("sonar.exclusions=**/node_modules/**,**/dist/**,**/build/**,**/*.test.ts,**/*.test.js");
                    sb.AppendLine("sonar.javascript.lcov.reportPaths=coverage/lcov.info");
                    sb.AppendLine("sonar.testExecutionReportPaths=test-report.xml");
                    if (lang == "typescript")
                    {
                        sb.AppendLine("sonar.typescript.tsconfigPath=tsconfig.json");
                    }
                    break;
                    
                case "python":
                    sb.AppendLine("sonar.sources=.");
                    sb.AppendLine("sonar.tests=tests");
                    sb.AppendLine("sonar.exclusions=**/tests/**,**/__pycache__/**,**/venv/**,**/.venv/**");
                    sb.AppendLine("sonar.python.coverage.reportPaths=coverage.xml");
                    sb.AppendLine("sonar.python.version=3.11");
                    break;
                    
                default:
                    sb.AppendLine("sonar.sources=.");
                    break;
            }
            
            // Quality gate settings
            sb.AppendLine();
            sb.AppendLine("# Quality Gate");
            sb.AppendLine("sonar.qualitygate.wait=true");
            sb.AppendLine("sonar.qualitygate.timeout=300");
            
            // Additional rules
            sb.AppendLine();
            sb.AppendLine("# Code Analysis");
            sb.AppendLine("sonar.sourceEncoding=UTF-8");
            sb.AppendLine("sonar.scm.provider=git");
            
            return sb.ToString();
        }

        public string GenerateQualityGateScript(string projectKey)
        {
            return $@"
# Wait for quality gate status
TASK_URL=$(cat .scannerwork/report-task.txt | grep ceTaskUrl | cut -d'=' -f2-)
echo ""Waiting for analysis to complete: $TASK_URL""

# Poll for task completion
MAX_ATTEMPTS=30
ATTEMPT=0
while [ $ATTEMPT -lt $MAX_ATTEMPTS ]; do
  sleep 10
  TASK_STATUS=$(curl -s -u ${{SONAR_TOKEN}}: ""$TASK_URL"" | jq -r '.task.status')
  echo ""Task status: $TASK_STATUS (attempt $((ATTEMPT+1))/$MAX_ATTEMPTS)""
  
  if [ ""$TASK_STATUS"" = ""SUCCESS"" ]; then
    break
  elif [ ""$TASK_STATUS"" = ""FAILED"" ] || [ ""$TASK_STATUS"" = ""CANCELED"" ]; then
    echo ""Analysis task failed: $TASK_STATUS""
    exit 1
  fi
  
  ATTEMPT=$((ATTEMPT+1))
done

if [ $ATTEMPT -ge $MAX_ATTEMPTS ]; then
  echo ""Timeout waiting for analysis completion""
  exit 1
fi

# Get quality gate status
ANALYSIS_ID=$(curl -s -u ${{SONAR_TOKEN}}: ""$TASK_URL"" | jq -r '.task.analysisId')
QG_STATUS=$(curl -s -u ${{SONAR_TOKEN}}: ""${{SONAR_HOST_URL}}/api/qualitygates/project_status?analysisId=$ANALYSIS_ID"" | jq -r '.projectStatus.status')

echo ""Quality Gate Status: $QG_STATUS""

if [ ""$QG_STATUS"" != ""OK"" ]; then
  echo ""Quality gate failed!""
  curl -s -u ${{SONAR_TOKEN}}: ""${{SONAR_HOST_URL}}/api/qualitygates/project_status?analysisId=$ANALYSIS_ID"" | jq '.projectStatus.conditions'
  exit 1
fi

echo ""Quality gate passed!""
";
        }
    }
}

