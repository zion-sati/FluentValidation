#!/usr/bin/env bash

set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
output_dir="${repository_root}/artifacts/packages"
release_version="$(tr -d '[:space:]' < "${repository_root}/eng/NetWasm.ReleaseVersion.txt")"
output_was_set=false
while [[ $# -gt 0 ]]; do
  case "$1" in
    --output)
      [[ $# -ge 2 ]] || { echo "--output requires a directory." >&2; exit 2; }
      output_dir="$2"
      output_was_set=true
      shift 2
      ;;
    --version)
      [[ $# -ge 2 ]] || { echo "--version requires a value." >&2; exit 2; }
      release_version="$2"
      shift 2
      ;;
    --help|-h)
      echo "Usage: $0 [--version VERSION] [--output DIRECTORY] [DIRECTORY]"
      exit 0
      ;;
    --*)
      echo "Unknown option: $1" >&2
      exit 2
      ;;
    *)
      if [[ "${output_was_set}" == true ]]; then
        echo "Package output was specified more than once." >&2
        exit 2
      fi
      output_dir="$1"
      output_was_set=true
      shift
      ;;
  esac
done

mkdir -p "${output_dir}"
output_dir="$(cd "${output_dir}" && pwd -P)"
case "${output_dir}" in
  "${repository_root}/artifacts"|"${repository_root}/artifacts"/*)
    ;;
  "${repository_root}"|"${repository_root}"/*)
    echo "Package output inside the repository must stay under artifacts/: ${output_dir}" >&2
    exit 2
    ;;
esac

find "${output_dir}" -maxdepth 1 -type f \
  \( -name 'NetWasm.FluentValidation.*.nupkg' -o -name 'NetWasm.FluentValidation.*.snupkg' \) \
  -delete

build_root="$(mktemp -d "${TMPDIR:-/tmp}/netwasm-fluentvalidation-build.XXXXXX")"
build_root="$(cd "${build_root}" && pwd -P)"
source_root="${build_root}/source"
cleanup() {
  if [[ -d "${source_root}" ]]; then
    git -C "${repository_root}" worktree remove --force "${source_root}" >/dev/null 2>&1 || true
  fi
  rm -rf "${build_root}"
}
trap cleanup EXIT

for command in dotnet git; do
  command -v "${command}" >/dev/null 2>&1 || {
    echo "Missing required command: ${command}" >&2
    exit 2
  }
done

[[ "$(git -C "${repository_root}" rev-parse --is-inside-work-tree 2>/dev/null || true)" == "true" ]] || {
  echo "Package construction requires a Git checkout." >&2
  exit 2
}
[[ -z "$(git -C "${repository_root}" status --porcelain=v1)" ]] || {
  echo "Package construction requires a clean checkout." >&2
  exit 2
}

repository_commit="$(git -C "${repository_root}" rev-parse HEAD)"
git -C "${repository_root}" worktree add --quiet --detach "${source_root}" "${repository_commit}"
cd "${source_root}"

nuget_config="${build_root}/NuGet.Config"
xml_escape() {
  printf '%s' "$1" | sed -e 's/&/\&amp;/g' -e 's/"/\&quot;/g' -e 's/</\&lt;/g' -e 's/>/\&gt;/g'
}
output_dir_xml="$(xml_escape "${output_dir}")"
ci_package_source_xml="$(xml_escape "${NETWASM_CI_PACKAGE_SOURCE:-}")"
{
  printf '%s\n' \
    '<?xml version="1.0" encoding="utf-8"?>' \
    '<configuration>' \
    '  <packageSources>' \
    '    <clear />' \
    "    <add key=\"current-build\" value=\"${output_dir_xml}\" />"
  if [[ -n "${NETWASM_CI_PACKAGE_SOURCE:-}" ]]; then
    printf '    <add key="ci-artifacts" value="%s" />\n' "${ci_package_source_xml}"
  fi
  printf '%s\n' \
    '    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />' \
    '  </packageSources>' \
    '</configuration>'
} > "${nuget_config}"

package_cache="${NUGET_PACKAGES:-${build_root}/packages}"
sdk_version="$(sed -n 's/.*"NetWasm.Sdk": "\([^"]*\)".*/\1/p' "${source_root}/global.json")"
if [[ -z "${sdk_version}" ]]; then
  echo "Unable to read the NetWasm.Sdk version from global.json." >&2
  exit 1
fi

seed_project="${build_root}/SeedSdk.csproj"
printf '%s\n' \
  '<Project Sdk="Microsoft.NET.Sdk">' \
  '  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>' \
  "  <ItemGroup><PackageReference Include=\"NetWasm.Sdk\" Version=\"${sdk_version}\" PrivateAssets=\"all\" /></ItemGroup>" \
  '</Project>' > "${seed_project}"
NUGET_PACKAGES="${package_cache}" dotnet restore "${seed_project}" \
  --configfile "${nuget_config}" \
  --disable-build-servers \
  --nologo

project="${source_root}/src/NetWasm.FluentValidation/NetWasm.FluentValidation.csproj"
NUGET_PACKAGES="${package_cache}" dotnet restore "${project}" \
  --configfile "${nuget_config}" \
  --disable-build-servers \
  --nologo \
  -p:UseArtifactsOutput=true \
  -p:ArtifactsPath="${build_root}/artifacts"
NUGET_PACKAGES="${package_cache}" dotnet pack "${project}" \
  -c Release \
  --no-restore \
  --nologo \
  -o "${output_dir}" \
  -p:PackageVersion="${release_version}" \
  -p:ContinuousIntegrationBuild=true \
  -p:RepositoryCommit="${repository_commit}" \
  -p:UseArtifactsOutput=true \
  -p:ArtifactsPath="${build_root}/artifacts"

package_count="$(find "${output_dir}" -maxdepth 1 -type f \
  -name 'NetWasm.FluentValidation.*.nupkg' | wc -l | tr -d ' ')"
if [[ "${package_count}" -ne 1 ]]; then
  echo "Expected exactly one NetWasm.FluentValidation package, found ${package_count}." >&2
  exit 1
fi

echo "Built NetWasm.FluentValidation ${release_version} in ${output_dir}"
