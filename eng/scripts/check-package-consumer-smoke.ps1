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

[DotBoxDService]
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

Write-Host "Package consumer smoke passed."
