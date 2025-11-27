# E2E Tests for Artifact Uploaders

## Overview

Comprehensive end-to-end tests for artifact uploaders against real service instances, covering metadata validation, ETag handling, specific HTTP status codes, and edge cases.

## Test Coverage

### Nexus Repository Manager
**File**: `tests/Ci_Cd.Tests/NexusIntegrationTests.cs`

#### Tests Implemented:
1. **Health Check** (`Nexus_ShouldBeHealthy`)
   - Verifies Nexus is accessible
   - Checks for proper HTTP response

2. **Upload with Metadata and ETag** (`Nexus_Upload_WithMetadataAndETag`)
   - Uploads artifact with SHA-256 checksum
   - Validates ETag header presence
   - Verifies Content-Length matches
   - Tests conditional GET with If-None-Match (returns 304)
   - Downloads and verifies content integrity

3. **Retry on 429** (`Nexus_Upload_HandleRetryOn429`)
   - Tests retry logic for rate limiting
   - Validates Retry-After header handling

4. **Checksum Validation** (`Nexus_Upload_WithChecksum`)
   - Verifies SHA-256 checksum generation
   - Ensures checksum header is sent

5. **Authentication Failure** (`Nexus_Upload_InvalidCredentials_Returns401`)
   - Tests invalid credentials scenario
   - Expects HTTP 401 Unauthorized

6. **Invalid Repository** (`Nexus_Upload_InvalidRepository_Returns400Or404`)
   - Tests upload to nonexistent repository
   - Expects HTTP 400/404 error

7. **Metadata Headers** (`Nexus_Head_Request_AllMetadataHeaders`)
   - Validates all HTTP metadata headers:
     - Content-Length
     - Last-Modified
     - ETag
     - Content-Type
     - Cache-Control (if present)

8. **Conditional Request with If-Modified-Since** (`Nexus_ConditionalRequest_IfModifiedSince_Returns304`)
   - Tests HTTP caching with If-Modified-Since
   - Expects 304 Not Modified

9. **Large File Upload** (`Nexus_LargeFile_Upload_With_ProgressTracking`)
   - Uploads 10MB file
   - Verifies size integrity

10. **Special Characters in Filename** (`Nexus_Upload_SpecialCharacters_InFileName`)
    - Tests handling of special characters and versions
    - Example: `test-special_chars.v1.0-SNAPSHOT.jar`

---

### Artifactory
**File**: `tests/Ci_Cd.Tests/ArtifactoryE2ETests.cs`

#### Tests Implemented:
1. **Health Check** (`Artifactory_ShouldBeHealthy`)
   - Pings Artifactory API
   - Validates service availability

2. **Upload with Metadata and ETag** (`Artifactory_Upload_WithMetadataAndETag`)
   - Uploads artifact with metadata
   - Validates ETag, Last-Modified headers
   - Checks X-Checksum-* headers (Artifactory-specific)
   - Tests conditional GET (If-None-Match → 304)
   - Tests conditional GET (If-Modified-Since → 304)

3. **Duplicate File Upload** (`Artifactory_Upload_DuplicateFile_ShouldSucceed`)
   - Tests overwriting existing artifacts
   - Verifies content is updated

4. **Invalid Credentials** (`Artifactory_Upload_InvalidCredentials_Returns401`)
   - Tests authentication failure
   - Expects HTTP 401

5. **Invalid Repository** (`Artifactory_Upload_InvalidRepo_Returns400Or404`)
   - Tests upload to nonexistent repository
   - Expects HTTP 400/404

6. **Artifact Info API** (`Artifactory_GetArtifactInfo_ReturnsMetadata`)
   - Queries artifact metadata via REST API
   - Validates JSON response fields:
     - repo
     - path
     - size
     - checksums (sha256, md5, sha1)
     - created timestamp
     - createdBy user

7. **Retry on 429** (`Artifactory_HandleRetryOn429`)
   - Tests rate limiting handling

8. **Large File Upload** (`Artifactory_LargeFile_Upload`)
   - Uploads 10MB file
   - Verifies size via HEAD request

---

### Docker Registry
**File**: `tests/Ci_Cd.Tests/DockerRegistryE2ETests.cs`

#### Tests Implemented:
1. **Health Check** (`Registry_ShouldBeHealthy`)
   - Checks `/v2/` endpoint
   - Validates registry availability

2. **Full Push Flow with Manifest Verification** (`Registry_FullPushFlow_WithManifestVerification`)
   - Performs complete Docker image push
   - Validates manifest structure:
     - schemaVersion: 2
     - mediaType
     - config object (digest, size)
     - layers array
   - Verifies Docker-Content-Digest header
   - Checks blob existence
   - Validates tags list API

