param(
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$addendumExample = Join-Path $root "examples/Addendum/SafeIR.AddendumExamples/SafeIR.AddendumExamples.csproj"

& dotnet run --project $addendumExample --configuration $Configuration --no-build
if ($LASTEXITCODE -ne 0) {
    throw "Addendum example smoke test failed with exit code $LASTEXITCODE"
}

Write-Host "Docs/example smoke checks passed."
