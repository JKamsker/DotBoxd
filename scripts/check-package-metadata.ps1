param(
    [string] $PackageDirectory = "artifacts/packages",
    [switch] $AllowPrereleaseVersions
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$fullPackageDirectory = if ([System.IO.Path]::IsPathRooted($PackageDirectory)) {
    $PackageDirectory
} else {
    Join-Path $root $PackageDirectory
}

if (-not (Test-Path -LiteralPath $fullPackageDirectory)) {
    throw "Package directory does not exist: $fullPackageDirectory"
}

function RequiredText($metadata, [string] $name, [string] $packageName) {
    $node = $metadata.SelectSingleNode("*[local-name()='$name']")
    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
        throw "Package $packageName must declare '$name'."
    }

    return $node.InnerText
}

$expectedIds = [string[]] @(
    "SafeIR.Core",
    "SafeIR.Validation",
    "SafeIR.Runtime",
    "SafeIR.Serialization.Json",
    "SafeIR.Transport.Http",
    "SafeIR.Transport.Ipc.ShaRpc",
    "SafeIR.Interpreter",
    "SafeIR.Compiler",
    "SafeIR.Verifier",
    "SafeIR.Hosting",
    "SafeIR.PluginAnalyzer",
    "SafeIR.Plugins"
)

Add-Type -AssemblyName System.IO.Compression.FileSystem

$packages = Get-ChildItem -LiteralPath $fullPackageDirectory -Filter "*.nupkg" -File |
    Where-Object { $_.Name -notlike "*.symbols.nupkg" } |
    Sort-Object Name

if ($packages.Count -eq 0) {
    throw "No .nupkg files found in $fullPackageDirectory"
}

$seen = New-Object "System.Collections.Generic.HashSet[string]" ([StringComparer]::Ordinal)
foreach ($package in $packages) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $nuspecEntry = $zip.Entries | Where-Object { $_.FullName.EndsWith(".nuspec", [StringComparison]::Ordinal) } | Select-Object -First 1
        if ($null -eq $nuspecEntry) {
            throw "Package $($package.Name) has no nuspec."
        }

        $reader = New-Object System.IO.StreamReader($nuspecEntry.Open())
        try {
            [xml] $nuspec = $reader.ReadToEnd()
        } finally {
            $reader.Dispose()
        }

        $metadata = $nuspec.DocumentElement.SelectSingleNode("*[local-name()='metadata']")
        if ($null -eq $metadata) {
            throw "Package $($package.Name) has no nuspec metadata."
        }

        $id = RequiredText $metadata "id" $package.Name
        if (-not $seen.Add($id)) {
            throw "Package id '$id' appears more than once."
        }

        if ($id -like "*.Tests" -or $id -like "*.Benchmarks" -or $id -like "*.Examples") {
            throw "Non-product package '$id' should not be packed."
        }

        $version = RequiredText $metadata "version" $package.Name
        if (-not $AllowPrereleaseVersions -and $version.Contains("-", [StringComparison]::Ordinal)) {
            throw "Package $($package.Name) has prerelease version '$version'. Stable release metadata is required."
        }

        [void] (RequiredText $metadata "authors" $package.Name)
        [void] (RequiredText $metadata "description" $package.Name)
        [void] (RequiredText $metadata "readme" $package.Name)

        $repository = $metadata.SelectSingleNode("*[local-name()='repository']")
        if ($null -eq $repository -or [string]::IsNullOrWhiteSpace($repository.url)) {
            throw "Package $($package.Name) must declare a repository URL."
        }
    } finally {
        $zip.Dispose()
    }
}

$missing = $expectedIds | Where-Object { -not $seen.Contains($_) }
if ($missing.Count -gt 0) {
    throw "Missing expected package ids: $($missing -join ', ')"
}

$unexpected = $seen | Where-Object { $_ -notin $expectedIds }
if ($unexpected.Count -gt 0) {
    throw "Unexpected package ids: $($unexpected -join ', ')"
}

Write-Host "Package metadata check passed. Packages: $($seen.Count)"