3. **Conditional Request with ETag** (`Registry_ConditionalRequest_WithETag`)
   - Gets manifest with ETag
   - Tests If-None-Match → 304 response

4. **Multiple Tags for Same Image** (`Registry_MultipleTagsForSameImage`)
   - Pushes same content with different tags
   - Verifies both tags point to same digest

5. **Delete Manifest** (`Registry_DeleteManifest`)
   - Tests DELETE operation on manifest
   - Handles 202 Accepted / 404 / 405 responses

6. **Blob Exists Check** (`Registry_BlobExists_Returns200`)
   - HEAD request for blob
   - Validates Content-Length

7. **Blob Not Found** (`Registry_BlobNotExists_Returns404`)
   - Tests 404 for nonexistent blob

8. **Manifest Not Found** (`Registry_ManifestNotFound_Returns404`)
   - Tests 404 for nonexistent manifest/tag

9. **Invalid Manifest Media Type** (`Registry_InvalidManifestMediaType_Returns400Or415`)
   - Tests rejection of invalid manifest
   - Expects 400 Bad Request or 415 Unsupported Media Type

10. **Catalog API** (`Registry_CatalogAPI_ListsRepositories`)
    - Queries `/v2/_catalog`
    - Validates repository listing

11. **Range Request** (`Registry_RangeRequest_PartialContent`)
    - Tests HTTP Range header support
    - Expects 206 Partial Content (if supported)

12. **Cache Headers Validation** (`Registry_CacheHeaders_Validation`)
    - Validates Docker-specific headers:
      - Docker-Content-Digest
      - Docker-Distribution-API-Version
      - ETag

13. **Large Layer Upload** (`Registry_LargeLayer_Upload`)
    - Uploads 100MB layer
    - Verifies blob size

---

### GitHub Releases
**File**: `tests/Ci_Cd.Tests/GitHubReleasesE2ETests.cs`

#### Tests Implemented:
1. **API Reachability** (`GitHub_Api_ShouldBeReachable`)
   - Checks GitHub API connectivity

2. **Rate Limit Check** (`GitHub_RateLimit_Check`)
   - Queries `/rate_limit` endpoint
   - Validates remaining API calls
   - **Requires**: `GITHUB_TOKEN` environment variable

3. **Upload Release with Metadata** (`GitHub_Upload_Release_WithMetadataAndETag`)
   - Creates release with unique tag
   - Uploads asset
   - Validates release metadata:
     - tag_name
     - assets array
     - asset name, size
     - browser_download_url
   - Tests ETag and Last-Modified headers
   - Tests conditional request (If-None-Match → 304)
   - Downloads and verifies content
   - Cleans up release after test
   - **Requires**: `GITHUB_TOKEN`, `GITHUB_TEST_REPO` env vars

4. **Invalid Token** (`GitHub_Upload_InvalidToken_Returns401`)
   - Tests authentication failure
   - Expects HTTP 401

5. **Invalid Repository** (`GitHub_Upload_InvalidRepo_Returns404`)
   - Tests upload to nonexistent repository
   - Expects HTTP 404

6. **Multiple Assets Same Release** (`GitHub_Upload_MultipleAssets_SameRelease`)
   - Uploads multiple assets to one release
   - Verifies both assets are present

7. **Rate Limit Headers** (`GitHub_RateLimit_Headers_Present`)
   - Validates rate limit headers in responses:
     - X-RateLimit-Limit
     - X-RateLimit-Remaining
     - X-RateLimit-Reset

8. **Large Asset Upload** (`GitHub_Upload_LargeAsset`)
   - Uploads 50MB file
   - Verifies size via API
   - Tests against GitHub's 2GB limit

---

## Environment Setup

### Local Docker Services
Start integration services:
```bash
./scripts/ci/start-integration-services.sh
```

This starts:
- **Nexus**: http://localhost:8081 (admin/admin123)
- **Artifactory**: http://localhost:8082 (admin/password)
- **Docker Registry**: http://localhost:5000
- **SonarQube**: http://localhost:9000

### GitHub Releases Tests
Set environment variables:
```bash
export GITHUB_TOKEN="your_github_token"
export GITHUB_TEST_REPO="owner/repository"
```

**Note**: GitHub tests are skipped if environment variables are not set.

---

## Running Tests

### All Artifact E2E Tests
```bash
./scripts/ci/test-artifacts-e2e.sh
```

### Specific Test Category
```bash
# Nexus only
dotnet test --filter "FullyQualifiedName~NexusIntegrationTests"

# Artifactory only
dotnet test --filter "FullyQualifiedName~ArtifactoryE2ETests"

# Docker Registry only
dotnet test --filter "FullyQualifiedName~DockerRegistryE2ETests"

# GitHub Releases only
dotnet test --filter "FullyQualifiedName~GitHubReleasesE2ETests"
```

