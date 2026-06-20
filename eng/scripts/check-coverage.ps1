param(
    # Directory containing one or more *.cobertura.xml reports (searched recursively).
    [string] $CoverageDirectory = "artifacts/coverage",
    # Minimum required line coverage (percent) over the shipping assemblies.
    # -1 reads `minimumLineCoverage` from .config/code-enforcer/coverage.json.
    [double] $MinimumLineCoverage = -1
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "../..")
$coverageRoot = Join-Path $root $CoverageDirectory

$threshold = $MinimumLineCoverage
if ($threshold -lt 0) {
    $configPath = Join-Path $root ".config/code-enforcer/coverage.json"
    if (-not (Test-Path -LiteralPath $configPath)) {
        throw "No coverage threshold supplied and .config/code-enforcer/coverage.json is missing."
    }

    $config = Get-Content -Raw -LiteralPath $configPath | ConvertFrom-Json
    $threshold = [double] $config.minimumLineCoverage
}

$reports = @(Get-ChildItem -Path $coverageRoot -Recurse -Filter "*.cobertura.xml" -ErrorAction SilentlyContinue)
if ($reports.Count -eq 0) {
    throw "No Cobertura reports (*.cobertura.xml) found under $coverageRoot."
}

function Test-ShippingPackage([string] $name) {
    if (-not $name.StartsWith("DotBoxD", [System.StringComparison]::Ordinal)) {
        return $false
    }
    if ($name.EndsWith(".Tests", [System.StringComparison]::Ordinal)) {
        return $false
    }
    if ($name -like "*.Benchmarks") {
        return $false
    }

    return $true
}

function Test-ShippingSourceFile([string] $file) {
    if ([string]::IsNullOrEmpty($file)) {
        return $false
    }

    # The package name alone does not separate shipping code from test-only code: fixture and
    # contract projects such as tests/DotBoxD.Kernels.TestFixtures.* , tests/DotBoxD.Services.TestContracts,
    # and tests/DotBoxD.Services.Tests.GeneratedFixtures all carry the DotBoxD.* prefix and do not end in
    # ".Tests", so they slip past Test-ShippingPackage and would count against the src/ coverage floor.
    # Their sources live under tests/, never src/, so require the source file to sit under a src/ directory.
    # Cobertura emits either absolute paths (.../DotBoxD/src/...) or deterministic CI paths (/_/src/...);
    # both contain a src/ path segment once separators are normalized.
    $normalized = $file.Replace('\', '/')
    return $normalized -match '(^|/)src/'
}

# Merge across reports: union the set of covered (and valid) line numbers per source
# file, so a line counted by any test project counts once and nothing is double-counted.
$validLines = @{}
$coveredLines = @{}

foreach ($report in $reports) {
    [xml] $document = Get-Content -Raw -LiteralPath $report.FullName
    foreach ($package in @($document.coverage.packages.package)) {
        if ($null -eq $package -or -not (Test-ShippingPackage ([string] $package.name))) {
            continue
        }

        foreach ($class in @($package.classes.class)) {
            if ($null -eq $class) {
                continue
            }

            $file = [string] $class.filename
            if (-not (Test-ShippingSourceFile $file)) {
                continue
            }

            if (-not $validLines.ContainsKey($file)) {
                $validLines[$file] = [System.Collections.Generic.HashSet[int]]::new()
                $coveredLines[$file] = [System.Collections.Generic.HashSet[int]]::new()
            }

            foreach ($line in @($class.lines.line)) {
                if ($null -eq $line) {
                    continue
                }

                $number = [int] $line.number
                [void] $validLines[$file].Add($number)
                if ([int] $line.hits -gt 0) {
                    [void] $coveredLines[$file].Add($number)
                }
            }
        }
    }
}

$totalValid = 0
$totalCovered = 0
foreach ($file in $validLines.Keys) {
    $totalValid += $validLines[$file].Count
    $totalCovered += $coveredLines[$file].Count
}

if ($totalValid -eq 0) {
    throw "No shipping (DotBoxD.*) lines were found in the coverage reports."
}

$rate = [math]::Round(100.0 * $totalCovered / $totalValid, 2)
Write-Host "Line coverage over DotBoxD shipping assemblies: $rate% ($totalCovered / $totalValid lines). Required minimum: $threshold%."

if ($rate -lt $threshold) {
    throw "Coverage $rate% is below the required minimum of $threshold%."
}

Write-Host "Coverage gate passed."
