param(
    [string] $PackageDirectory = "artifacts/packages",
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$fullPackageDirectory = if ([System.IO.Path]::IsPathRooted($PackageDirectory)) {
    $PackageDirectory
} else {
    Join-Path $root $PackageDirectory
}

if (-not (Test-Path -LiteralPath $fullPackageDirectory)) {
    throw "Package directory does not exist: $fullPackageDirectory"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem

function ReadPackageVersions {
    $versions = @{}
    $packages = Get-ChildItem -LiteralPath $fullPackageDirectory -Filter "*.nupkg" -File |
        Where-Object { $_.Name -notlike "*.symbols.nupkg" }
    foreach ($package in $packages) {
        $zip = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
        try {
            $nuspecEntry = $zip.Entries |
                Where-Object { $_.FullName.EndsWith(".nuspec", [StringComparison]::Ordinal) } |
                Select-Object -First 1
            if ($null -eq $nuspecEntry) {
                throw "Package $($package.Name) has no nuspec."
            }

            $reader = New-Object System.IO.StreamReader($nuspecEntry.Open())
            try {
                [xml] $nuspec = $reader.ReadToEnd()
            } finally {
                $reader.Dispose()
            }

            $metadata = $nuspec.DocumentElement.SelectSingleNode("*[local-name()='metadata']")
            $id = $metadata.SelectSingleNode("*[local-name()='id']").InnerText
            $version = $metadata.SelectSingleNode("*[local-name()='version']").InnerText
            $versions[$id] = $version
        } finally {
            $zip.Dispose()
        }
    }

    return $versions
}

$versions = ReadPackageVersions
$requiredIds = @(
    "DotBoxD.Hosting",
    "DotBoxD.Kernels.Runtime",
    "DotBoxD.Kernels.Serialization.Json",
    "DotBoxD.Hosting.Http",
    "DotBoxD.Plugins",
    "DotBoxD.Abstractions",
    "DotBoxD",
    "DotBoxD.Services",
    "DotBoxD.Services.All",
    "DotBoxD.Plugins.Analyzer",
    "DotBoxD.Pushdown.Services"
)
foreach ($id in $requiredIds) {
    if (-not $versions.Contains($id)) {
        throw "Package consumer smoke is missing package '$id'."
    }
}

$artifactsRoot = Join-Path $root "artifacts"
$workRoot = Join-Path $artifactsRoot "package-consumer-smoke"
$resolvedArtifactsRoot = [System.IO.Path]::GetFullPath($artifactsRoot)
$resolvedWorkRoot = [System.IO.Path]::GetFullPath($workRoot)
if (-not $resolvedWorkRoot.StartsWith($resolvedArtifactsRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean smoke directory outside artifacts: $resolvedWorkRoot"
}

if (Test-Path -LiteralPath $resolvedWorkRoot) {
    Remove-Item -LiteralPath $resolvedWorkRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $resolvedWorkRoot | Out-Null

function XmlEscape([string] $value) {
    return [System.Security.SecurityElement]::Escape($value)
}

$isolatedPackagesFolder = Join-Path $resolvedWorkRoot ".nuget-packages"
$splitSdkPackageDirectory = Join-Path $resolvedWorkRoot "split-sdk-packages"
New-Item -ItemType Directory -Path $splitSdkPackageDirectory | Out-Null
$nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <!--
    Isolate the restore cache to this work directory. Pinning version 0.1.0 collides with any
    previously published 0.1.0 on nuget.org; the global packages folder caches by id+version, so a
    once-cached published package would shadow the freshly-built local build. A per-run folder keeps
    the smoke hermetic.
  -->
  <config>
    <add key="globalPackagesFolder" value="$(XmlEscape $isolatedPackagesFolder)" />
  </config>
  <packageSources>
    <clear />
    <add key="local" value="$(XmlEscape $fullPackageDirectory)" />
    <add key="split-sdk" value="$(XmlEscape $splitSdkPackageDirectory)" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <!--
    Resolve every DotBoxD.* package from the freshly-built local feed only. Pinning version 0.1.0
    can otherwise collide with a previously published 0.1.0 on nuget.org, so the smoke would test
    stale package contents instead of the local build. The pattern is DotBoxD.* (not just
    DotBoxD.Kernels.*) so the Hosting/Plugins/Abstractions/Pushdown packages also map locally.
  -->
  <packageSourceMapping>
    <packageSource key="local">
      <package pattern="DotBoxD" />
      <package pattern="DotBoxD.*" />
    </packageSource>
    <packageSource key="split-sdk">
      <package pattern="DotBoxD.PluginSdkSplit.Sdk" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@
Set-Content -LiteralPath (Join-Path $resolvedWorkRoot "NuGet.config") -Value $nugetConfig

$project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <!-- This temp project lives under artifacts/ inside the repo, so MSBuild would otherwise
         inherit the repo's Directory.Packages.props (Central Package Management) and reject the
         inline PackageReference versions with NU1008. A real external consumer is not under our
         CPM, so opt out here to simulate that hermetic consumption. -->
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <!-- The same inheritance would inject the repo's Roslynator/Meziantou analyzers as versionless
         PackageReferences (NU1015 once CPM is off); a real external consumer never gets them. -->
    <DotBoxDDisableExtraAnalyzers>true</DotBoxDDisableExtraAnalyzers>
    <!-- Likewise the repo's .editorconfig + EnforceCodeStyleInBuild would fail this throwaway
         consumer's generated Program.cs on style rules (e.g. IDE0005); an external consumer has
         neither, so simulate that and keep the smoke focused on restore/build/run. -->
    <EnforceCodeStyleInBuild>false</EnforceCodeStyleInBuild>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="DotBoxD.Hosting" Version="$($versions["DotBoxD.Hosting"])" />
    <PackageReference Include="DotBoxD.Kernels.Runtime" Version="$($versions["DotBoxD.Kernels.Runtime"])" />
    <PackageReference Include="DotBoxD.Kernels.Serialization.Json" Version="$($versions["DotBoxD.Kernels.Serialization.Json"])" />
    <PackageReference Include="DotBoxD.Hosting.Http" Version="$($versions["DotBoxD.Hosting.Http"])" />
    <PackageReference Include="DotBoxD.Plugins" Version="$($versions["DotBoxD.Plugins"])" />
    <PackageReference Include="DotBoxD.Abstractions" Version="$($versions["DotBoxD.Abstractions"])" />
    <PackageReference Include="DotBoxD.Pushdown.Services" Version="$($versions["DotBoxD.Pushdown.Services"])" />
    <PackageReference Include="DotBoxD.Plugins.Analyzer" Version="$($versions["DotBoxD.Plugins.Analyzer"])" PrivateAssets="all" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
"@
Set-Content -LiteralPath (Join-Path $resolvedWorkRoot "DotBoxD.Kernels.PackageConsumerSmoke.csproj") -Value $project

$program = @"
using DotBoxD.Kernels;
using DotBoxD.Hosting;
using DotBoxD.Hosting.Execution;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Json;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json;
using DotBoxD.Hosting.Http;
using DotBoxD.Hosting.Http.Hosting;
using DotBoxD.Hosting.Http.Policy;
using DotBoxD.Pushdown.Services;
using DotBoxD.Kernels.PackageConsumerSmoke;
using System.IO;

var host = SandboxHost.Create(builder =>
{
    builder.AddDefaultPureBindings();
    builder.AddFileBindings();
    builder.AddLogBindings();
    builder.AddNetworkBindings();
    builder.UseInterpreter();
});

var policy = SandboxPolicyBuilder.Create()
    .AllowRuntimeAsync()
    .GrantFileRead(Path.GetTempPath(), 1024)
    .GrantLogging()
    .GrantHttpGet(new[] { "example.com" }, 4096)
    .Build();

var moduleImporter = typeof(JsonImporter);
var pluginUpload = typeof(PluginPackageJsonSerializer);
var ipc = typeof(RpcMessagePackIpc);

// Prove the packaged JsonExporter module-export surface is reachable and that the
// documented JSON IR round trip (export -> import -> prepare) works through the public
// package references and namespaces. If the exporter is dropped from the package, lands in
// the wrong namespace, or loses a transitive dependency, this consumer fails to compile or
// the prepared-plan assertion throws, failing the smoke.
var roundTripModule = new SandboxModule(
    "package-consumer-roundtrip",
    SemVersion.One,
    SemVersion.One,
    new CapabilityRequest[0],
    new[]
    {
        new SandboxFunction(
            "main",
            true,
            new Parameter[0],
            SandboxType.I32,
            new Statement[]
            {
                new ReturnStatement(
                    new LiteralExpression(SandboxValue.FromInt32(7), new SourceSpan(1, 1)),
                    new SourceSpan(1, 1))
            })
    },
    new Dictionary<string, string>());

var exportedJson = JsonExporter.Export(roundTripModule, indented: true);
var reimported = JsonImporter.Import(exportedJson);
if (reimported.Id != "package-consumer-roundtrip")
{
    throw new InvalidOperationException(`$"Unexpected round-tripped module id: {reimported.Id}");
}

var roundTripPlan = await host.PrepareAsync(reimported, policy);
if (!roundTripPlan.Module.Functions.Any(f => f.Id == "main"))
{
    throw new InvalidOperationException("Round-tripped module is missing its entrypoint after prepare.");
}

// Prove the packaged DotBoxD.Plugins.Analyzer source generator produced a callable
// *PluginPackage.Create() factory for the [Plugin] kernel defined below. If the
// analyzer asset is missing from the package, the generator fails to initialize, or the
// generated factory cannot build a valid PluginPackage, this consumer will not compile or
// the runtime assertions below will throw, failing the smoke.
var package = SmokePluginPackage.Create();
if (package.Manifest.PluginId != "package-consumer-smoke")
{
    throw new InvalidOperationException(`$"Unexpected generated plugin id: {package.Manifest.PluginId}");
}

if (package.Manifest.Subscriptions.Count != 1 ||
    package.Manifest.Subscriptions[0].Event != "DotBoxD.Kernels.PackageConsumerSmoke.SmokeEvent" ||
    package.Manifest.Subscriptions[0].Kernel != "SmokeKernel")
{
    throw new InvalidOperationException("Generated manifest is missing the expected SmokeEvent/SmokeKernel subscription.");
}

if (!package.Module.Functions.Any(f => f.Id == package.Entrypoints.ShouldHandle) ||
    !package.Module.Functions.Any(f => f.Id == package.Entrypoints.Handle))
{
    throw new InvalidOperationException("Generated module is missing the ShouldHandle/Handle entrypoints.");
}

Console.WriteLine($"{host.GetType().Name}:{policy.Hash}:{moduleImporter.Name}:{pluginUpload.Name}:{ipc.Name}:{package.Manifest.PluginId}");
"@
Set-Content -LiteralPath (Join-Path $resolvedWorkRoot "Program.cs") -Value $program

$kernel = @"
namespace DotBoxD.Kernels.PackageConsumerSmoke;

using DotBoxD.Abstractions;

public sealed record SmokeEvent(string TargetId, string Message, int Amount);

[Plugin("package-consumer-smoke")]
public sealed partial class SmokeKernel : IEventKernel<SmokeEvent>
{
    [LiveSetting]
    public int MinAmount { get; set; } = 1;

    public bool ShouldHandle(SmokeEvent e, HookContext ctx)
        => e.Amount >= MinAmount;

    public void Handle(SmokeEvent e, HookContext ctx)
        => ctx.Messages.Send(e.TargetId, e.Message);
}
"@
Set-Content -LiteralPath (Join-Path $resolvedWorkRoot "SmokeKernel.cs") -Value $kernel

dotnet restore $resolvedWorkRoot --configfile (Join-Path $resolvedWorkRoot "NuGet.config")
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet build $resolvedWorkRoot --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet run --project $resolvedWorkRoot --configuration $Configuration --no-build
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

function Invoke-ServiceGeneratorSmoke([string] $Name, [string] $PackageId) {
    $projectRoot = Join-Path $resolvedWorkRoot $Name
    New-Item -ItemType Directory -Path $projectRoot | Out-Null

    $project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <!-- The same inheritance would inject the repo's Roslynator/Meziantou analyzers as versionless
         PackageReferences (NU1015 once CPM is off); a real external consumer never gets them. -->
    <DotBoxDDisableExtraAnalyzers>true</DotBoxDDisableExtraAnalyzers>
    <!-- Likewise the repo's .editorconfig + EnforceCodeStyleInBuild would fail this throwaway
         consumer's generated Program.cs on style rules (e.g. IDE0005); an external consumer has
         neither, so simulate that and keep the smoke focused on restore/build/run. -->
    <EnforceCodeStyleInBuild>false</EnforceCodeStyleInBuild>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="$PackageId" Version="$($versions[$PackageId])" />
  </ItemGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $projectRoot "$Name.csproj") -Value $project

    $program = @"
using DotBoxD.Services.Attributes;
using DotBoxD.Services.Generated;

var service = DotBoxDGenerated.Services.SingleOrDefault(s => s.ServiceType == typeof(IMetaSmokeService));
if (service.ServiceName != "IMetaSmokeService")
{
    throw new InvalidOperationException("The service source generator did not register IMetaSmokeService.");
}

Console.WriteLine(service.ProxyType.Name + ":" + service.DispatcherType.Name);

[RpcService]
public interface IMetaSmokeService
{
    Task<int> EchoAsync(int value, CancellationToken ct = default);
}
"@
    Set-Content -LiteralPath (Join-Path $projectRoot "Program.cs") -Value $program

    dotnet restore $projectRoot --configfile (Join-Path $resolvedWorkRoot "NuGet.config")
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    dotnet build $projectRoot --configuration $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    dotnet run --project $projectRoot --configuration $Configuration --no-build
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

Invoke-ServiceGeneratorSmoke "DotBoxD.Services.All.MetaSmoke" "DotBoxD.Services.All"
Invoke-ServiceGeneratorSmoke "DotBoxD.MetaSmoke" "DotBoxD"

function Invoke-DotBoxDPluginAuthoringSmoke {
    $projectRoot = Join-Path $resolvedWorkRoot "DotBoxD.PluginAuthoringSmoke"
    New-Item -ItemType Directory -Path $projectRoot | Out-Null

    $project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <!-- The same inheritance would inject the repo's Roslynator/Meziantou analyzers as versionless
         PackageReferences (NU1015 once CPM is off); a real external consumer never gets them. -->
    <DotBoxDDisableExtraAnalyzers>true</DotBoxDDisableExtraAnalyzers>
    <!-- Likewise the repo's .editorconfig + EnforceCodeStyleInBuild would fail this throwaway
         consumer's generated Program.cs on style rules (e.g. IDE0005); an external consumer has
         neither, so simulate that and keep the smoke focused on restore/build/run. -->
    <EnforceCodeStyleInBuild>false</EnforceCodeStyleInBuild>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="DotBoxD" Version="$($versions["DotBoxD"])" />
  </ItemGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $projectRoot "DotBoxD.PluginAuthoringSmoke.csproj") -Value $project

    $program = @"
using DotBoxD.Abstractions;
using DotBoxD.Plugins;

var server = PluginServer.Create();
_ = server;
var package = SmokePluginPackage.Create();
if (package.Manifest.PluginId != "dotboxd-meta-plugin-smoke")
{
    throw new InvalidOperationException("The root DotBoxD meta package did not expose plugin authoring generation.");
}

Console.WriteLine(package.Manifest.PluginId);

public sealed record SmokeEvent(string TargetId, string Message, int Amount);

[Plugin("dotboxd-meta-plugin-smoke")]
public sealed partial class SmokeKernel : IEventKernel<SmokeEvent>
{
    public bool ShouldHandle(SmokeEvent e, HookContext ctx) => e.Amount > 0;

    public void Handle(SmokeEvent e, HookContext ctx) => ctx.Messages.Send(e.TargetId, e.Message);
}
"@
    Set-Content -LiteralPath (Join-Path $projectRoot "Program.cs") -Value $program

    dotnet restore $projectRoot --configfile (Join-Path $resolvedWorkRoot "NuGet.config")
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    dotnet build $projectRoot --configuration $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    dotnet run --project $projectRoot --configuration $Configuration --no-build
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

Invoke-DotBoxDPluginAuthoringSmoke

function Invoke-PluginSdkSplitSmoke {
    $sdkRoot = Join-Path $resolvedWorkRoot "DotBoxD.PluginSdkSplit.Sdk"
    $consumerRoot = Join-Path $resolvedWorkRoot "DotBoxD.PluginSdkSplit.Consumer"
    $sdkVersion = "0.1.0-smoke"
    New-Item -ItemType Directory -Path $sdkRoot | Out-Null
    New-Item -ItemType Directory -Path $consumerRoot | Out-Null

    $sdkProject = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <DotBoxDDisableExtraAnalyzers>true</DotBoxDDisableExtraAnalyzers>
    <EnforceCodeStyleInBuild>false</EnforceCodeStyleInBuild>
    <IsPackable>true</IsPackable>
    <PackageId>DotBoxD.PluginSdkSplit.Sdk</PackageId>
    <Version>$sdkVersion</Version>
    <Authors>DotBoxD</Authors>
    <Description>Temporary SDK package used by the DotBoxD package consumer smoke.</Description>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="DotBoxD.Abstractions" Version="$($versions["DotBoxD.Abstractions"])" />
    <PackageReference Include="DotBoxD.Plugins" Version="$($versions["DotBoxD.Plugins"])" />
    <PackageReference Include="DotBoxD.Services" Version="$($versions["DotBoxD.Services"])" />
    <PackageReference Include="DotBoxD.Pushdown.Services" Version="$($versions["DotBoxD.Pushdown.Services"])" />
    <PackageReference Include="DotBoxD.Plugins.Analyzer" Version="$($versions["DotBoxD.Plugins.Analyzer"])" PrivateAssets="all" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $sdkRoot "DotBoxD.PluginSdkSplit.Sdk.csproj") -Value $sdkProject

    $sdkSource = @"
using System.Threading;
using System.Threading.Tasks;
using DotBoxD.Abstractions;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Services.Attributes;

namespace SplitSdk
{
    [RpcService]
    public interface ISplitControls
    {
        [HostBinding("split.read.tool", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        int Peek(string id);
    }

    [RpcService]
    public interface IGameWorld
    {
        ISplitControls Tools { get; }

        [HostBinding("split.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        int Read(string id);
    }

    public sealed record SplitEvent(string TargetId, int Amount);

    [GeneratePluginServer(Context = typeof(GamePluginContext))]
    public partial class GamePluginServer : IGameWorld
    {
    }

    public sealed partial class GamePluginContext
    {
        [KernelMethod]
        public bool IsAllowed(string id, int minimum) => World.Read(id) >= minimum;

        [KernelMethod]
        public bool IsEven(int value) => value % 2 == 0;
    }
}

namespace SplitSdk.Ipc
{
    public readonly record struct LiveSettingUpdate(string Name, string Value);

    [RpcService]
    public interface IGamePluginControlService : DotBoxD.Plugins.IServerExtensionWireClient
    {
        ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);
        ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default);
        ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default);
        ValueTask UpdateSettingsAsync(string pluginId, LiveSettingUpdate[] updates, bool atomic = false, CancellationToken ct = default);
        ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);
    }
}

"@
    Set-Content -LiteralPath (Join-Path $sdkRoot "Sdk.cs") -Value $sdkSource

    dotnet restore $sdkRoot --configfile (Join-Path $resolvedWorkRoot "NuGet.config")
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    dotnet build $sdkRoot --configuration $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    dotnet pack $sdkRoot --configuration $Configuration --no-build --output $splitSdkPackageDirectory
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    $sdkPackage = Join-Path $splitSdkPackageDirectory "DotBoxD.PluginSdkSplit.Sdk.$sdkVersion.nupkg"
    if (-not (Test-Path -LiteralPath $sdkPackage)) {
        throw "Split SDK smoke did not produce expected package: $sdkPackage"
    }

    $consumerProject = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <DotBoxDDisableExtraAnalyzers>true</DotBoxDDisableExtraAnalyzers>
    <EnforceCodeStyleInBuild>false</EnforceCodeStyleInBuild>
    <InterceptorsNamespaces>DotBoxD.Plugins.Generated</InterceptorsNamespaces>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="DotBoxD.PluginSdkSplit.Sdk" Version="$sdkVersion" />
    <PackageReference Include="DotBoxD.Plugins" Version="$($versions["DotBoxD.Plugins"])" />
    <PackageReference Include="DotBoxD.Pushdown.Services" Version="$($versions["DotBoxD.Pushdown.Services"])" />
    <PackageReference Include="DotBoxD.Plugins.Analyzer" Version="$($versions["DotBoxD.Plugins.Analyzer"])" PrivateAssets="all" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $consumerRoot "DotBoxD.PluginSdkSplit.Consumer.csproj") -Value $consumerProject

    $consumerProgram = @"
using System.Reflection;
using DotBoxD.Abstractions;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Json;
using DotBoxD.Pushdown.Services;
using DotBoxD.Services.Generated;
using SplitSdk;
using SplitSdk.Ipc;

var pipeName = "dotboxd-split-sdk-" + Guid.NewGuid().ToString("N");
var control = new RecordingControlService();
await using var ipcHost = RpcMessagePackIpc.ListenNamedPipe(pipeName, peer =>
{
    peer.ProvideGamePluginControlService(control);
    peer.ProvideGameWorld(new FakeWorld());
});
await ipcHost.StartAsync();

await using IGameWorldServer server = GamePluginServerBuilder
    .FromPipeName(pipeName)
    .Setup(s => s.Tools.Extend<BonusKernel>())
    .Build();
await server.StartAsync();

var hooks = server.Hooks;
var subscriptions = server.Subscriptions;
SplitSdkConsumerUsage.Configure(hooks, subscriptions);

if (control.ServerExtensions.Count != 1 ||
    control.ServerExtensions[0].Manifest.PluginId != "split-bonus" ||
    !control.ServerExtensions[0].Manifest.RequiredCapabilities.Contains("split.read.tool"))
{
    throw new InvalidOperationException("The split SDK consumer did not install the grafted extension package.");
}

var extensionResult = server.Tools.AddBonus(5);
if (extensionResult != 12 || control.LastRpcPluginId != "split-bonus")
{
    throw new InvalidOperationException("The split SDK grafted extension did not invoke through the generated IPC wire client.");
}

if (control.Hooks.Count != 1 ||
    control.Hooks[0].Manifest.Subscriptions[0].Event != "SplitSdk.SplitEvent" ||
    !control.Hooks[0].Manifest.RequiredCapabilities.Contains("split.read.value") ||
    !control.Hooks[0].Manifest.RequiredCapabilities.Contains("host.message.write"))
{
    throw new InvalidOperationException("The prebuilt SDK context descriptor did not flow host requirements into the hook package.");
}

if (control.Subscriptions.Count != 1 ||
    control.Subscriptions[0].Manifest.Subscriptions[0].Event != "SplitSdk.SplitEvent" ||
    !control.Subscriptions[0].Manifest.RequiredCapabilities.Contains("host.message.write"))
{
    throw new InvalidOperationException("The prebuilt SDK subscription registry marker was not honored through PackageReference.");
}

var generatedPackageTypes = Assembly.GetExecutingAssembly().GetTypes().Count(type =>
    type.Name.StartsWith("HookChain_", StringComparison.Ordinal) &&
    type.Name.EndsWith("PluginPackage", StringComparison.Ordinal));
if (generatedPackageTypes < 2)
{
    throw new InvalidOperationException("The consumer did not generate both hook and subscription packages from the prebuilt SDK facade.");
}

Console.WriteLine(control.Hooks[0].Manifest.PluginId + ":" + control.Subscriptions[0].Manifest.PluginId + ":" + extensionResult);

public static class SplitSdkConsumerUsage
{
    public static void Configure(GamePluginHookRegistry hooks, GamePluginSubscriptionRegistry subscriptions)
    {
        var aliasedHooks = hooks;
        aliasedHooks.On<SplitEvent>()
            .Where((e, ctx) => ctx.IsAllowed(e.TargetId, e.Amount))
            .Run((e, ctx) => ctx.Messages.Send(e.TargetId, "accepted"));

        var aliasedSubscriptions = subscriptions;
        aliasedSubscriptions.On<SplitEvent>()
            .Where((e, ctx) => ctx.IsEven(e.Amount))
            .Run((e, ctx) => ctx.Messages.Send(e.TargetId, "even"));
    }
}

[ServerExtension(typeof(ISplitControls), "split-bonus")]
public sealed partial class BonusKernel
{
    private readonly ISplitControls _tools;

    public BonusKernel(ISplitControls tools) => _tools = tools;

    [ServerExtensionMethod(typeof(ISplitControls))]
    public int AddBonus(int amount, HookContext ctx)
    {
        return amount + _tools.Peek("bonus");
    }
}

public sealed class FakeWorld : IGameWorld
{
    public ISplitControls Tools { get; } = new FakeSplitControls();

    public int Read(string id) => id.Length + 10;
}

public sealed class FakeSplitControls : ISplitControls
{
    public int Peek(string id) => 7;
}

public sealed class RecordingControlService : IGamePluginControlService
{
    public List<PluginPackage> Hooks { get; } = new();
    public List<PluginPackage> Subscriptions { get; } = new();
    public List<PluginPackage> ServerExtensions { get; } = new();
    public string LastRpcPluginId { get; private set; } = string.Empty;

    public ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default)
    {
        var package = PluginPackageJsonSerializer.Import(packageJson);
        Hooks.Add(package);
        return ValueTask.FromResult(package.CallbackSubscriptionId ?? package.Manifest.PluginId);
    }

    public ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default)
    {
        var package = PluginPackageJsonSerializer.Import(packageJson);
        Subscriptions.Add(package);
        return ValueTask.FromResult(package.CallbackSubscriptionId ?? package.Manifest.PluginId);
    }

    public ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default)
    {
        var package = PluginPackageJsonSerializer.Import(packageJson);
        ServerExtensions.Add(package);
        return ValueTask.FromResult(package.Manifest.PluginId);
    }

    public ValueTask<byte[]> InvokeServerExtensionAsync(string pluginId, byte[] arguments, CancellationToken ct = default)
    {
        LastRpcPluginId = pluginId;
        var amount = KernelRpcBinaryCodec.DecodeArguments(arguments)[0].Int32Value;
        return ValueTask.FromResult(KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Int32(amount + 7)));
    }

    public ValueTask UpdateSettingsAsync(
        string pluginId,
        LiveSettingUpdate[] updates,
        bool atomic = false,
        CancellationToken ct = default)
        => ValueTask.CompletedTask;

    public ValueTask HoldUntilShutdownAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
}
"@
    Set-Content -LiteralPath (Join-Path $consumerRoot "Program.cs") -Value $consumerProgram

    dotnet restore $consumerRoot --configfile (Join-Path $resolvedWorkRoot "NuGet.config")
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    dotnet build $consumerRoot --configuration $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    dotnet run --project $consumerRoot --configuration $Configuration --no-build
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

Invoke-PluginSdkSplitSmoke

Write-Host "Package consumer smoke passed."
