param(
    [string] $PackageDirectory = "artifacts/packages",
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
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
    "SafeIR.Hosting",
    "SafeIR.Runtime",
    "SafeIR.Serialization.Json",
    "SafeIR.Transport.Http",
    "SafeIR.Plugins",
    "SafeIR.PluginAnalyzer",
    "SafeIR.Transport.Ipc.ShaRpc"
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

$nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$(XmlEscape $fullPackageDirectory)" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
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
    <PackageReference Include="SafeIR.Hosting" Version="$($versions["SafeIR.Hosting"])" />
    <PackageReference Include="SafeIR.Runtime" Version="$($versions["SafeIR.Runtime"])" />
    <PackageReference Include="SafeIR.Serialization.Json" Version="$($versions["SafeIR.Serialization.Json"])" />
    <PackageReference Include="SafeIR.Transport.Http" Version="$($versions["SafeIR.Transport.Http"])" />
    <PackageReference Include="SafeIR.Plugins" Version="$($versions["SafeIR.Plugins"])" />
    <PackageReference Include="SafeIR.Transport.Ipc.ShaRpc" Version="$($versions["SafeIR.Transport.Ipc.ShaRpc"])" />
    <PackageReference Include="SafeIR.PluginAnalyzer" Version="$($versions["SafeIR.PluginAnalyzer"])" PrivateAssets="all" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
"@
Set-Content -LiteralPath (Join-Path $resolvedWorkRoot "SafeIR.PackageConsumerSmoke.csproj") -Value $project

$program = @"
using SafeIR;
using SafeIR.Hosting;
using SafeIR.Plugins;
using SafeIR.Serialization.Json;
using SafeIR.Transport.Http;
using SafeIR.Transport.Ipc;

var host = SandboxHost.Create(builder =>
{
    builder.AddDefaultPureBindings();
    builder.AddFileBindings();
    builder.AddLogBindings();
    builder.UseInterpreter();
});

var policy = SandboxPolicyBuilder.Create()
    .GrantFileRead("config", 1024)
    .GrantLogging()
    .GrantHttpGet(new[] { "example.com" }, 4096)
    .Build();

var moduleImporter = typeof(SafeIrJsonImporter);
var pluginUpload = typeof(PluginPackageJsonSerializer);
var ipc = typeof(SafeIrShaRpcMessagePackIpc);
Console.WriteLine($"{host.GetType().Name}:{policy.Hash}:{moduleImporter.Name}:{pluginUpload.Name}:{ipc.Name}");
"@
Set-Content -LiteralPath (Join-Path $resolvedWorkRoot "Program.cs") -Value $program

dotnet restore $resolvedWorkRoot --configfile (Join-Path $resolvedWorkRoot "NuGet.config")
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet build $resolvedWorkRoot --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Package consumer smoke passed."
