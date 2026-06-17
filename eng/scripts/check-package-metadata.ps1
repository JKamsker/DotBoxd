param(
    [string] $PackageDirectory = "artifacts/packages",
    [switch] $AllowPrereleaseVersions,
    [string[]] $AllowedPrereleasePackageIds = @(),
    [string] $ExpectedVersion = "",
    [string] $ExpectedRepositoryCommit = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
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
$expectedRepositoryUrl = "https://github.com/JKamsker/DotBoxD"
$expectedRepositoryType = "git"
$expectedPackageMetadata = @{
    "DotBoxD.Kernels" = @{
        Description = "Core DotBoxD.Kernels model, policy, resource metering, diagnostics, and canonical hashing primitives."
        Tags = @("dotboxd", "core", "policy", "resources", "hashing")
    }
    "DotBoxD.Kernels.Validation" = @{
        Description = "DotBoxD.Kernels structural, type, effect, policy, and binding validation APIs."
        Tags = @("dotboxd", "validation", "type-checking", "policy")
    }
    "DotBoxD.Kernels.Runtime" = @{
        Description = "DotBoxD.Kernels safe host runtime bindings for files, time, random, logging, strings, and math."
        Tags = @("dotboxd", "runtime", "bindings", "files", "logging")
    }
    "DotBoxD.Kernels.Serialization.Json" = @{
        Description = "DotBoxD.Kernels JSON IR import and export helpers for the module envelope."
        Tags = @("dotboxd", "json", "serialization")
    }
    "DotBoxD.Abstractions" = @{
        Description = "DotBoxD.Kernels plugin-to-host contracts: plugin marker attribute, event kernel, hook context, message sink, and event adapter abstractions."
        Tags = @("dotboxd", "plugins", "contracts", "abstractions")
    }
    "DotBoxD.Hosting.Http" = @{
        Description = "DotBoxD.Kernels HTTP GET transport bindings, grant helpers, and pinned HTTP policy validation."
        Tags = @("dotboxd", "http", "transport", "network", "policy")
    }
    "DotBoxD.Pushdown.Services" = @{
        Description = "Preview DotBoxD MessagePack IPC addon that runs sandboxed kernels next to host RPC services, with named-pipe helpers."
        Tags = @("dotboxd", "ipc", "transport", "pushdown", "preview")
    }
    "DotBoxD.Kernels.Interpreter" = @{
        Description = "DotBoxD.Kernels interpreted execution backend for validated IR modules."
        Tags = @("dotboxd", "interpreter", "execution")
    }
    "DotBoxD.Kernels.Compiler" = @{
        Description = "DotBoxD.Kernels generated-runtime compiler backend and persistent compiled artifact cache."
        Tags = @("dotboxd", "compiler", "cache", "generated-runtime")
    }
    "DotBoxD.Kernels.Verifier" = @{
        Description = "DotBoxD.Kernels generated assembly verifier and artifact manifest models."
        Tags = @("dotboxd", "verifier", "assemblies", "manifests")
    }
    "DotBoxD.Hosting" = @{
        Description = "DotBoxD.Kernels host orchestration APIs for import, preparation, execution, isolation, and runtime selection."
        Tags = @("dotboxd", "hosting", "orchestration", "isolation")
    }
    "DotBoxD.Plugins.Analyzer" = @{
        Description = "DotBoxD.Kernels plugin source analyzer and generator package for package-backed plugins."
        Tags = @("dotboxd", "plugins", "analyzer", "source-generator")
    }
    "DotBoxD.Plugins" = @{
        Description = "DotBoxD.Kernels plugin manifest, installed kernel, hook, and message-binding APIs."
        Tags = @("dotboxd", "plugins", "hooks", "kernels")
    }
    "DotBoxD" = @{
        Description = "DotBoxD meta-package: pulls in the full stack across all three usage modes — Services (source-generated RPC), Kernels (validated sandboxed logic hosted via DotBoxD.Hosting), and Pushdown (running kernels next to host services). Reference this single package to get the complete DotBoxD surface for a net10.0 host."
        Tags = @("dotboxd", "rpc", "sandbox", "kernels", "pushdown", "meta")
        Tfm = "net10.0"
    }
    "DotBoxD.Services.All" = @{
        Description = "DotBoxD service/channels bundle: the source-generated RPC core (DotBoxD.Services) together with the MessagePack codec and the TCP and named-pipe transports. Targets netstandard2.1 and is Unity/IL2CPP compatible for service-only consumers that do not need the Kernels or Pushdown stack."
        Tags = @("dotboxd", "rpc", "services", "channels", "transport", "messagepack", "unity", "il2cpp", "meta")
        Tfm = "netstandard2.1"
    }
    "DotBoxD.Services" = @{
        Description = "High-performance, transport-agnostic RPC framework for C#. Bundles the runtime core and the source generator (compile-time client proxies and server dispatchers)."
        Tags = @("dotboxd", "rpc", "services", "source-generator", "transport")
        Tfm = "netstandard2.1"
        ExtraEntries = @("analyzers/dotnet/cs/DotBoxD.Services.SourceGenerator.dll")
    }
    "DotBoxD.Transports.Tcp" = @{
        Description = "TCP transport implementation for DotBoxD"
        Tags = @("dotboxd", "transport")
        Tfm = "netstandard2.1"
    }
    "DotBoxD.Codecs.MessagePack" = @{
        Description = "MessagePack serializer implementation for DotBoxD"
        Tags = @("dotboxd", "serialization")
        Tfm = "netstandard2.1"
    }
    "DotBoxD.Transports.NamedPipes" = @{
        Description = "Named pipe transport implementation for DotBoxD process-boundary IPC."
        Tags = @("dotboxd", "transport")
        Tfm = "netstandard2.1"
        Readme = "named-pipe-transport.md"
        ExtraEntries = @("README.md")
        SkipReadmeGuidance = $true
    }
}

function GetPackageStructuralFacts([string] $id) {
    $facts = @{
        Tfm = "net10.0"
        Readme = "README.md"
        ExtraEntries = @()
        SkipReadmeGuidance = $false
    }

    if ($expectedPackageMetadata.ContainsKey($id)) {
        $entry = $expectedPackageMetadata[$id]
        if ($entry.ContainsKey("Tfm") -and -not [string]::IsNullOrWhiteSpace([string] $entry.Tfm)) {
            $facts.Tfm = [string] $entry.Tfm
        }
        if ($entry.ContainsKey("Readme") -and -not [string]::IsNullOrWhiteSpace([string] $entry.Readme)) {
            $facts.Readme = [string] $entry.Readme
        }
        if ($entry.ContainsKey("ExtraEntries") -and $null -ne $entry.ExtraEntries) {
            $facts.ExtraEntries = [string[]] $entry.ExtraEntries
        }
        if ($entry.ContainsKey("SkipReadmeGuidance")) {
            $facts.SkipReadmeGuidance = [bool] $entry.SkipReadmeGuidance
        }
    }

    return $facts
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

function AssertPackageEntryAllowlist($zip, [string] $id, [string] $readme, [string] $tfm, [string[]] $extraEntries, [string] $packageName) {
    $allowedExact = New-Object "System.Collections.Generic.HashSet[string]" ([StringComparer]::Ordinal)
    [void] $allowedExact.Add("[Content_Types].xml")
    [void] $allowedExact.Add("_rels/.rels")
    [void] $allowedExact.Add("$id.nuspec")
    [void] $allowedExact.Add($readme)
    [void] $allowedExact.Add(".signature.p7s")

    foreach ($extra in $extraEntries) {
        if (-not [string]::IsNullOrWhiteSpace($extra)) {
            [void] $allowedExact.Add($extra)
        }
    }

    if ($id -eq "DotBoxD.Plugins.Analyzer") {
        [void] $allowedExact.Add("analyzers/dotnet/cs/DotBoxD.Plugins.Analyzer.dll")
        [void] $allowedExact.Add("analyzers/dotnet/cs/DotBoxD.Plugins.Analyzer.xml")
    } else {
        [void] $allowedExact.Add("lib/$tfm/$id.dll")
        [void] $allowedExact.Add("lib/$tfm/$id.xml")
        if ($id -eq "DotBoxD") {
            [void] $allowedExact.Add("analyzers/dotnet/cs/DotBoxD.Plugins.Analyzer.dll")
            [void] $allowedExact.Add("analyzers/dotnet/cs/DotBoxD.Plugins.Analyzer.xml")
        }
    }

    # Embedded, machine-readable JSON ingestion schemas (CMP-0012) are also packed so consumers can
    # load the contract from the package. The module envelope ships with the purpose-agnostic
    # serialization package; the plugin-package envelope ships with the plugin package.
    if ($id -eq "DotBoxD.Kernels.Serialization.Json") {
        [void] $allowedExact.Add("schemas/v1/dotboxd-kernel-module.schema.json")
    }

    if ($id -eq "DotBoxD.Plugins") {
        [void] $allowedExact.Add("schemas/v1/dotboxd-plugin-package.schema.json")
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
        "dotnet add package DotBoxD.Hosting",
        "dotnet add package DotBoxD.Kernels.Serialization.Json",
        "dotnet add package DotBoxD.Hosting.Http",
        "dotnet add package DotBoxD.Plugins.Analyzer",
        "dotnet add package DotBoxD.Pushdown.Services",
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
    [string] $tfm,
    [System.Collections.IDictionary] $symbolPackages) {
    if ($id -eq "DotBoxD.Plugins.Analyzer") {
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
        AssertZipEntry $zip "lib/$tfm/$id.pdb" $symbolPackage.Name
    } finally {
        $zip.Dispose()
    }
}

$expectedIds = [string[]] @(
    "DotBoxD.Kernels",
    "DotBoxD.Kernels.Validation",
    "DotBoxD.Kernels.Runtime",
    "DotBoxD.Kernels.Serialization.Json",
    "DotBoxD.Hosting.Http",
    "DotBoxD.Pushdown.Services",
    "DotBoxD.Kernels.Interpreter",
    "DotBoxD.Kernels.Compiler",
    "DotBoxD.Kernels.Verifier",
    "DotBoxD.Hosting",
    "DotBoxD.Plugins.Analyzer",
    "DotBoxD.Plugins",
    "DotBoxD.Abstractions",
    "DotBoxD",
    "DotBoxD.Services.All",
    "DotBoxD.Services",
    "DotBoxD.Transports.Tcp",
    "DotBoxD.Codecs.MessagePack",
    "DotBoxD.Transports.NamedPipes"
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

        $structuralFacts = GetPackageStructuralFacts $id
        $expectedReadme = [string] $structuralFacts.Readme
        $packageTfm = [string] $structuralFacts.Tfm
        $extraEntries = [string[]] $structuralFacts.ExtraEntries
        $skipReadmeGuidance = [bool] $structuralFacts.SkipReadmeGuidance

        $readme = RequiredText $metadata "readme" $package.Name
        if ($readme -ne $expectedReadme) {
            throw "Package $($package.Name) must use $expectedReadme as its package readme."
        }

        AssertZipEntry $zip $readme $package.Name
        if (-not $skipReadmeGuidance) {
            AssertReadmeGuidance $zip $readme $package.Name
        }

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

        if ($id -eq "DotBoxD.Plugins.Analyzer") {
            AssertZipEntry $zip "analyzers/dotnet/cs/DotBoxD.Plugins.Analyzer.dll" $package.Name
            AssertZipEntry $zip "analyzers/dotnet/cs/DotBoxD.Plugins.Analyzer.xml" $package.Name
            AssertNoZipEntryPrefix $zip "lib/" $package.Name
        } else {
            AssertZipEntry $zip "lib/$packageTfm/$id.dll" $package.Name
            AssertZipEntry $zip "lib/$packageTfm/$id.xml" $package.Name
        }

        AssertPackageEntryAllowlist $zip $id $readme $packageTfm $extraEntries $package.Name
        AssertSymbolPackage $package $id $version $packageTfm $symbolPackages

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
