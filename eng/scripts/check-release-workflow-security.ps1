#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Security gate for the tag-driven release workflow (.github/workflows/release.yml).

.DESCRIPTION
    Asserts that the package-producing / publishing pipelines keep their security posture:
      - every action is pinned to a full 40-char commit SHA (no floating tags);
      - the privileged attestation job (OIDC + attestation write) is isolated, depends on
        pack, and only downloads + attests artifacts (no source checkout / build / test);
      - the pack job does NOT carry OIDC/attestation write permissions;
      - publishing to NuGet.org is gated to the canonical repo on a real tag;
      - main-branch CI prerelease publishing is gated to the canonical repo and only
        publishes packages produced by its pack job;
      - provenance attestation covers both .nupkg and .snupkg with a pinned action;
      - CI gates run the release-readiness checklist evidence gate in require-complete mode;
      - the line-length guard is not abused to run local dotnet tools.

    Lives in eng/scripts/ (repo root is two levels up).
#>

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$workflowPath = Join-Path $root ".github/workflows/release.yml"
$ciWorkflowPath = Join-Path $root ".github/workflows/ci.yml"
$lineGuardPath = Join-Path $root "eng/scripts/check-csharp-file-lines.ps1"

if (-not (Test-Path -LiteralPath $workflowPath)) {
    throw "Release workflow file does not exist: $workflowPath"
}

if (-not (Test-Path -LiteralPath $ciWorkflowPath)) {
    throw "CI workflow file does not exist: $ciWorkflowPath"
}

if (-not (Test-Path -LiteralPath $lineGuardPath)) {
    throw "Line guard script does not exist: $lineGuardPath"
}

$workflow = Get-Content -Raw -LiteralPath $workflowPath
$ciWorkflow = Get-Content -Raw -LiteralPath $ciWorkflowPath
$lineGuard = Get-Content -Raw -LiteralPath $lineGuardPath

function Get-WorkflowJobBlock([string] $jobId) {
    $escaped = [regex]::Escape($jobId)
    $match = [regex]::Match($workflow, "(?ms)^  ${escaped}:\s*\r?\n.*?(?=^  [A-Za-z0-9_-]+:\s*\r?\n|\z)")
    if (-not $match.Success) {
        throw "Release workflow job '$jobId' does not exist."
    }

    return $match.Value
}

$packJob = Get-WorkflowJobBlock "pack"
$attestJob = Get-WorkflowJobBlock "attest"
$publishJob = Get-WorkflowJobBlock "publish"

function Get-CiWorkflowJobBlock([string] $jobId) {
    $escaped = [regex]::Escape($jobId)
    $match = [regex]::Match($ciWorkflow, "(?ms)^  ${escaped}:\s*\r?\n.*?(?=^  [A-Za-z0-9_-]+:\s*\r?\n|\z)")
    if (-not $match.Success) {
        throw "CI workflow job '$jobId' does not exist."
    }

    return $match.Value
}

$ciPackJob = Get-CiWorkflowJobBlock "pack-packages"
$ciPublishJob = Get-CiWorkflowJobBlock "publish-nuget"
$ciGatesJob = Get-CiWorkflowJobBlock "gates"

# 1. Every action reference must be pinned to a full commit SHA.
function Assert-PinnedActions([string] $workflowText, [string] $description) {
    $usesMatches = [regex]::Matches($workflowText, "(?m)^\s*uses:\s*(?<action>[^@\s]+)@(?<ref>[^\s#]+)")
    foreach ($match in $usesMatches) {
        $action = $match.Groups["action"].Value
        $ref = $match.Groups["ref"].Value
        # Local reusable workflow references (./.github/...) have no @ref; the regex won't match
        # them. Any external action that does match must be SHA-pinned.
        if ($action.StartsWith("./", [StringComparison]::Ordinal)) {
            continue
        }
        if ($ref -notmatch "^[0-9a-fA-F]{40}$") {
            throw "$description action '$action@$ref' must be pinned to a full 40-character commit SHA."
        }
    }
}

Assert-PinnedActions $workflow "Release workflow"
$ciUsesMatches = [regex]::Matches($ciWorkflow, "(?m)^\s*uses:\s*(?<action>[^@\s]+)@(?<ref>[^\s#]+)")
foreach ($match in $ciUsesMatches) {
    $action = $match.Groups["action"].Value
    $ref = $match.Groups["ref"].Value
    if ($action.StartsWith("./", [StringComparison]::Ordinal)) {
        continue
    }
    if ($ref -notmatch "^[0-9a-fA-F]{40}$") {
        throw "CI workflow action '$action@$ref' must be pinned to a full 40-character commit SHA."
    }
}

# 2. The pack job must not receive OIDC or attestation write permissions, nor attest itself.
if ($packJob -match "(?m)^\s+(id-token|attestations):\s+write\s*$") {
    throw "Pack job must not receive OIDC or attestation write permissions."
}

if ($packJob -match "actions/attest-build-provenance@") {
    throw "Pack job must not perform release attestation."
}

