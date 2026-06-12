param(
    [string] $PackageDirectory = "artifacts/packages",
    [switch] $AllowPrereleaseVersions,
    [string[]] $AllowedPrereleasePackageIds = @(),
    [string] $ExpectedVersion = "",
    [string] $ExpectedRepositoryCommit = ""
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

$normalizedExpectedRepositoryCommit = $ExpectedRepositoryCommit.Trim()
if ([string]::IsNullOrWhiteSpace($normalizedExpectedRepositoryCommit)) {
    $gitHead = & git -C $root rev-parse HEAD 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($gitHead)) {
        throw "ExpectedRepositoryCommit was not provided and the current git commit could not be resolved."
    }

    $normalizedExpectedRepositoryCommit = ([string] $gitHead).Trim()
}

$allowedPrereleaseIds = New-Object "System.Collections.Generic.HashSet[string]" ([StringComparer]::Ordinal)
foreach ($allowedId in $AllowedPrereleasePackageIds) {
    if (-not [string]::IsNullOrWhiteSpace($allowedId)) {
        [void] $allowedPrereleaseIds.Add($allowedId)
    }
}

$expectedLicenseExpression = "MIT"
$expectedRepositoryUrl = "https://github.com/JKamsker/Safe-IR"
$expectedRepositoryType = "git"
$allowedPrereleaseDependenciesByPackage = @{
    "SafeIR.Transport.Ipc.ShaRpc" = [string[]] @(
        "ShaRPC",
        "ShaRPC.Serializers.MessagePack",
        "ShaRPC.Transports.NamedPipes"
    )
}

function IsHexString([string] $value, [int] $length) {
    if ($value.Length -ne $length) {
        return $false
    }

    foreach ($character in $value.ToCharArray()) {
        if (-not [Uri]::IsHexDigit($character)) {
            return $false
        }
    }

    return $true
}

if (-not (IsHexString $normalizedExpectedRepositoryCommit 40)) {
    throw "Expected repository commit must be a 40-character hexadecimal git object id."
}

function RequiredText($metadata, [string] $name, [string] $packageName) {
    $node = $metadata.SelectSingleNode("*[local-name()='$name']")
    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
        throw "Package $packageName must declare '$name'."
    }

    return $node.InnerText
}

function IsPrereleaseVersion([string] $version) {
    return $version.IndexOf("-", [StringComparison]::Ordinal) -ge 0
}

function AssertZipEntry($zip, [string] $entryName, [string] $packageName) {
    $entry = $zip.Entries | Where-Object {
        $_.FullName.Equals($entryName, [StringComparison]::Ordinal)
    } | Select-Object -First 1

    if ($null -eq $entry) {
        throw "Package $packageName is missing expected package entry '$entryName'."
    }
}

function AssertNoZipEntryPrefix($zip, [string] $prefix, [string] $packageName) {
    $entry = $zip.Entries | Where-Object {
        $_.FullName.StartsWith($prefix, [StringComparison]::Ordinal)
    } | Select-Object -First 1

    if ($null -ne $entry) {
        throw "Package $packageName must not include entries under '$prefix'. Found '$($entry.FullName)'."
    }
}

function IsAllowedPrereleaseDependency([string] $packageId, [string] $dependencyId, [string] $dependencyVersion) {
    if (-not $allowedPrereleaseDependenciesByPackage.ContainsKey($packageId)) {
        return $false
    }

    $allowedDependencyIds = $allowedPrereleaseDependenciesByPackage[$packageId]
    if ($dependencyId -notin $allowedDependencyIds) {
        return $false
    }

    return $dependencyVersion.StartsWith("1.0.0-ci.", [StringComparison]::Ordinal)
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
        $readme = RequiredText $metadata "readme" $package.Name
        if ($readme -ne "README.md") {
            throw "Package $($package.Name) must use README.md as its package readme."
        }

        AssertZipEntry $zip $readme $package.Name

        $license = $metadata.SelectSingleNode("*[local-name()='license']")
        if ($null -eq $license -or
            [string] $license.type -ne "expression" -or
            $license.InnerText -ne $expectedLicenseExpression) {
            throw "Package $($package.Name) must declare exact license expression '$expectedLicenseExpression'."
        }

        if (-not (Test-Path -LiteralPath (Join-Path $root "LICENSE"))) {
            throw "Package $($package.Name) declares MIT but the repository has no LICENSE file."
        }

        $repository = $metadata.SelectSingleNode("*[local-name()='repository']")
        if ($null -eq $repository -or
            [string] $repository.url -ne $expectedRepositoryUrl -or
            [string] $repository.type -ne $expectedRepositoryType) {
            throw "Package $($package.Name) must declare repository $expectedRepositoryType $expectedRepositoryUrl."
        }

        if ([string] $repository.commit -ne $normalizedExpectedRepositoryCommit) {
            throw "Package $($package.Name) repository commit '$([string] $repository.commit)' does not match current commit '$normalizedExpectedRepositoryCommit'."
        }

        if ($id -eq "SafeIR.PluginAnalyzer") {
            AssertZipEntry $zip "analyzers/dotnet/cs/SafeIR.PluginAnalyzer.dll" $package.Name
            AssertNoZipEntryPrefix $zip "lib/" $package.Name
        } else {
            AssertZipEntry $zip "lib/net10.0/$id.dll" $package.Name
        }

        $dependencies = $metadata.SelectNodes(".//*[local-name()='dependency']")
        foreach ($dependency in $dependencies) {
            $dependencyId = [string] $dependency.id
            $dependencyVersion = [string] $dependency.version
            if ([string]::IsNullOrWhiteSpace($dependencyVersion) -or
                -not (IsPrereleaseVersion $dependencyVersion)) {
                continue
            }

            if ($AllowPrereleaseVersions) {
                continue
            }

            if (IsAllowedPrereleaseDependency $id $dependencyId $dependencyVersion) {
                continue
            }

            throw "Stable package $id depends on unapproved prerelease package $dependencyId $dependencyVersion."
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
