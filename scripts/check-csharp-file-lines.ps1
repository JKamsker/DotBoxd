param(
    [int] $WarnAt = 350,
    [int] $FailAt = 500,
    [int] $MaxFilesPerFolder = 15,
    [string] $Config = "tools/CodeEnforcer/code-enforcer.json"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "tools/CodeEnforcer/src/CodeEnforcer/CodeEnforcer.csproj"
$targetFramework = "net10.0"
$configuration = "Release"
$assembly = Join-Path $root "tools/CodeEnforcer/src/CodeEnforcer/bin/$configuration/$targetFramework/CodeEnforcer.dll"
$configPath = if ([System.IO.Path]::IsPathRooted($Config)) {
    $Config
} else {
    Join-Path $root $Config
}

$arguments = @(
    "--root", $root,
    "--config", $configPath,
    "--soft-line-limit", $WarnAt,
    "--hard-line-limit", $FailAt,
    "--max-files-per-folder", $MaxFilesPerFolder
)

if (-not (Test-Path -LiteralPath $assembly)) {
    Write-Host "CodeEnforcer is not compiled. Building $configuration..."
    & dotnet build $project --configuration $configuration
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

& dotnet $assembly @arguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
