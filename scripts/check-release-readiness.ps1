param(
    [switch] $RequireComplete
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$checklists = @(
    Join-Path $root "docs/Specs/Initial/safe-ir-sandbox-spec/checklists/release-readiness.md"
    Join-Path $root "docs/Specs/Initial/safe-ir-sandbox-spec/checklists/security-review.md"
)

$items = @()
foreach ($checklist in $checklists) {
    if (-not (Test-Path -LiteralPath $checklist)) {
        throw "Missing checklist: $checklist"
    }

    $lines = Get-Content -LiteralPath $checklist
    if (-not ($lines | Where-Object { $_ -match '^- \[[ xX]\] ' })) {
        throw "Checklist has no task items: $checklist"
    }

    $section = ""
    $gate = "required"
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match '^##\s+(.+)$') {
            $section = $matches[1]
            $gate = "required"
            continue
        }

        if ($line -match '^<!--\s*release-gate:\s*(required|inventory)\s*-->\s*$') {
            $gate = $matches[1]
            continue
        }

        if ($line -match '^- \[([ xX])\] (.+)$') {
            $items += [pscustomobject] @{
                File = $checklist
                Line = $i + 1
                Section = $section
                Required = $gate -eq "required"
                Complete = $matches[1] -match '[xX]'
                Text = $matches[2]
            }
        }
    }
}

$openItems = @($items | Where-Object { -not $_.Complete })
$requiredOpenItems = @($openItems | Where-Object { $_.Required })

if ($RequireComplete) {
    if ($requiredOpenItems.Count -gt 0) {
        $sample = $requiredOpenItems |
            Select-Object -First 10 |
            ForEach-Object { "$([System.IO.Path]::GetFileName($_.File)):$($_.Line) [$($_.Section)] $($_.Text)" }
        Write-Error "Release readiness requires all release-gated checklist items to be complete. Open required items: $($requiredOpenItems.Count). $($sample -join '; ')"
    }

    Write-Host "Release checklist gate passed. Open required items: 0. Open inventory items: $($openItems.Count)"
    return
}

Write-Host "Release checklist inventory completed. Open items: $($openItems.Count). Open required items: $($requiredOpenItems.Count)"
