param(
    [int] $WarnAt,
    [int] $FailAt,
    [int] $MaxFilesPerFolder,
    [int] $MaxFilesInProjectFolder
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$toolPackage = "CodeEnforcer"
$toolVersion = "0.1.0-ci.8"
$toolManifest = Join-Path $root ".config/dotnet-tools.json"
$arguments = @(
    "--root", $root
)

if ($PSBoundParameters.ContainsKey("WarnAt")) {
    $arguments += @("--soft-line-limit", $WarnAt)
}

if ($PSBoundParameters.ContainsKey("FailAt")) {
    $arguments += @("--hard-line-limit", $FailAt)
}

if ($PSBoundParameters.ContainsKey("MaxFilesPerFolder")) {
    $arguments += @("--max-files-per-folder", $MaxFilesPerFolder)
}

if ($PSBoundParameters.ContainsKey("MaxFilesInProjectFolder")) {
    $arguments += @("--max-files-per-root-dir", $MaxFilesInProjectFolder)
}

Push-Location $root
try {
    if (-not (Test-Path -LiteralPath $toolManifest)) {
        & dotnet new tool-manifest
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }

    $toolList = & dotnet tool list --local
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    if (-not ($toolList -match "(?im)^\s*codeenforcer\s+")) {
        Write-Host "Installing local CodeEnforcer tool $toolVersion..."
        & dotnet tool install $toolPackage --version $toolVersion --local
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }

    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    & dotnet tool run code-enforcer -- @arguments
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}
