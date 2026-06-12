param(
    [switch] $Update
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$specRoot = Join-Path $root "docs/Specs/Initial/safe-ir-sandbox-spec"
$addendumRoot = Join-Path $root "docs/Specs/Addendum"
$manifestPath = Join-Path $specRoot "manifest.json"

if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "Missing spec manifest: $manifestPath"
}

$existing = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$created = if ($existing.created) { [string] $existing.created } else { (Get-Date -Format "yyyy-MM-dd") }
$entries = @()
$utf8 = [System.Text.UTF8Encoding]::new($false, $true)

function Normalize-LineEndings {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Text
    )

    return $Text.Replace("`r`n", "`n").Replace("`r", "`n")
}

function Get-NormalizedTextBytes {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $text = [System.IO.File]::ReadAllText($Path, $utf8)
    $normalized = Normalize-LineEndings -Text $text
    return $utf8.GetBytes($normalized)
}

function Get-Sha256Hex {
    param(
        [Parameter(Mandatory = $true)]
        [byte[]] $Bytes
    )

    $hash = [System.Security.Cryptography.SHA256]::HashData($Bytes)
    return [Convert]::ToHexString($hash).ToLowerInvariant()
}

$documentSets = @(
    @{
        Root = $specRoot
        Prefix = ""
    },
    @{
        Root = $addendumRoot
        Prefix = "../Addendum/"
    }
)

foreach ($documentSet in $documentSets) {
    $documentRoot = [string] $documentSet.Root
    if (-not (Test-Path -LiteralPath $documentRoot)) {
        throw "Missing spec document root: $documentRoot"
    }

    foreach ($file in Get-ChildItem -LiteralPath $documentRoot -Recurse -File | Sort-Object FullName) {
        if ($file.FullName -eq $manifestPath) {
            continue
        }

        $relative = [System.IO.Path]::GetRelativePath($documentRoot, $file.FullName).Replace('\', '/')
        $path = ([string] $documentSet.Prefix) + $relative
        $bytes = Get-NormalizedTextBytes -Path $file.FullName
        $entries += [ordered] @{
            path = $path
            sha256 = Get-Sha256Hex -Bytes $bytes
            bytes = $bytes.Length
        }
    }
}

$entries = @($entries | Sort-Object { $_["path"] })

$manifest = [ordered] @{
    name = "safe-ir-sandbox-spec"
    created = $created
    files = $entries
}

$expectedForWrite = ($manifest | ConvertTo-Json -Depth 8) + [Environment]::NewLine
$expected = Normalize-LineEndings -Text $expectedForWrite
if ($Update) {
    Set-Content -LiteralPath $manifestPath -Value $expectedForWrite -Encoding utf8NoBOM -NoNewline
    Write-Host "Updated spec manifest: $manifestPath"
    return
}

$actual = Normalize-LineEndings -Text (Get-Content -Raw -LiteralPath $manifestPath)
if ($actual -ne $expected) {
    Write-Error "Spec manifest is stale. Run ./scripts/check-spec-manifest.ps1 -Update"
}

Write-Host "Spec manifest check passed. Files: $($entries.Count)"
