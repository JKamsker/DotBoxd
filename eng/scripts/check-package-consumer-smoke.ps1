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
    "DotBoxd.Hosting",
    "DotBoxd.Kernels.Runtime",
    "DotBoxd.Kernels.Serialization.Json",
    "DotBoxd.Hosting.Http",
    "DotBoxd.Plugins",
    "DotBoxd.Abstractions",
    "DotBoxd.Plugins.Analyzer",
    "DotBoxd.Pushdown.Services"
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
    Resolve the DotBoxd.Kernels.* packages from the freshly-built local feed only. Pinning version 0.1.0
    can otherwise collide with a previously published 0.1.0 on nuget.org, so the smoke would test
    stale package contents instead of the local build.
  -->
  <packageSourceMapping>
    <packageSource key="local">
      <package pattern="DotBoxd.Kernels.*" />
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
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="DotBoxd.Hosting" Version="$($versions["DotBoxd.Hosting"])" />
    <PackageReference Include="DotBoxd.Kernels.Runtime" Version="$($versions["DotBoxd.Kernels.Runtime"])" />
    <PackageReference Include="DotBoxd.Kernels.Serialization.Json" Version="$($versions["DotBoxd.Kernels.Serialization.Json"])" />
    <PackageReference Include="DotBoxd.Hosting.Http" Version="$($versions["DotBoxd.Hosting.Http"])" />
    <PackageReference Include="DotBoxd.Plugins" Version="$($versions["DotBoxd.Plugins"])" />
    <PackageReference Include="DotBoxd.Abstractions" Version="$($versions["DotBoxd.Abstractions"])" />
    <PackageReference Include="DotBoxd.Pushdown.Services" Version="$($versions["DotBoxd.Pushdown.Services"])" />
    <PackageReference Include="DotBoxd.Plugins.Analyzer" Version="$($versions["DotBoxd.Plugins.Analyzer"])" PrivateAssets="all" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
"@
Set-Content -LiteralPath (Join-Path $resolvedWorkRoot "DotBoxd.Kernels.PackageConsumerSmoke.csproj") -Value $project

$program = @"
using DotBoxd.Kernels;
using DotBoxd.Hosting;
using DotBoxd.Plugins;
using DotBoxd.Kernels.Serialization.Json;
using DotBoxd.Hosting.Http;
using DotBoxd.Kernels.Transport.Ipc;
using DotBoxd.Kernels.PackageConsumerSmoke;
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
    .GrantFileRead(Path.GetTempPath(), 1024)
    .GrantLogging()
    .GrantHttpGet(new[] { "example.com" }, 4096)
    .Build();

var moduleImporter = typeof(DotBoxdJsonImporter);
var pluginUpload = typeof(PluginPackageJsonSerializer);
var ipc = typeof(DotBoxdDotBoxdRpcMessagePackIpc);

// Prove the packaged DotBoxdJsonExporter module-export surface is reachable and that the
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

var exportedJson = DotBoxdJsonExporter.Export(roundTripModule, indented: true);
var reimported = DotBoxdJsonImporter.Import(exportedJson);
if (reimported.Id != "package-consumer-roundtrip")
{
    throw new InvalidOperationException(`$"Unexpected round-tripped module id: {reimported.Id}");
}

var roundTripPlan = await host.PrepareAsync(reimported, policy);
if (!roundTripPlan.Module.Functions.Any(f => f.Id == "main"))
{
    throw new InvalidOperationException("Round-tripped module is missing its entrypoint after prepare.");
}

// Prove the packaged DotBoxd.Plugins.Analyzer source generator produced a callable
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
    package.Manifest.Subscriptions[0].Event != "SmokeEvent" ||
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
namespace DotBoxd.Kernels.PackageConsumerSmoke;

using DotBoxd.Abstractions;

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

Write-Host "Package consumer smoke passed."
