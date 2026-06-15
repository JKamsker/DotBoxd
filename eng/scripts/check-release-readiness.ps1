param(
    [switch] $RequireComplete
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$checklists = @(
    Join-Path $root "docs/Specs/Initial/dotboxd-sandbox-spec/checklists/release-readiness.md"
    Join-Path $root "docs/Specs/Initial/dotboxd-sandbox-spec/checklists/security-review.md"
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
            $completeMarker = $matches[1]
            $text = $matches[2]
            $items += [pscustomobject] @{
                File = $checklist
                Line = $i + 1
                Section = $section
                Required = $gate -eq "required"
                Complete = $completeMarker -match '[xX]'
                Text = $text
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
    $releaseReadiness = Join-Path $root "docs/Specs/Initial/dotboxd-sandbox-spec/checklists/release-readiness.md"
    $securityReview = Join-Path $root "docs/Specs/Initial/dotboxd-sandbox-spec/checklists/security-review.md"
    $releaseEvidence = @(
        @{
            Text = "Restricted IR implemented."
            Path = "src/Kernels/DotBoxD.Kernels/ModuleModel.cs"
            Patterns = @("SandboxModule", "Expression")
        },
        @{
            Text = "Canonical hashing implemented."
            Path = "src/Kernels/DotBoxD.Kernels/Model/CanonicalModuleHasher.cs"
            Patterns = @("Hash", "Serialize")
        },
        @{
            Text = "Type checker implemented."
            Path = "src/Kernels/DotBoxD.Kernels.Validation/FunctionAnalyzer.cs"
            Patterns = @("Analyze", "SandboxType")
        },
        @{
            Text = "Effect analyzer implemented."
            Path = "src/Kernels/DotBoxD.Kernels.Validation/FunctionAnalyzer.cs"
            Patterns = @("Effects", "Binding")
        },
        @{
            Text = "Capability policy implemented."
            Path = "src/Kernels/DotBoxD.Kernels/Policy.cs"
            Patterns = @("CapabilityGrant", "SandboxPolicy")
        },
        @{
            Text = "Binding registry validation implemented."
            Path = "src/Kernels/DotBoxD.Kernels/Bindings/BindingRegistryValidator.cs"
            Patterns = @("Validate", "BindingDescriptor")
        },
        @{
            Text = "Interpreted mode implemented."
            Path = "src/Kernels/DotBoxD.Kernels.Interpreter/SandboxInterpreter.cs"
            Patterns = @("ExecuteAsync", "ExecuteEntrypointAsync")
        },
        @{
            Text = "Fuel limits implemented."
            Path = "src/Kernels/DotBoxD.Kernels/Model/Resources.cs"
            Patterns = @("ChargeFuel", "MaxFuel")
        },
        @{
            Text = "Safe error model implemented."
            Path = "src/Kernels/DotBoxD.Kernels/Model/Diagnostics.cs"
            Patterns = @("SandboxError", "SafeMessage")
        },
        @{
            Text = "Basic audit implemented."
            Path = "src/Kernels/DotBoxD.Kernels/Bindings/Audit.cs"
            Patterns = @("SandboxAuditEvent", "IAuditSink")
        },
        @{
            Text = "At least one safe file binding implemented and tested."
            Path = "tests/DotBoxD.Kernels.Tests/Runtime/File/SafeFileSystemTests.cs"
            Patterns = @("Granted_file_read", "file.readText")
        },
        @{
            Text = "Path traversal tests pass."
            Path = "tests/DotBoxD.Kernels.Tests/Runtime/File/SafeFileSystemTests.cs"
            Patterns = @("\.\./secret\.txt", "config/\.\./\.\./secret\.txt")
        },
        @{
            Text = "Binding security checklist passes."
            Path = "tests/DotBoxD.Kernels.Tests/Bindings/BindingRegistryHardeningTests.cs"
            Patterns = @("E-BINDING-AUDIT", "E-BINDING-GRANT", "E-BINDING-TYPE")
        },
        @{
            Text = "Compiler emits valid managed assemblies."
            Path = "src/Kernels/DotBoxD.Kernels.Compiler/Emitters/ReflectionEmitSandboxCompiler.cs"
            Patterns = @("AssemblyBuilder", "CompileAsync")
        },
        @{
            Text = "Generated assemblies use runtime stubs only."
            Path = "src/Kernels/DotBoxD.Kernels.Compiler/Emitters/ExpressionEmitter.cs"
            Patterns = @("CompiledRuntime", "EmitCall")
        },
        @{
            Text = "Verifier implemented."
            Path = "src/Kernels/DotBoxD.Kernels.Verifier/Generated/GeneratedAssemblyVerifier.cs"
            Patterns = @("VerifyAsync", "VerificationResult")
        },
        @{
            Text = "Verifier malicious fixtures pass."
            Path = "tests/DotBoxD.Kernels.Tests/Verifier/Core/VerifierAttackMatrixTests.cs"
            Patterns = @("HttpClient", "ProcessStart", "Calli")
        },
        @{
            Text = "Compiled/interpreted differential tests pass."
            Path = "tests/DotBoxD.Kernels.Tests/Fuzz/DifferentialFuzzTests.cs"
            Patterns = @("ExecutionMode\.Interpreted", "ExecutionMode\.Compiled")
        },
        @{
            Text = "Path traversal tests pass."
            Path = "tests/DotBoxD.Kernels.Tests/Runtime/File/SafeFileSystemTests.cs"
            Patterns = @("\.\./secret\.txt", "config/\.\./\.\./secret\.txt")
        },
        @{
            Text = "DLL cache manifest implemented."
            Path = "src/Kernels/DotBoxD.Kernels.Verifier/Generated/VerificationModels.cs"
            Patterns = @("ArtifactManifest", "CacheKey")
        },
        @{
            Text = "Cache invalidation tests pass."
            Path = "tests/DotBoxD.Kernels.Tests/Compiled/Core/CompiledCacheMetadataTests.cs"
            Patterns = @("CacheKey", "quarantined_and_recompiled")
        },
        @{
            Text = "Cache corruption tests pass."
            Path = "tests/DotBoxD.Kernels.Tests/Compiled/Core/CompiledMaterializationCacheTests.cs"
            Patterns = @("MutatesSecondArtifactCompiler", "AssemblyBytes")
        },
        @{
            Text = '`AssemblyLoadContext` lifecycle tested.'
            Path = "tests/DotBoxD.Kernels.Tests/Compiled/Core/CompiledMaterializationCacheTests.cs"
            Patterns = @("WeakReference", "AssemblyLoadContext")
        },
        @{
            Text = "Fallback behavior documented."
            Path = "docs/Specs/Initial/dotboxd-sandbox-spec/spec/16-public-api.md"
            Patterns = @("Fallback behavior", "AllowFallbackToInterpreter")
        }
    )

    foreach ($entry in $releaseEvidence) {
        $item = $items | Where-Object {
            $_.Required -and $_.File -eq $releaseReadiness -and $_.Text -eq $entry.Text
        } | Select-Object -First 1
        if ($null -eq $item) {
            throw "Release readiness evidence references a missing checklist item: $($entry.Text)"
        }

        if ($item.Complete) {
            Assert-Evidence $entry.Text $entry.Path $entry.Patterns
        }
    }

    $missingReleaseEvidence = @()
    foreach ($item in $items | Where-Object { $_.Required -and $_.Complete -and $_.File -eq $releaseReadiness }) {
        $hasEvidence = @($releaseEvidence | Where-Object { $_.Text -eq $item.Text }).Count -gt 0
        if (-not $hasEvidence) {
            $missingReleaseEvidence += $item
        }
    }

    if ($missingReleaseEvidence.Count -gt 0) {
        $sample = $missingReleaseEvidence |
            Select-Object -First 10 |
            ForEach-Object { "$([System.IO.Path]::GetFileName($_.File)):$($_.Line) $($_.Text)" }
        throw "Completed release readiness items are missing evidence checks: $($sample -join '; ')"
    }

    $securitySectionEvidence = @{
        "User input" = @{
            Path = "tests/DotBoxD.Kernels.Tests/Serialization/JsonImporterTests.cs"
            Patterns = @("unsupported_properties", "assemblyName")
        }
        "IR" = @{
            Path = "tests/DotBoxD.Kernels.Tests/Serialization/JsonImporterTests.cs"
            Patterns = @("targetSandboxVersion", "reject")
        }
        "Type system" = @{
            Path = "src/Kernels/DotBoxD.Kernels/Sandbox/SandboxType.cs"
            Patterns = @("SandboxType", "OpaqueId")
        }
        "Bindings" = @{
            Path = "tests/DotBoxD.Kernels.Tests/Bindings/BindingRegistryHardeningTests.cs"
            Patterns = @("E-BINDING", "AuditLevel.PerCall")
        }
        "Capabilities/policy" = @{
            Path = "tests/DotBoxD.Kernels.Tests/Policy/CapabilityRevocationTests.cs"
            Patterns = @("RevokeCapability", "CapabilityRevoked")
        }
        "Interpreter" = @{
            Path = "src/Kernels/DotBoxD.Kernels.Interpreter/Internal/StatementExecutor.cs"
            Patterns = @("ChargeFuel", "ChargeLoopIteration")
        }
        "Compiler" = @{
            Path = "src/Kernels/DotBoxD.Kernels.Compiler/Emitters/ReflectionEmitSandboxCompiler.cs"
            Patterns = @("CompileAsync", "Verification")
        }
        "Verifier" = @{
            Path = "tests/DotBoxD.Kernels.Tests/Verifier/Generated/VerifierTests.cs"
            Patterns = @("PInvoke", "MutableStatic")
        }
        "Cache" = @{
            Path = "tests/DotBoxD.Kernels.Tests/Compiled/Core/CompiledCacheTests.cs"
            Patterns = @("Policy_hash_change", "Binding_manifest_change")
        }
        "Resource limits" = @{
            Path = "tests/DotBoxD.Kernels.Tests/Compiled/Generated/CompiledRuntimeQuotaTests.cs"
            Patterns = @("QuotaExceeded", "Fuel")
        }
        "Audit" = @{
            Path = "tests/DotBoxD.Kernels.Tests/Audit/SafeLoggingTests.cs"
            Patterns = @("RunSummary", "redacted")
        }
    }

    foreach ($section in $securitySectionEvidence.Keys) {
        $entry = $securitySectionEvidence[$section]
        Assert-Evidence "Security review section '$section'" $entry.Path $entry.Patterns
    }

    $missingSecurityEvidence = @($items | Where-Object {
        $_.Required -and $_.Complete -and $_.File -eq $securityReview -and
        -not $securitySectionEvidence.ContainsKey($_.Section)
    })
    if ($missingSecurityEvidence.Count -gt 0) {
        $sample = $missingSecurityEvidence |
            Select-Object -First 10 |
            ForEach-Object { "$([System.IO.Path]::GetFileName($_.File)):$($_.Line) [$($_.Section)] $($_.Text)" }
        throw "Completed security review items are missing section evidence checks: $($sample -join '; ')"
    }

    # Completed "Documentation" inventory items are release evidence: verify each checked entry
    # still points at an existing doc with expected patterns (non-blocking for -RequireComplete).
    $docSpec = "docs/Specs/Initial/dotboxd-sandbox-spec"
    $documentationEvidence = @{
        "User-facing language docs." = @{ Path = "$docSpec/spec/04-ir-language.md"; Patterns = @("JSON IR", "IR design goals") }
        "Host binding author guide." = @{ Path = "$docSpec/spec/07-bindings.md"; Patterns = @("# 07 — Bindings", "host-provided") }
        "Security model docs." = @{ Path = "$docSpec/spec/02-threat-model.md"; Patterns = @("Threat Model", "Assets to protect") }
        "Capability catalog." = @{ Path = "$docSpec/spec/08-runtime-safe-apis.md"; Patterns = @("Safe file API", "Safe network API") }
        "Error code reference." = @{ Path = "$docSpec/spec/09-interpreted-mode.md"; Patterns = @("E-POLICY-001", "E-RUNTIME-004") }
        "Debugging guide." = @{ Path = "$docSpec/spec/09-interpreted-mode.md"; Patterns = @("Debugging support") }
        "Operational runbook." = @{ Path = "$docSpec/operations/runbook.md"; Patterns = @("Operational Runbook") }
    }

    $completedDocumentation = @($items | Where-Object {
        -not $_.Required -and $_.Complete -and $_.File -eq $releaseReadiness -and $_.Section -eq "Documentation"
    })

    foreach ($item in $completedDocumentation) {
        if ($documentationEvidence.ContainsKey($item.Text)) {
            $entry = $documentationEvidence[$item.Text]
            Assert-Evidence "Documentation item '$($item.Text)'" $entry.Path $entry.Patterns
        }
    }

    $missingDocumentationEvidence = @($completedDocumentation | Where-Object { -not $documentationEvidence.ContainsKey($_.Text) })
    if ($missingDocumentationEvidence.Count -gt 0) {
        $sample = $missingDocumentationEvidence |
            Select-Object -First 10 |
            ForEach-Object { "$([System.IO.Path]::GetFileName($_.File)):$($_.Line) $($_.Text)" }
        throw "Completed documentation inventory items are missing evidence checks: $($sample -join '; ')"
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
