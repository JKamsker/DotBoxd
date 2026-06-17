param(
    [switch] $Update
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$specRoot = Join-Path $root "docs/Specs/Initial/dotboxd-sandbox-spec"
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

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha256.ComputeHash($Bytes)
    } finally {
        $sha256.Dispose()
    }

    return -join ($hash | ForEach-Object { $_.ToString("x2") })
}

function ConvertTo-JsonString {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Value
    )

    $builder = New-Object System.Text.StringBuilder
    [void] $builder.Append('"')
    foreach ($character in $Value.ToCharArray()) {
        $code = [int] $character
        if ($code -eq 34) {
            [void] $builder.Append('\"')
        } elseif ($code -eq 92) {
            [void] $builder.Append('\\')
        } elseif ($code -eq 8) {
            [void] $builder.Append('\b')
        } elseif ($code -eq 12) {
            [void] $builder.Append('\f')
        } elseif ($code -eq 10) {
            [void] $builder.Append('\n')
        } elseif ($code -eq 13) {
            [void] $builder.Append('\r')
        } elseif ($code -eq 9) {
            [void] $builder.Append('\t')
        } elseif ($code -lt 32) {
            [void] $builder.Append(('\u{0:x4}' -f $code))
        } else {
            [void] $builder.Append($character)
        }
    }

    [void] $builder.Append('"')
    return $builder.ToString()
}

function ConvertTo-ManifestJson {
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.IDictionary] $Manifest
    )

    $lines = New-Object "System.Collections.Generic.List[string]"
    $files = @($Manifest["files"])
    [void] $lines.Add("{")
    [void] $lines.Add('  "name": ' + (ConvertTo-JsonString ([string] $Manifest["name"])) + ',')
    [void] $lines.Add('  "created": ' + (ConvertTo-JsonString ([string] $Manifest["created"])) + ',')
    [void] $lines.Add('  "files": [')
    for ($i = 0; $i -lt $files.Count; $i++) {
        $entry = $files[$i]
        $suffix = if ($i -eq $files.Count - 1) { "" } else { "," }
        [void] $lines.Add("    {")
        [void] $lines.Add('      "path": ' + (ConvertTo-JsonString ([string] $entry["path"])) + ',')
        [void] $lines.Add('      "sha256": ' + (ConvertTo-JsonString ([string] $entry["sha256"])) + ',')
        [void] $lines.Add('      "bytes": ' + ([string] $entry["bytes"]))
        [void] $lines.Add("    }$suffix")
    }

    [void] $lines.Add("  ]")
    [void] $lines.Add("}")
    return ($lines.ToArray() -join [Environment]::NewLine) + [Environment]::NewLine
}

function Get-RelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $BasePath,
        [Parameter(Mandatory = $true)]
        [string] $TargetPath
    )

    return [System.IO.Path]::GetRelativePath(
        [System.IO.Path]::GetFullPath($BasePath),
        [System.IO.Path]::GetFullPath($TargetPath))
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

        $relative = (Get-RelativePath -BasePath $documentRoot -TargetPath $file.FullName).Replace('\', '/')
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
    name = "dotboxd-sandbox-spec"
    created = $created
    files = $entries
}

$expectedForWrite = ConvertTo-ManifestJson -Manifest $manifest
$expected = Normalize-LineEndings -Text $expectedForWrite
if ($Update) {
    [System.IO.File]::WriteAllText($manifestPath, $expectedForWrite, $utf8)
    Write-Host "Updated spec manifest: $manifestPath"
    return
}

$actual = Normalize-LineEndings -Text (Get-Content -Raw -LiteralPath $manifestPath)
if ($actual -ne $expected) {
    Write-Error "Spec manifest is stale. Run ./eng/scripts/check-spec-manifest.ps1 -Update"
}

Write-Host "Spec manifest check passed. Files: $($entries.Count)"