# 3. The attestation job must be properly scoped and isolated.
if ($attestJob -notmatch "(?m)^\s{4}needs:\s+pack\s*$") {
    throw "Attestation job must depend on successful pack completion (needs: pack)."
}

if ($attestJob -notmatch "(?ms)^\s{4}permissions:\s*\r?\n(?:\s{6}[a-z-]+:\s+\S+\s*\r?\n)*\s{6}id-token:\s+write\s*\r?\n") {
    throw "Attestation job must declare id-token: write."
}

if ($attestJob -notmatch "(?m)^\s{6}attestations:\s+write\s*$") {
    throw "Attestation job must declare attestations: write."
}

# The attestation job must only download artifacts and attest them: no source checkout,
# no SDK setup, no arbitrary run steps that could tamper with the artifacts pre-attestation.
if ($attestJob -match "(?im)^\s+uses:\s+actions/(checkout|setup-dotnet)@") {
    throw "Attestation job must not check out source or set up the SDK; it must only download and attest artifacts."
}

if ($attestJob -match "(?im)^\s+run:\s*") {
    throw "Attestation job must not contain arbitrary run steps; it must only download and attest artifacts."
}

if ($attestJob -notmatch "actions/attest-build-provenance@[0-9a-fA-F]{40}") {
    throw "Release workflow must attest packages with a SHA-pinned attest-build-provenance action."
}

if ($attestJob -notmatch "artifacts/packages/\*\.nupkg") {
    throw "Package attestation must cover every .nupkg in artifacts/packages."
}

if ($attestJob -notmatch "artifacts/packages/\*\.snupkg") {
    throw "Package attestation must cover every .snupkg in artifacts/packages."
}

# 4. Publishing must be gated to the canonical repo on a real tag.
if ($publishJob -notmatch "github\.repository\s*==\s*'JKamsker/DotBoxD'") {
    throw "Publish job must be gated to the canonical repository (github.repository == 'JKamsker/DotBoxD')."
}

if ($publishJob -notmatch "github\.event_name\s*==\s*'push'") {
    throw "Publish job must be gated to push events."
}

if ($publishJob -notmatch "startsWith\(github\.ref,\s*'refs/tags/v'\)") {
    throw "Publish job must be gated to version tag refs (startsWith(github.ref, 'refs/tags/v'))."
}

# 5. The reused CI workflow must gate the release (verify job uses ci.yml).
if ($workflow -notmatch "(?m)^\s+uses:\s+\./\.github/workflows/ci\.yml\s*$") {
    throw "Release workflow must reuse ci.yml as a verification gate (uses: ./.github/workflows/ci.yml)."
}

# 6. CI must run release readiness before it can produce publishable package artifacts.
if ($ciGatesJob -notmatch "check-release-readiness\.ps1\s+-RequireComplete") {
    throw "CI gates job must run eng/scripts/check-release-readiness.ps1 -RequireComplete."
}

# 7. Main-branch CI publishing must consume pack artifacts and stay tightly gated.
if ($ciPackJob -notmatch "(?m)^\s{4}needs:\s+\[build-test,\s*gates\]\s*$") {
    throw "CI pack job must depend on build-test and gates before producing publishable packages."
}

if ($ciPackJob -match "bitwarden/sm-action@" -or $ciPackJob -match "dotnet\s+nuget\s+push") {
    throw "CI pack job must not fetch publish credentials or push packages."
}

if ($ciPublishJob -notmatch "(?m)^\s{4}needs:\s+pack-packages\s*$") {
    throw "CI publish job must depend on the pack-packages artifact job."
}

if ($ciPublishJob -notmatch "github\.event_name\s*==\s*'push'") {
    throw "CI publish job must be gated to push events."
}

if ($ciPublishJob -notmatch "github\.repository\s*==\s*'JKamsker/DotBoxD'") {
    throw "CI publish job must be gated to the canonical repository (github.repository == 'JKamsker/DotBoxD')."
}

if ($ciPublishJob -notmatch "github\.ref\s*==\s*'refs/heads/main'") {
    throw "CI publish job must be gated to the main branch."
}

if ($ciPublishJob -match "(?im)^\s+uses:\s+actions/checkout@") {
    throw "CI publish job must not check out source; it may only download package artifacts and publish them."
}

if ($ciPublishJob -notmatch "actions/download-artifact@[0-9a-fA-F]{40}") {
    throw "CI publish job must download packaged artifacts with a SHA-pinned action."
}

if ($ciPublishJob -notmatch "name:\s+nuget-packages") {
    throw "CI publish job must download the nuget-packages artifact produced by pack-packages."
}

if ($ciPublishJob -notmatch "dotnet\s+nuget\s+push\s+`"artifacts/packages/\*\.nupkg`"") {
    throw "CI publish job must push the downloaded .nupkg files from artifacts/packages."
}

# 8. The line guard must not install, restore, or execute dotnet local tools.
if ($lineGuard -match "(?im)^\s*&?\s*dotnet\s+(tool\s+(install|restore|run)|new\s+tool-manifest)\b") {
    throw "Release line guard must not install, restore, or execute dotnet local tools."
}

Write-Host "Release workflow security check passed."
