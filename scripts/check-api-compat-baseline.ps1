param(
    [string] $BaselineDirectory = "docs/api-baselines",
    [switch] $Update
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$baselineRoot = if ([System.IO.Path]::IsPathRooted($BaselineDirectory)) {
    $BaselineDirectory
} else {
    Join-Path $root $BaselineDirectory
}

$packages = @(
    @{ Id = "SafeIR.Core"; Path = "src/SafeIR.Core" },
    @{ Id = "SafeIR.Validation"; Path = "src/SafeIR.Validation" },
    @{ Id = "SafeIR.Runtime"; Path = "src/SafeIR.Runtime" },
    @{ Id = "SafeIR.Serialization.Json"; Path = "src/SafeIR.Serialization.Json" },
    @{ Id = "SafeIR.Transport.Http"; Path = "src/SafeIR.Transport.Http" },
    @{ Id = "SafeIR.Transport.Ipc.ShaRpc"; Path = "src/SafeIR.Transport.Ipc.ShaRpc" },
    @{ Id = "SafeIR.Interpreter"; Path = "src/SafeIR.Interpreter" },
    @{ Id = "SafeIR.Compiler"; Path = "src/SafeIR.Compiler" },
    @{ Id = "SafeIR.Verifier"; Path = "src/SafeIR.Verifier" },
    @{ Id = "SafeIR.Hosting"; Path = "src/SafeIR.Hosting" },
    @{ Id = "SafeIR.PluginAnalyzer"; Path = "src/SafeIR.PluginAnalyzer" },
    @{ Id = "SafeIR.Plugins"; Path = "src/SafeIR.Plugins" }
)

function Normalize-ApiLine([string] $Line) {
    $trimmed = $Line.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed) -or
        $trimmed.StartsWith("//", [StringComparison]::Ordinal) -or
        $trimmed.StartsWith("[", [StringComparison]::Ordinal)) {
        return $null
    }

    if ($trimmed -notmatch "^(public|protected\s+internal|protected)\b") {
        return $null
    }

    if ($trimmed -match "^(public|protected)\s+(get|set|init)\b") {
        return $null
    }

    $normalized = $trimmed -replace "\s+", " "
    $normalized = $normalized.TrimEnd("{", ";").Trim()
    if ([string]::IsNullOrWhiteSpace($normalized)) {
        return $null
    }

    return $normalized
}

function Get-PackageApi([hashtable] $Package) {
    $packagePath = Join-Path $root $Package.Path
    if (-not (Test-Path -LiteralPath $packagePath)) {
        throw "Package source directory does not exist: $packagePath"
    }

    $api = New-Object "System.Collections.Generic.HashSet[string]" ([StringComparer]::Ordinal)
    $files = Get-ChildItem -LiteralPath $packagePath -Recurse -File -Filter "*.cs" |
        Where-Object {
            $_.FullName -notmatch "[\\/](bin|obj)[\\/]" -and
            $_.Name -notlike "*.g.cs"
        }

    foreach ($file in $files) {
        foreach ($line in Get-Content -LiteralPath $file.FullName) {
            $apiLine = Normalize-ApiLine $line
            if ($null -ne $apiLine) {
                [void] $api.Add($apiLine)
            }
        }
    }

    return @($api | Sort-Object)
}

function BaselinePath([string] $PackageId) {
    return Join-Path $baselineRoot "$PackageId.txt"
}

function Read-Baseline([string] $Path, [string] $PackageId) {
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Missing public API baseline for $PackageId at $Path. Run scripts/check-api-compat-baseline.ps1 -Update for an intentional baseline refresh."
    }

    return @(Get-Content -LiteralPath $Path |
        Where-Object {
            -not [string]::IsNullOrWhiteSpace($_) -and
            -not $_.TrimStart().StartsWith("#", [StringComparison]::Ordinal)
        })
}

function Write-Baseline([string] $Path, [string] $PackageId, [string[]] $Api) {
    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $content = @(
        "# SafeIR public API baseline",
        "# Package: $PackageId",
        "# Update intentionally with scripts/check-api-compat-baseline.ps1 -Update when approving public API changes.",
        ""
    ) + $Api
    Set-Content -LiteralPath $Path -Value $content
}

function Compare-Baseline([string] $PackageId, [string[]] $Expected, [string[]] $Actual) {
    $expectedSet = New-Object "System.Collections.Generic.HashSet[string]" ([StringComparer]::Ordinal)
    foreach ($item in $Expected) {
        [void] $expectedSet.Add($item)
    }

    $actualSet = New-Object "System.Collections.Generic.HashSet[string]" ([StringComparer]::Ordinal)
    foreach ($item in $Actual) {
        [void] $actualSet.Add($item)
    }

    $removed = @($Expected | Where-Object { -not $actualSet.Contains($_) })
    $added = @($Actual | Where-Object { -not $expectedSet.Contains($_) })
    if ($removed.Count -eq 0 -and $added.Count -eq 0) {
        return
    }

    $details = New-Object "System.Collections.Generic.List[string]"
    if ($removed.Count -gt 0) {
        $details.Add("Removed API:")
        foreach ($item in $removed) {
            $details.Add("  - $item")
        }
    }

    if ($added.Count -gt 0) {
        $details.Add("Added API:")
        foreach ($item in $added) {
            $details.Add("  + $item")
        }
    }

    throw "Public API baseline mismatch for $PackageId.`n$($details -join [Environment]::NewLine)`nIf this is intentional, update the baseline and document the versioning decision."
}

if ($Update) {
    foreach ($package in $packages) {
        $api = Get-PackageApi $package
        Write-Baseline (BaselinePath $package.Id) $package.Id $api
    }

    Write-Host "Public API baselines updated. Packages: $($packages.Count)"
    return
}

foreach ($package in $packages) {
    $actual = Get-PackageApi $package
    $expected = Read-Baseline (BaselinePath $package.Id) $package.Id
    Compare-Baseline $package.Id $expected $actual
}

Write-Host "Public API baseline check passed. Packages: $($packages.Count)"
