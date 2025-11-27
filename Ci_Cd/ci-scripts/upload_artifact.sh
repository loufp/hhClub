#!/usr/bin/env bash
set -euo pipefail

file="$1"
mode="$2" # artifactory|github

# artifactory args: url repo user pass
# github args: repo token (owner/repo)

retry() {
  local attempts=5
  local delay=2
  local i=0
  while [ "$i" -lt "$attempts" ]; do
    "$@" && return 0
    i=$((i+1))
    sleep $((delay * i))
  done
  return 1
}

upload_artifactory() {
  local url="$1"; shift
  local repo="$1"; shift
  local user="$1"; shift
  local pass="$1"; shift
  local name
  name=$(basename "$file")
  local target

  # ensure base url doesn't end with /
  url=${url%/}
  target="$url/$repo/$name"
  echo "Uploading $file to Artifactory: $target"

  # try upload with retry
  if retry curl -sS -u "$user:$pass" -T "$file" -H "X-Checksum-Deploy: false" "$target"; then
    echo "Uploaded to Artifactory: $target"
    return 0
  else
    echo "Failed to upload to Artifactory: $target" >&2
    return 1
  fi
}

# helper to parse JSON field (simple, avoids jq)
json_extract() {
  local key="$1"; shift
  sed -n 's/.*"'$key'"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p'
}

upload_github() {
  local ghrepo="$1"; shift
  local token="$1"; shift
  local name
  name=$(basename "$file")
  local tag=${CI_COMMIT_TAG:-"v$(date +%s)"}
  echo "Creating or obtaining release '$tag' in $ghrepo and uploading $name"

  # create release payload
  local payload
  payload=$(printf '{"tag_name":"%s","name":"%s","draft":false,"prerelease":false}' "$tag" "$tag")

  # try create release
  local create_resp
  create_resp=$(curl -sS -X POST "https://api.github.com/repos/$ghrepo/releases" -H "Authorization: token $token" -H "Accept: application/vnd.github.v3+json" -d "$payload" || true)

  # extract upload_url
  local upload_url
  upload_url=$(echo "$create_resp" | json_extract upload_url | sed 's/{?name,label}//')

  # if failed to create (e.g., tag already exists), try get release by tag
  if [ -z "$upload_url" ]; then
    echo "Create release returned no upload_url, attempting to get release by tag"
    local get_resp
    get_resp=$(curl -sS "https://api.github.com/repos/$ghrepo/releases/tags/$tag" -H "Authorization: token $token" -H "Accept: application/vnd.github.v3+json" || true)
    upload_url=$(echo "$get_resp" | json_extract upload_url | sed 's/{?name,label}//')
  fi

  # if still empty, try to list releases and pick latest
  if [ -z "$upload_url" ]; then
    echo "Could not find release by tag, trying to list releases"
    local list_resp
    list_resp=$(curl -sS "https://api.github.com/repos/$ghrepo/releases" -H "Authorization: token $token" -H "Accept: application/vnd.github.v3+json" || true)
    upload_url=$(echo "$list_resp" | grep -o '"upload_url": *"[^"]*"' | head -n1 | sed 's/"upload_url": *"//' | sed 's/{?name,label}"$//' || true)
  fi

  if [ -z "$upload_url" ]; then
    echo "Failed to obtain upload_url for GitHub release" >&2
    echo "Response from create: $create_resp" >&2
    return 1
  fi

  # perform upload
  local upload_endpoint
  upload_endpoint="$upload_url?name=$(python3 -c 'import urllib.parse,sys; print(urllib.parse.quote(sys.argv[1]))' "$name")"

  echo "Uploading asset to: $upload_endpoint"
  # use retry for upload
  if retry curl -sS -X POST "$upload_endpoint" -H "Authorization: token $token" -H "Content-Type: application/octet-stream" --data-binary "@$file"; then
    echo "Uploaded $name to GitHub release"
    return 0
  else
    echo "Failed to upload asset to GitHub" >&2
    return 1
  fi
}

case "$mode" in
  artifactory)
    if [ $# -lt 4 ]; then
      echo "Usage: $0 <file> artifactory <url> <repo> <user> <pass>" >&2
      exit 2
    fi
    upload_artifactory "$@"
    ;;
  github)
    if [ $# -lt 2 ]; then
      echo "Usage: $0 <file> github <owner/repo> <token>" >&2
      exit 2
    fi
    upload_github "$@"
    ;;
  *)
    echo "Unknown upload mode: $mode" >&2
    exit 2
    ;;
esac
