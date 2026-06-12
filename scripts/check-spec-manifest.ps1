param(
    [switch] $Update
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$specRoot = Join-Path $root "docs/Specs/Initial/safe-ir-sandbox-spec"
$manifestPath = Join-Path $specRoot "manifest.json"

if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "Missing spec manifest: $manifestPath"
}

$existing = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$created = if ($existing.created) { [string] $existing.created } else { (Get-Date -Format "yyyy-MM-dd") }
$entries = @()

foreach ($file in Get-ChildItem -LiteralPath $specRoot -Recurse -File | Sort-Object FullName) {
    if ($file.FullName -eq $manifestPath) {
        continue
    }

    $relative = [System.IO.Path]::GetRelativePath($specRoot, $file.FullName).Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $entries += [ordered] @{
        path = $relative
        sha256 = $hash
        bytes = $file.Length
    }
}

$manifest = [ordered] @{
    name = "safe-ir-sandbox-spec"
    created = $created
    files = $entries
}

$expected = ($manifest | ConvertTo-Json -Depth 8) + [Environment]::NewLine
if ($Update) {
    Set-Content -LiteralPath $manifestPath -Value $expected -Encoding utf8NoBOM -NoNewline
    Write-Host "Updated spec manifest: $manifestPath"
    return
}

$actual = Get-Content -Raw -LiteralPath $manifestPath
if ($actual -ne $expected) {
    Write-Error "Spec manifest is stale. Run ./scripts/check-spec-manifest.ps1 -Update"
}

Write-Host "Spec manifest check passed. Files: $($entries.Count)"
