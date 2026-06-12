param(
    [switch] $RequireComplete
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$checklists = @(
    Join-Path $root "docs/Specs/Initial/safe-ir-sandbox-spec/checklists/release-readiness.md"
    Join-Path $root "docs/Specs/Initial/safe-ir-sandbox-spec/checklists/security-review.md"
)

$openItems = @()
foreach ($checklist in $checklists) {
    if (-not (Test-Path -LiteralPath $checklist)) {
        throw "Missing checklist: $checklist"
    }

    $lines = Get-Content -LiteralPath $checklist
    if (-not ($lines | Where-Object { $_ -match '^- \[[ xX]\] ' })) {
        throw "Checklist has no task items: $checklist"
    }

    $openItems += $lines | Where-Object { $_ -match '^- \[ \] ' }
}

if ($RequireComplete -and $openItems.Count -gt 0) {
    Write-Error "Release readiness requires all checklist items to be complete. Open items: $($openItems.Count)"
}

Write-Host "Release checklist gate passed. Open items: $($openItems.Count)"
