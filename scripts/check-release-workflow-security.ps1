$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
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
$usesMatches = [regex]::Matches($workflow, "(?m)^\s*uses:\s*(?<action>[^@\s]+)@(?<ref>[^\s#]+)")
foreach ($match in $usesMatches) {
    $action = $match.Groups["action"].Value
    $ref = $match.Groups["ref"].Value
    if ($ref -notmatch "^[0-9a-fA-F]{40}$") {
        throw "Workflow action '$action@$ref' must be pinned to a full 40-character commit SHA."
    }
}

if ($workflow -notmatch "(?ms)build-test-pack:.*?permissions:\s*\r?\n\s+contents:\s+read\s*\r?\n\s+id-token:\s+write\s*\r?\n\s+attestations:\s+write") {
    throw "Package-producing job must declare explicit least-privilege permissions for contents read and package attestation."
}

if ($workflow -match "(?m)^\s+contents:\s+write\s*$") {
    throw "Package-producing workflow must not grant contents: write."
}

if ($workflow -notmatch "actions/attest-build-provenance@[0-9a-fA-F]{40}") {
    throw "Release package workflow must attest package artifacts with a pinned attest-build-provenance action."
}

if ($workflow -notmatch "artifacts/packages/\*\.nupkg") {
    throw "Package attestation must cover every .nupkg in artifacts/packages."
}

if ($workflow -notmatch "artifacts/packages/\*\.snupkg") {
    throw "Package attestation must cover every .snupkg in artifacts/packages."
}

if ($lineGuard -match "(?im)^\s*&?\s*dotnet\s+(tool\s+(install|restore|run)|new\s+tool-manifest)\b") {
    throw "Release line guard must not install, restore, or execute dotnet local tools in the package-producing job."
}

Write-Host "Release workflow security check passed."
