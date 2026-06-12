param(
    [Parameter(Mandatory = $true)]
    [string] $Project,
    [string] $Configuration = "Release",
    [switch] $NoBuild,
    [Parameter(Mandatory = $true)]
    [string[]] $RequiredFullyQualifiedNameContains
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$projectPath = if ([System.IO.Path]::IsPathRooted($Project)) {
    $Project
} else {
    Join-Path $root $Project
}

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "Test project does not exist: $projectPath"
}

$requiredNames = @($RequiredFullyQualifiedNameContains | Where-Object {
    -not [string]::IsNullOrWhiteSpace($_)
})
if ($requiredNames.Count -eq 0) {
    throw "At least one required test name must be provided."
}

$resultsDirectory = Join-Path $root "artifacts/test-results/required-tests"
New-Item -ItemType Directory -Force -Path $resultsDirectory | Out-Null
$trxFileName = "required-tests-" + [Guid]::NewGuid().ToString("N") + ".trx"
$filter = ($requiredNames | ForEach-Object { "FullyQualifiedName~$_" }) -join "|"

$arguments = @(
    "test", $projectPath,
    "--configuration", $Configuration,
    "--logger", "trx;LogFileName=$trxFileName",
    "--results-directory", $resultsDirectory,
    "--filter", $filter
)

if ($NoBuild) {
    $arguments += "--no-build"
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet test failed with exit code $LASTEXITCODE."
}

$trxPath = Join-Path $resultsDirectory $trxFileName
if (-not (Test-Path -LiteralPath $trxPath)) {
    throw "dotnet test did not produce the expected TRX file: $trxPath"
}

[xml] $trx = Get-Content -Raw -LiteralPath $trxPath
$definitionsById = @{}
foreach ($definition in $trx.SelectNodes("//*[local-name()='UnitTest']")) {
    $method = $definition.SelectSingleNode("*[local-name()='TestMethod']")
    $className = if ($null -ne $method) { [string] $method.className } else { "" }
    $methodName = if ($null -ne $method) { [string] $method.name } else { "" }
    $definitionsById[[string] $definition.id] = [pscustomobject] @{
        DisplayName = [string] $definition.name
        FullyQualifiedName = ($className + "." + $methodName).Trim(".")
    }
}

$executed = @()
foreach ($result in $trx.SelectNodes("//*[local-name()='UnitTestResult']")) {
    if ([string] $result.outcome -eq "NotExecuted") {
        continue
    }

    $definition = $definitionsById[[string] $result.testId]
    if ($null -eq $definition) {
        $definition = [pscustomobject] @{
            DisplayName = [string] $result.testName
            FullyQualifiedName = [string] $result.testName
        }
    }

    $executed += $definition
}

if ($executed.Count -eq 0) {
    throw "The required test filter matched zero executed tests."
}

$missing = @()
foreach ($requiredName in $requiredNames) {
    $matches = @($executed | Where-Object {
        $_.FullyQualifiedName.IndexOf($requiredName, [StringComparison]::Ordinal) -ge 0 -or
        $_.DisplayName.IndexOf($requiredName, [StringComparison]::Ordinal) -ge 0
    })
    if ($matches.Count -eq 0) {
        $missing += $requiredName
    }
}

if ($missing.Count -gt 0) {
    throw "Required test filter did not execute expected test groups: $($missing -join ', ')"
}

Write-Host "Required test gate passed. Executed tests: $($executed.Count). Required groups: $($requiredNames.Count)."