### Integration Category
```bash
dotnet test --filter "Category=Integration"
```

---

## Test Scenarios Covered

### HTTP Status Codes
- ✅ 200 OK - Successful operations
- ✅ 304 Not Modified - Conditional requests (ETag, If-Modified-Since)
- ✅ 401 Unauthorized - Authentication failures
- ✅ 404 Not Found - Missing resources
- ✅ 400 Bad Request - Invalid requests
- ✅ 415 Unsupported Media Type - Invalid content types
- ✅ 429 Too Many Requests - Rate limiting
- ✅ 202 Accepted - Async operations (Docker Registry delete)
- ✅ 206 Partial Content - Range requests

### Metadata Headers
- ✅ ETag - Entity tag for caching
- ✅ Last-Modified - Modification timestamp
- ✅ Content-Length - File size
- ✅ Content-Type - MIME type
- ✅ Cache-Control - Caching directives
- ✅ X-Checksum-* - Integrity checksums (Artifactory, Nexus)
- ✅ Docker-Content-Digest - Docker image digest
- ✅ Docker-Distribution-API-Version - Registry API version
- ✅ X-RateLimit-* - GitHub rate limiting info
- ✅ Retry-After - Rate limit retry timing

### Edge Cases
- ✅ Large files (10MB, 50MB, 100MB)
- ✅ Special characters in filenames
- ✅ Duplicate uploads/overwrites
- ✅ Nonexistent repositories
- ✅ Invalid credentials
- ✅ Multiple tags/assets
- ✅ Conditional requests
- ✅ Range requests
- ✅ Delete operations
- ✅ Catalog/listing APIs

---

## Troubleshooting

### Services Not Starting
```bash
# Check service status
docker compose -f Ci_Cd/docker-compose.integration.yml ps

# View logs
docker compose -f Ci_Cd/docker-compose.integration.yml logs nexus
docker compose -f Ci_Cd/docker-compose.integration.yml logs artifactory
docker compose -f Ci_Cd/docker-compose.integration.yml logs registry

# Restart services
docker compose -f Ci_Cd/docker-compose.integration.yml down
docker compose -f Ci_Cd/docker-compose.integration.yml up -d
```

### Nexus First-Time Setup
1. Open http://localhost:8081
2. Complete setup wizard
3. Set admin password to `admin123`
4. Create `maven-releases` repository if needed

### Artifactory First-Time Setup
1. Open http://localhost:8082
2. Login with admin/password
3. Complete setup wizard
4. Create `generic-local` repository

### GitHub Tests Failing
- Ensure `GITHUB_TOKEN` has appropriate scopes:
  - `repo` - Full repository access
  - `write:packages` - Upload release assets
- Verify `GITHUB_TEST_REPO` format: `owner/repository`
- Check rate limit: https://api.github.com/rate_limit

---

## CI/CD Integration

### GitHub Actions Example
```yaml
name: E2E Artifact Tests

on: [push, pull_request]

jobs:
  e2e-tests:
    runs-on: ubuntu-latest
    
    steps:
      - uses: actions/checkout@v3
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0.x'
      
      - name: Start Integration Services
        run: ./scripts/ci/start-integration-services.sh
      
      - name: Run E2E Tests
        run: ./scripts/ci/test-artifacts-e2e.sh
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
          GITHUB_TEST_REPO: ${{ github.repository }}
      
      - name: Cleanup
        if: always()
        run: docker compose -f Ci_Cd/docker-compose.integration.yml down -v
```

---

## Performance Metrics

Typical test execution times:
- Nexus tests: ~30-60 seconds
- Artifactory tests: ~30-60 seconds
- Docker Registry tests: ~20-40 seconds
- GitHub Releases tests: ~30-90 seconds (network dependent)

Total suite: ~2-5 minutes (excluding service startup time)

---

## Future Enhancements

- [ ] Azure Artifacts E2E tests
- [ ] AWS S3 artifact storage tests
- [ ] Google Artifact Registry tests
- [ ] Stress testing with concurrent uploads
- [ ] Network failure simulation
- [ ] Bandwidth throttling tests
- [ ] Authentication token refresh tests
- [ ] Multi-region replication validation

---

## References

- [Nexus Repository Manager API](https://help.sonatype.com/repomanager3/integrations/rest-and-integration-api)
- [JFrog Artifactory API](https://www.jfrog.com/confluence/display/JFROG/Artifactory+REST+API)
- [Docker Registry HTTP API V2](https://docs.docker.com/registry/spec/api/)
- [GitHub Releases API](https://docs.github.com/en/rest/releases)

