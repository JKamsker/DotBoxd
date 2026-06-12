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

function Assert-Evidence([string] $ChecklistText, [string] $Path, [string[]] $Patterns = @()) {
    $fullPath = Join-Path $root $Path
    if (-not (Test-Path -LiteralPath $fullPath)) {
        throw "Release checklist item '$ChecklistText' is marked complete but evidence is missing: $Path"
    }

    $content = Get-Content -Raw -LiteralPath $fullPath
    foreach ($pattern in $Patterns) {
        if ($content -notmatch $pattern) {
            throw "Release checklist item '$ChecklistText' is marked complete but evidence '$Path' does not contain pattern '$pattern'."
        }
    }
}

function Assert-CompletedItemEvidence {
    $evidence = @(
        @{
            Text = "Verifier malicious fixtures pass."
            Path = "tests/SafeIR.Tests/VerifierAttackMatrixTests.cs"
            Patterns = @("HttpClient", "ProcessStart", "Calli")
        },
        @{
            Text = "Compiled/interpreted differential tests pass."
            Path = "tests/SafeIR.Tests/DifferentialFuzzTests.cs"
            Patterns = @("ExecutionMode\.Interpreted", "ExecutionMode\.Compiled")
        },
        @{
            Text = "Path traversal tests pass."
            Path = "tests/SafeIR.Tests/SafeFileSystemTests.cs"
            Patterns = @("\.\./secret\.txt", "config/\.\./\.\./secret\.txt")
        },
        @{
            Text = "Cache corruption tests pass."
            Path = "tests/SafeIR.Tests/CompiledMaterializationCacheTests.cs"
            Patterns = @("MutatesSecondArtifactCompiler", "AssemblyBytes")
        },
        @{
            Text = "Binding security checklist passes."
            Path = "tests/SafeIR.Tests/BindingRegistryHardeningTests.cs"
            Patterns = @("E-BINDING-AUDIT", "E-BINDING-GRANT", "E-BINDING-TYPE")
        }
    )

    foreach ($entry in $evidence) {
        $item = $items | Where-Object { $_.Required -and $_.Text -eq $entry.Text } | Select-Object -First 1
        if ($null -ne $item -and $item.Complete) {
            Assert-Evidence $entry.Text $entry.Path $entry.Patterns
        }
    }
}

Assert-CompletedItemEvidence

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
