param(
    [int] $WarnAt = 350,
    [int] $FailAt = 500,
    [int] $MaxFilesPerFolder = 15,
    [string] $Config = "tools/CodeEnforcer/code-enforcer.json"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "tools/CodeEnforcer/src/CodeEnforcer/CodeEnforcer.csproj"
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

& dotnet run --project $project -- @arguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
