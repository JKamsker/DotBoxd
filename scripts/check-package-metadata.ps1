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
$expectedPackageMetadata = @{
    "SafeIR.Core" = @{
        Description = "Core SafeIR model, policy, resource metering, diagnostics, and canonical hashing primitives."
        Tags = @("safe-ir", "core", "policy", "resources", "hashing")
    }
    "SafeIR.Validation" = @{
        Description = "SafeIR structural, type, effect, policy, and binding validation APIs."
        Tags = @("safe-ir", "validation", "type-checking", "policy")
    }
    "SafeIR.Runtime" = @{
        Description = "SafeIR safe host runtime bindings for files, time, random, logging, strings, and math."
        Tags = @("safe-ir", "runtime", "bindings", "files", "logging")
    }
    "SafeIR.Serialization.Json" = @{
        Description = "SafeIR JSON IR import and export helpers for the module envelope."
        Tags = @("safe-ir", "json", "serialization")
    }
    "SafeIR.Server.Abstractions" = @{
        Description = "SafeIR plugin-to-host contracts: plugin marker attribute, event kernel, hook context, message sink, and event adapter abstractions."
        Tags = @("safe-ir", "plugins", "contracts", "abstractions")
    }
    "SafeIR.Transport.Http" = @{
        Description = "SafeIR HTTP GET transport bindings, grant helpers, and pinned HTTP policy validation."
        Tags = @("safe-ir", "http", "transport", "network", "policy")
    }
    "SafeIR.Transport.Ipc.ShaRpc" = @{
        Description = "Preview SafeIR ShaRPC MessagePack IPC transport addon with named-pipe helpers."
        Tags = @("safe-ir", "ipc", "transport", "sharpc", "preview")
    }
    "SafeIR.Interpreter" = @{
        Description = "SafeIR interpreted execution backend for validated IR modules."
        Tags = @("safe-ir", "interpreter", "execution")
    }
    "SafeIR.Compiler" = @{
        Description = "SafeIR generated-runtime compiler backend and persistent compiled artifact cache."
        Tags = @("safe-ir", "compiler", "cache", "generated-runtime")
    }
    "SafeIR.Verifier" = @{
        Description = "SafeIR generated assembly verifier and artifact manifest models."
        Tags = @("safe-ir", "verifier", "assemblies", "manifests")
    }
    "SafeIR.Hosting" = @{
        Description = "SafeIR host orchestration APIs for import, preparation, execution, isolation, and runtime selection."
        Tags = @("safe-ir", "hosting", "orchestration", "isolation")
    }
    "SafeIR.PluginAnalyzer" = @{
        Description = "SafeIR plugin source analyzer and generator package for package-backed plugins."
        Tags = @("safe-ir", "plugins", "analyzer", "source-generator")
    }
    "SafeIR.Plugins" = @{
        Description = "SafeIR plugin manifest, installed kernel, hook, and message-binding APIs."
        Tags = @("safe-ir", "plugins", "hooks", "kernels")
    }
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

function AssertPackageEntryAllowlist($zip, [string] $id, [string] $readme, [string] $packageName) {
    $allowedExact = New-Object "System.Collections.Generic.HashSet[string]" ([StringComparer]::Ordinal)
    [void] $allowedExact.Add("[Content_Types].xml")
    [void] $allowedExact.Add("_rels/.rels")
    [void] $allowedExact.Add("$id.nuspec")
    [void] $allowedExact.Add($readme)
    [void] $allowedExact.Add(".signature.p7s")

    if ($id -eq "SafeIR.PluginAnalyzer") {
        [void] $allowedExact.Add("analyzers/dotnet/cs/SafeIR.PluginAnalyzer.dll")
        [void] $allowedExact.Add("analyzers/dotnet/cs/SafeIR.PluginAnalyzer.xml")
    } else {
        [void] $allowedExact.Add("lib/net10.0/$id.dll")
        [void] $allowedExact.Add("lib/net10.0/$id.xml")
    }

    # Embedded, machine-readable JSON ingestion schemas (CMP-0012) are also packed so consumers can
    # load the contract from the package. The module envelope ships with the purpose-agnostic
    # serialization package; the plugin-package envelope ships with the plugin package.
    if ($id -eq "SafeIR.Serialization.Json") {
        [void] $allowedExact.Add("schemas/v1/safe-ir-module.schema.json")
    }

    if ($id -eq "SafeIR.Plugins") {
        [void] $allowedExact.Add("schemas/v1/safe-ir-plugin-package.schema.json")
    }

    foreach ($entry in $zip.Entries) {
        $name = $entry.FullName
        if ($name.EndsWith("/", [StringComparison]::Ordinal)) {
            continue
        }

        if ($allowedExact.Contains($name)) {
            continue
        }

        if ($name.StartsWith("package/services/metadata/core-properties/", [StringComparison]::Ordinal) -and
            $name.EndsWith(".psmdcp", [StringComparison]::Ordinal)) {
            continue
        }

        throw "Package $packageName includes unexpected package entry '$name'."
    }
}

function IsAllowedPrereleaseDependency([string] $packageId, [string] $dependencyId, [string] $dependencyVersion) {
    return $false
}

function AssertPackageMetadata($metadata, [string] $id, [string] $packageName) {
    if (-not $expectedPackageMetadata.ContainsKey($id)) {
        throw "Package $packageName has no expected metadata inventory entry."
    }

    $expected = $expectedPackageMetadata[$id]
    $description = RequiredText $metadata "description" $packageName
    if ($description -ne $expected.Description) {
        throw "Package $packageName description must be package-specific. Expected '$($expected.Description)', found '$description'."
    }

    $tagsValue = RequiredText $metadata "tags" $packageName
    $tags = New-Object "System.Collections.Generic.HashSet[string]" ([StringComparer]::OrdinalIgnoreCase)
    foreach ($tag in [regex]::Split($tagsValue, '[;\s]+')) {
        $trimmed = $tag.Trim()
        if ($trimmed.Length -gt 0) {
            [void] $tags.Add($trimmed)
        }
    }

    foreach ($requiredTag in $expected.Tags) {
        if (-not $tags.Contains($requiredTag)) {
            throw "Package $packageName tags must include '$requiredTag'. Found '$tagsValue'."
        }
    }
}

function AssertReadmeGuidance($zip, [string] $readme, [string] $packageName) {
    $entry = $zip.Entries | Where-Object {
        $_.FullName.Equals($readme, [StringComparison]::Ordinal)
    } | Select-Object -First 1

    if ($null -eq $entry) {
        throw "Package $packageName is missing expected readme '$readme'."
    }

    $reader = New-Object System.IO.StreamReader($entry.Open())
    try {
        $content = $reader.ReadToEnd()
    } finally {
        $reader.Dispose()
    }

    $requiredReadmePatterns = @(
        "## Installing from NuGet",
        "dotnet add package SafeIR.Hosting",
        "dotnet add package SafeIR.Serialization.Json",
        "dotnet add package SafeIR.Transport.Http",
        "dotnet add package SafeIR.PluginAnalyzer",
        "dotnet add package SafeIR.Transport.Ipc.ShaRpc",
        "PluginPackageJsonSerializer"
    )
    foreach ($pattern in $requiredReadmePatterns) {
        if ($content.IndexOf($pattern, [StringComparison]::Ordinal) -lt 0) {
            throw "Package $packageName readme must include NuGet install guidance containing '$pattern'."
        }
    }
}

function AssertSymbolPackage(
    [System.IO.FileInfo] $package,
    [string] $id,
    [string] $version,
    [System.Collections.IDictionary] $symbolPackages) {
    if ($id -eq "SafeIR.PluginAnalyzer") {
        return
    }

    $symbolName = "$id.$version.snupkg"
    if (-not $symbolPackages.Contains($symbolName)) {
        throw "Package $($package.Name) must have matching symbol package '$symbolName'."
    }

    $symbolPackage = $symbolPackages[$symbolName]
    $zip = [System.IO.Compression.ZipFile]::OpenRead($symbolPackage.FullName)
    try {
        AssertZipEntry $zip "$id.nuspec" $symbolPackage.Name
        AssertZipEntry $zip "lib/net10.0/$id.pdb" $symbolPackage.Name
    } finally {
        $zip.Dispose()
    }
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
    "SafeIR.Plugins",
    "SafeIR.Server.Abstractions"
)

Add-Type -AssemblyName System.IO.Compression.FileSystem

$packages = Get-ChildItem -LiteralPath $fullPackageDirectory -Filter "*.nupkg" -File |
    Where-Object { $_.Name -notlike "*.symbols.nupkg" } |
    Sort-Object Name
$symbolPackages = @{}
foreach ($symbolPackage in Get-ChildItem -LiteralPath $fullPackageDirectory -Filter "*.snupkg" -File) {
    $symbolPackages[$symbolPackage.Name] = $symbolPackage
}

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
        AssertPackageMetadata $metadata $id $package.Name
        $readme = RequiredText $metadata "readme" $package.Name
        if ($readme -ne "README.md") {
            throw "Package $($package.Name) must use README.md as its package readme."
        }

        AssertZipEntry $zip $readme $package.Name
        AssertReadmeGuidance $zip $readme $package.Name

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
            AssertZipEntry $zip "analyzers/dotnet/cs/SafeIR.PluginAnalyzer.xml" $package.Name
            AssertNoZipEntryPrefix $zip "lib/" $package.Name
        } else {
            AssertZipEntry $zip "lib/net10.0/$id.dll" $package.Name
            AssertZipEntry $zip "lib/net10.0/$id.xml" $package.Name
        }

        AssertPackageEntryAllowlist $zip $id $readme $package.Name
        AssertSymbolPackage $package $id $version $symbolPackages

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
