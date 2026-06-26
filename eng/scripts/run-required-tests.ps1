param(
    [Parameter(Mandatory = $true)]
    [string] $Project,
    [string] $Configuration = "Release",
    [switch] $NoBuild,
    [Parameter(Mandatory = $true)]
    [string[]] $RequiredFullyQualifiedNameContains,
    [hashtable] $MinimumExecutedTestsByGroup = @{}
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
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

$defaultMinimums = @{
    SafeFileSystemTests = 8
    SafeFileSystemWriteTests = 11
    SafeFileSystemReparsePointTests = 4
    FileExtensionPolicyTests = 2
    PathUriLiteralValidationTests = 29
    CompiledArtifactGuardTests = 16
    CompiledAsyncCapabilityParityTests = 4
    CompiledAsyncSynchronizationContextParityTests = 1
    CompiledRuntimeQuotaTests = 1
    CompiledCacheAuditTests = 1
    CompiledCacheConcurrencyTests = 4
    CompiledCacheEntrypointTests = 1
    CompiledCacheMetadataTests = 3
    CompiledCacheRootGuardTests = 6
    CompiledCacheTests = 10
    CacheKeyIdentityTests = 5
    VerifierAttackMatrixTests = 7
    VerifierDocumentedAttackMatrixTests = 6
    VerifierLoopMeteringTests = 1
    VerifierManifestIdentityTests = 16
    VerifierMemberSignatureTests = 5
    BindingRegistryHardeningTests = 12
    WorkerIsolationTests = 11
    WorkerResultHardeningTests = 11
    JsonApiSurfaceTests = 1
    PublicModelImmutabilityTests = 5
    PluginPackageValidationTests = 11
    EventIndexTrustBoundaryTests = 6
    CapabilityPolicySplitTests = 4
    HookChainRuntimeTests = 11
    SubscriptionRuntimeTests = 9
    RemoteRunLocalChainRuntimeTests = 12
    RemoteRunLocalValidationTests = 5
    GeneratedRemoteHookChainFallbackTests = 5
    ServerExtensionGeneratedContextParameterTests = 4
    PluginAnalyzerHookChainTests = 53
    InvokeAsyncGenerationTests = 7
    InvokeAsyncSurpriseGenerationTests = 6
    InvokeAsyncGeneratedReceiverSurpriseTests = 12
    InvokeAsyncGeneratedCodeRegressionTests = 7
    InvokeAsyncArrayRegressionTests = 1
    InvokeAsyncScalarDecodeRuntimeTests = 4
    InvokeAsyncCaptureBagRuntimeTests = 2
    PluginAnalyzerKernelMethodTests = 10
    PluginAnalyzerKernelMethodProjectionRegressionTests = 1
    PluginAnalyzerKernelMethodSurpriseRegressionTests = 7
    PluginAnalyzerKernelMethodDescriptor = 33
    PluginServerFacadeRegressionTests = 3
    PluginServerMemberShapeRegressionTests = 11
    PluginServerSurpriseRegressionTests = 10
    PluginServerTargetShapeRegressionTests = 3
    ServerExtensionProxyAsyncTests = 4
    RpcKernelGenerationTests = 8
    RpcKernelMethodGenerationTests = 6
    RpcKernelMethodContextHelperRegressionTests = 1
    RpcKernelNamedArgumentGenerationTests = 4
    RpcKernelNumericConversionGenerationTests = 4
    ServerExtensionInlineScopedHandleTests = 4
    ServerExtensionSurpriseRegressionTests = 8
    ServerExtensionGeneratedDtoReaderRegressionTests = 9
    ServerExtensionClientNoPayloadValidationTests = 1
    ServerExtensionClientContractSurpriseTests = 1
    ServerExtensionClientDecodeSurpriseTests = 2
    ServerExtensionClientProxySurpriseTests = 1
    KernelRpcMarshallerSurpriseTests = 17
    KernelRpcMarshallerDtoOrderTests = 18
    HookResultBuilderHintNameRegressionTests = 1
    ServerExtensionEnumOverflowRegressionTests = 1
    Computed_dto_projection_round_trips = 1
    PluginRevocationTests = 4
    PinnedHttpTransportTests = 3
    DifferentialFuzzTests = 1
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
$belowMinimum = @()
foreach ($requiredName in $requiredNames) {
    $matches = @($executed | Where-Object {
        $_.FullyQualifiedName.IndexOf($requiredName, [StringComparison]::Ordinal) -ge 0 -or
        $_.DisplayName.IndexOf($requiredName, [StringComparison]::Ordinal) -ge 0
    })
    if ($matches.Count -eq 0) {
        $missing += $requiredName
        continue
    }

    $minimum = 1
    if ($MinimumExecutedTestsByGroup.ContainsKey($requiredName)) {
        $minimum = [int] $MinimumExecutedTestsByGroup[$requiredName]
    } elseif ($defaultMinimums.ContainsKey($requiredName)) {
        $minimum = [int] $defaultMinimums[$requiredName]
    }

    if ($matches.Count -lt $minimum) {
        $belowMinimum += "$requiredName expected >= $minimum, executed $($matches.Count)"
    }
}

if ($missing.Count -gt 0) {
    throw "Required test filter did not execute expected test groups: $($missing -join ', ')"
}

if ($belowMinimum.Count -gt 0) {
    throw "Required test groups executed fewer tests than expected: $($belowMinimum -join '; ')"
}

Write-Host "Required test gate passed. Executed tests: $($executed.Count). Required groups: $($requiredNames.Count)."
