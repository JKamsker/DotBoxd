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
    BranchMergeScopeTests = 2
    CacheKeyIdentityTests = 5
    VerifierAttackMatrixTests = 7
    VerifierDocumentedAttackMatrixTests = 6
    Verifier_rejects_starg_argument_mutation_opcode = 1
    VerifierLoopMeteringTests = 1
    VerifierManifestIdentityTests = 16
    VerifierMemberSignatureTests = 5
    BindingRegistryHardeningTests = 12
    WorkerIsolationTests = 11
    WorkerResultHardeningTests = 11
    JsonApiSurfaceTests = 1
    PublicModelImmutabilityTests = 5
    PluginPackageJsonTests = 23
    PluginPackageValidationTests = 22
    KernelRegistryLookupConsistencyTests = 1
    PluginTypedLiveSettingViewTests = 4
    EventIndexTrustBoundaryTests = 7
    CapabilityPolicySplitTests = 7
    PluginLiveUpdateModeTests = 2
    HookChainInterceptorRuntimeSurpriseTests = 2
    HookChainRuntimeTests = 11
    SubscriptionRuntimeTests = 9
    RemoteRunLocalChainRuntimeTests = 71
    RemoteRunLocalValidationTests = 9
    GeneratedRemoteHookChainFallbackTests = 38
    PluginAnalyzerEnumerableDtoValidationTests = 1
    PluginAnalyzerLiveSettingTypeValidationTests = 2
    ServerExtensionGeneratedContextParameterTests = 4
    PluginAnalyzerHookChainTests = 79
    InvokeAsyncGenerationTests = 7
    InvokeAsyncSurpriseGenerationTests = 10
    InvokeAsyncGeneratedReceiverSurpriseTests = 20
    InvokeAsyncGeneratedCodeRegressionTests = 12
    InvokeAsyncArrayRegressionTests = 1
    InvokeAsyncCaptureAliasRegressionTests = 1
    InvokeAsyncDtoConstructorSelectionRegressionTests = 2
    InvokeAsyncScalarDecodeRuntimeTests = 4
    InvokeAsyncDateTimeRuntimeTests = 3
    InvokeAsyncFrameworkStructRuntimeTests = 1
    InvokeAsyncCaptureBagRuntimeTests = 2
    InvokeAsyncMapReturnRuntimeTests = 1
    InvokeAsyncObjectInitializerOrderTests = 1
    InvokeAsyncInstallCancellationRuntimeTests = 2
    HostServiceBindingContractTests = 2
    PluginAnalyzerForbiddenApiReachabilityTests = 7
    PluginAnalyzerForbiddenApiConstructorReachabilityTests = 3
    PluginAnalyzerKernelMethodTests = 10
    PluginAnalyzerKernelMethodProjectionRegressionTests = 1
    PluginAnalyzerKernelMethodSurpriseRegressionTests = 8
    PluginAnalyzerKernelMethodEnumDefaultRegressionTests = 1
    PluginAnalyzerKernelMethodImplicitThisRegressionTests = 1
    PluginAnalyzerKernelMethodDescriptor = 33
    PluginServerFacadeRegressionTests = 3
    PluginServerMemberShapeRegressionTests = 13
    PluginServerSurpriseRegressionTests = 21
    PluginServerControlServiceTests = 4
    PluginServerContextContractTests = 29
    PluginServerTargetShapeRegressionTests = 4
    PluginServerSurfaceCollisionRegressionTests = 4
    ServerExtensionProxyAsyncTests = 4
    ServerExtensionProxyCancellationTests = 1
    ServerExtensionProxyNullableContractTests = 4
    RpcKernelGenerationTests = 13
    RpcKernelPackageValidationTests = 7
    RpcKernelLiveSettingRuntimeTests = 2
    RpcKernelMethodGenerationTests = 7
    RpcKernelMethodContextHelperRegressionTests = 1
    RpcKernelNamedArgumentGenerationTests = 4
    RpcKernelNumericConversionGenerationTests = 4
    RpcKernelObjectCreationValidationTests = 2
    RegistrationAccumulatorAssignableChildTests = 2
    RegistrationAccumulatorGenerationTests = 7
    RegistrationAccumulatorInheritedChildTests = 3
    RegistrationAccumulatorNullableConstraintTests = 1
    ServerExtensionInlineScopedHandleTests = 6
    ServerExtensionReceiverAuthoritySurpriseTests = 5
    ServerExtensionClientAccessibilityTests = 5
    ServerExtensionSurpriseRegressionTests = 18
    ServerExtensionDerivedDtoRegressionTests = 3
    ServerExtensionLoweringSurpriseTests = 10
    ServerExtensionGeneratedDtoReaderRegressionTests = 11
    ServerExtensionClientNoPayloadValidationTests = 1
    ServerExtensionClientContractSurpriseTests = 3
    ServerExtensionClientDecodeSurpriseTests = 2
    ServerExtensionClientProxySurpriseTests = 6
    ServerExtensionClientProxyValidationTests = 8
    ServerExtensionDateTimeSupportTests = 4
    ServerExtensionTimeSpanSupportTests = 6
    KernelRpcMarshallerSurpriseTests = 27
    ServerExtensionUnsupportedFrameworkStructTests = 1
    KernelRpcMarshallerDtoOrderTests = 18
    KernelRpcValueTests = 15
    KernelRpcBinaryCodecTests = 22
    RpcEnvelopeValidationTests = 5
    NoPayloadProtocolSurpriseTests = 5
    RequestEnvelopeMessageIdMismatch_ReturnsProtocolError = 1
    InvokeAsync_ResponseEnvelopeMessageIdMismatch_FaultsRequestWithProtocolException = 1
    InstanceRegistryTests = 8
    UnknownStreamComplete_ReportsProtocolError = 1
    UnknownStreamError_ReportsProtocolError = 1
    ForwardedRemoteStreamError_PreservesOriginalRemoteType = 1
    ClientTransportConnectionLifecycleTests = 4
    SingleConnectionTransportCoverageTests = 8
    SingleConnectionServerTransportLifecycleRegressionTests = 5
    NamedPipeServerTransportCancellationRegressionTests = 2
    UnsupportedShapeCoverageTests = 6
    UnsupportedDtoConstructibilityTests = 1
    UnsupportedDtoPayloadShapeTests = 5
    UnsupportedTransportPayloadShapeTests = 3
    SubServiceAvailabilityTests = 5
    SubServiceReturnedPropertyScopeTests = 1
    SubServicePropertyInheritanceTests = 8
    NullableSubServiceTests = 5
    FloatingPointSpecialDefaults_ArePreservedInGeneratedSurface = 1
    ParamsParameters_ArePreservedInGeneratedProxySurface = 1
    CallerInfoAttributes_ArePreservedInGeneratedServiceSurface = 1
    CallerArgumentExpression_TargetingCancellationToken_RemainsValidOnAsyncSibling = 1
    GenericServiceMethod_WithRefStructAntiConstraint_PreservesConstraintOnProxyStub = 1
    GeneratedFactoryRegistryDefaultValueTests = 2
    GeneratedDispatcherSurfaceTests = 1
    ServerExtensionMapTypeSupportTests = 8
    ServerExtensionMapBodyLoweringTests = 4
    HookResultBuilderHintNameRegressionTests = 1
    HookResultBuilderKeywordFieldTests = 1
    ServerExtensionEnumOverflowRegressionTests = 1
    Computed_dto_projection_round_trips = 1
    RegisterLocal_value_only_overload_lowers_and_runs_the_handler = 1
    DtoProjectionTests = 5
    PluginRevocationTests = 4
    Fix_CMP_0012_Tests = 33
    Fix_CMP_0026_Tests = 8
    Fix_API_0027_Tests = 2
    PinnedHttpTransportTests = 3
    ExpressionQueryTranslatorTests = 23
    QuerySatisfiabilityTests = 16
    QueryTextTests = 21
    QueryReviewHardeningTests = 25
    QueryComparerHardeningTests = 4
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
