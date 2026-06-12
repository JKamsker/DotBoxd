param(
    [string] $PackageDirectory = "artifacts/packages",
    [switch] $AllowPrereleaseVersions,
    [string[]] $AllowedPrereleasePackageIds = @(),
    [string] $ExpectedVersion = ""
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

$normalizedExpectedVersion = $ExpectedVersion.Trim()
if ($normalizedExpectedVersion.StartsWith("v", [StringComparison]::OrdinalIgnoreCase)) {
    $normalizedExpectedVersion = $normalizedExpectedVersion.Substring(1)
}

$allowedPrereleaseIds = New-Object "System.Collections.Generic.HashSet[string]" ([StringComparer]::Ordinal)
foreach ($allowedId in $AllowedPrereleasePackageIds) {
    if (-not [string]::IsNullOrWhiteSpace($allowedId)) {
        [void] $allowedPrereleaseIds.Add($allowedId)
    }
}

function RequiredText($metadata, [string] $name, [string] $packageName) {
    $node = $metadata.SelectSingleNode("*[local-name()='$name']")
    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
        throw "Package $packageName must declare '$name'."
    }

    return $node.InnerText
}

function IsPrereleaseVersion([string] $version) {
    return $version.Contains("-", [StringComparison]::Ordinal)
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
        $allowPackagePrerelease = $AllowPrereleaseVersions -or $allowedPrereleaseIds.Contains($id)
        if (-not $allowPackagePrerelease -and (IsPrereleaseVersion $version)) {
            throw "Package $($package.Name) has prerelease version '$version'. Stable release metadata is required."
        }

        if (-not [string]::IsNullOrWhiteSpace($normalizedExpectedVersion)) {
            if ($allowedPrereleaseIds.Contains($id)) {
                if (-not $version.StartsWith($normalizedExpectedVersion + "-", [StringComparison]::Ordinal)) {
                    throw "Package $($package.Name) version '$version' must use tag version '$normalizedExpectedVersion' as its prefix."
                }
            } elseif ($version -ne $normalizedExpectedVersion) {
                throw "Package $($package.Name) version '$version' does not match tag version '$normalizedExpectedVersion'."
            }
        }

        [void] (RequiredText $metadata "authors" $package.Name)
        [void] (RequiredText $metadata "description" $package.Name)
        [void] (RequiredText $metadata "readme" $package.Name)
        $license = $metadata.SelectSingleNode("*[local-name()='license']")
        if ($null -eq $license -or [string]::IsNullOrWhiteSpace($license.InnerText)) {
            throw "Package $($package.Name) must declare a license expression or file."
        }

        if ($license.InnerText -eq "MIT" -and -not (Test-Path -LiteralPath (Join-Path $root "LICENSE"))) {
            throw "Package $($package.Name) declares MIT but the repository has no LICENSE file."
        }

        $repository = $metadata.SelectSingleNode("*[local-name()='repository']")
        if ($null -eq $repository -or [string]::IsNullOrWhiteSpace($repository.url)) {
            throw "Package $($package.Name) must declare a repository URL."
        }

        if (-not $allowPackagePrerelease) {
            $dependencies = $metadata.SelectNodes(".//*[local-name()='dependency']")
            foreach ($dependency in $dependencies) {
                $dependencyId = $dependency.id
                $dependencyVersion = $dependency.version
                if (-not [string]::IsNullOrWhiteSpace($dependencyVersion) -and
                    (IsPrereleaseVersion $dependencyVersion)) {
                    throw "Stable package $id depends on prerelease package $dependencyId $dependencyVersion."
                }
            }
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
