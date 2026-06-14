$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$workflowPath = Join-Path $root ".github/workflows/ci.yml"
$lineGuardPath = Join-Path $root "scripts/check-csharp-file-lines.ps1"
if (-not (Test-Path -LiteralPath $workflowPath)) {
    throw "Workflow file does not exist: $workflowPath"
}

if (-not (Test-Path -LiteralPath $lineGuardPath)) {
    throw "Line guard script does not exist: $lineGuardPath"
}

$workflow = Get-Content -Raw -LiteralPath $workflowPath
$lineGuard = Get-Content -Raw -LiteralPath $lineGuardPath

function Get-WorkflowJobBlock([string] $jobId) {
    $escaped = [regex]::Escape($jobId)
    $match = [regex]::Match($workflow, "(?ms)^  ${escaped}:\s*\r?\n.*?(?=^  [A-Za-z0-9_-]+:\s*\r?\n|\z)")
    if (-not $match.Success) {
        throw "Workflow job '$jobId' does not exist."
    }

    return $match.Value
}

$buildJob = Get-WorkflowJobBlock "build-test-pack"
$attestationJob = Get-WorkflowJobBlock "attest-package-artifacts"

$usesMatches = [regex]::Matches($workflow, "(?m)^\s*uses:\s*(?<action>[^@\s]+)@(?<ref>[^\s#]+)")
foreach ($match in $usesMatches) {
    $action = $match.Groups["action"].Value
    $ref = $match.Groups["ref"].Value
    if ($ref -notmatch "^[0-9a-fA-F]{40}$") {
        throw "Workflow action '$action@$ref' must be pinned to a full 40-character commit SHA."
    }
}

if ($buildJob -notmatch "(?ms)^\s{4}permissions:\s*\r?\n\s{6}contents:\s+read\s*(?:\r?\n|$)") {
    throw "Build/test/package job must declare explicit contents: read permissions."
}

if ($buildJob -match "(?m)^\s+(id-token|attestations):\s+write\s*$") {
    throw "Build/test/package job must not receive OIDC or attestation write permissions."
}

if ($buildJob -match "actions/attest-build-provenance@") {
    throw "Build/test/package job must not perform release attestation."
}

if ($attestationJob -notmatch "(?m)^\s{4}if:\s+\$\{\{\s*startsWith\(github\.ref,\s*'refs/heads/release/'\)\s*\|\|\s*startsWith\(github\.ref,\s*'refs/tags/'\)\s*\}\}\s*$") {
    throw "Attestation job must be restricted to release branches and tags."
}

if ($attestationJob -notmatch "(?m)^\s{4}needs:\s+build-test-pack\s*$") {
    throw "Attestation job must depend on successful build/test/package completion."
}

if ($attestationJob -notmatch "(?ms)^\s{4}permissions:\s*\r?\n\s{6}contents:\s+read\s*\r?\n\s{6}id-token:\s+write\s*\r?\n\s{6}attestations:\s+write") {
    throw "Attestation job must declare contents read plus OIDC and attestation write permissions."
}

if ($attestationJob -match "(?im)^\s+run:\s*" -or
    $attestationJob -match "(?im)^\s+uses:\s+actions/(checkout|setup-dotnet)@") {
    throw "Attestation job must only download package artifacts and attest them."
}

if ($workflow -match "(?m)^\s+contents:\s+write\s*$") {
    throw "Package-producing workflow must not grant contents: write."
}

if ($attestationJob -notmatch "actions/attest-build-provenance@[0-9a-fA-F]{40}") {
    throw "Release package workflow must attest package artifacts with a pinned attest-build-provenance action."
}

if ($workflow -match "packages-\$\{\{\s*matrix\.os\s*\}\}") {
    throw "Package-producing workflow must upload one canonical package artifact set, not OS-specific package sets."
}

if ($workflow -notmatch "(?ms)- name: Pack\s*\r?\n\s+if:\s+\$\{\{\s*matrix\.os\s*==\s*'ubuntu-latest'\s*\}\}") {
    throw "Package pack step must run only on the canonical ubuntu-latest matrix leg."
}

if ($workflow -notmatch "(?ms)- name: Upload package artifacts.*?name:\s+packages-canonical") {
    throw "Package upload step must publish the canonical package artifact set as 'packages-canonical'."
}

if ($attestationJob -notmatch "artifacts/packages/\*\.nupkg") {
    throw "Package attestation must cover every .nupkg in artifacts/packages."
}

if ($attestationJob -notmatch "artifacts/packages/\*\.snupkg") {
    throw "Package attestation must cover every .snupkg in artifacts/packages."
}

if ($lineGuard -match "(?im)^\s*&?\s*dotnet\s+(tool\s+(install|restore|run)|new\s+tool-manifest)\b") {
    throw "Release line guard must not install, restore, or execute dotnet local tools in the package-producing job."
}

Write-Host "Release workflow security check passed."
