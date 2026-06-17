param(
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$capabilitiesExample = Join-Path $root "samples/Kernels/Capabilities/DotBoxD.Kernels.Example.Capabilities/DotBoxD.Kernels.Example.Capabilities.csproj"
$hostingExample = Join-Path $root "samples/Kernels/Hosting/DotBoxD.Kernels.Example.Hosting/DotBoxD.Kernels.Example.Hosting.csproj"
$pluginAuthoringExample = Join-Path $root "samples/Kernels/PluginAuthoring/DotBoxD.Kernels.Example.PluginAuthoring/DotBoxD.Kernels.Example.PluginAuthoring.csproj"
$httpTransportExample = Join-Path $root "samples/Kernels/HttpTransport/DotBoxD.Kernels.HttpTransportExample/DotBoxD.Kernels.HttpTransportExample.csproj"
$localPluginExample = Join-Path $root "samples/Kernels/LocalPlugin/DotBoxD.Kernels.PluginLocal/DotBoxD.Kernels.PluginLocal.csproj"
$ipcServerExample = Join-Path $root "samples/Pushdown/PluginIpc/DotBoxD.Kernels.PluginIpc.Server/DotBoxD.Kernels.PluginIpc.Server.csproj"
$ipcClientExample = Join-Path $root "samples/Pushdown/PluginIpc/DotBoxD.Kernels.PluginIpc.Client/DotBoxD.Kernels.PluginIpc.Client.csproj"
$gameServerExample = Join-Path $root "samples/Kernels/GameServer/DotBoxD.Kernels.Game.Server/DotBoxD.Kernels.Game.Server.csproj"
$gamePluginExample = Join-Path $root "samples/Kernels/GameServer/DotBoxD.Kernels.Game.Plugin/DotBoxD.Kernels.Game.Plugin.csproj"

function Resolve-RepoPath([string] $Path) {
    $normalized = $Path.Trim().Trim('"').Replace('\', [System.IO.Path]::DirectorySeparatorChar)
    return Join-Path $root $normalized
}

function Assert-ExistingPath([string] $Document, [int] $LineNumber, [string] $Path) {
    $resolved = Resolve-RepoPath $Path
    if (-not (Test-Path -LiteralPath $resolved)) {
        throw "$Document line $LineNumber references missing path: $Path"
    }
}

function Test-DocumentCommands([string] $Path) {
    $lines = Get-Content -LiteralPath $Path
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        foreach ($match in [regex]::Matches($line, 'dotnet\s+(restore|build|test|pack)\s+(?<target>[^`\s]+)')) {
            Assert-ExistingPath $Path ($i + 1) $match.Groups["target"].Value
        }

        foreach ($match in [regex]::Matches($line, 'dotnet\s+run\s+--project\s+(?<project>[^`\s]+)')) {
            Assert-ExistingPath $Path ($i + 1) $match.Groups["project"].Value
        }

        foreach ($match in [regex]::Matches($line, '(?<script>(?:\./|\.\\)?(?:scripts|eng/scripts|eng\\scripts)[^`\s]*\.ps1)')) {
            Assert-ExistingPath $Path ($i + 1) $match.Groups["script"].Value
        }
    }
}

function Assert-DocsDoNotContain([string] $Pattern, [string] $Description) {
    $documents = Get-ChildItem -LiteralPath (Join-Path $root "docs/Specs") -Recurse -File -Filter "*.md"
    $matches = @($documents | Select-String -Pattern $Pattern)
    if ($matches.Count -gt 0) {
        $first = $matches[0]
        throw "Documentation contains stale text ($Description): $($first.Path):$($first.LineNumber)"
    }
}

function Read-TextIfExists([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        return ""
    }

    return (Get-Content -LiteralPath $Path -Raw)
}

function Write-CapturedOutput([string] $Description, [string] $OutputPath, [string] $ErrorPath) {
    $output = Read-TextIfExists $OutputPath
    $errorOutput = Read-TextIfExists $ErrorPath

    if (-not [string]::IsNullOrWhiteSpace($output)) {
        Write-Host $output.TrimEnd()
    }

    if (-not [string]::IsNullOrWhiteSpace($errorOutput)) {
        Write-Warning "$Description stderr:`n$($errorOutput.TrimEnd())"
    }
}

function Stop-ProcessTree([System.Diagnostics.Process] $Process) {
    if ($Process.HasExited) {
        return
    }

    try {
        $Process.Kill($true)
    } catch {
        $Process.Kill()
    }

    $Process.WaitForExit()
}

function Invoke-DotNetProject([string] $Description, [string[]] $Arguments, [int] $TimeoutSeconds = 60) {
    $outputPath = Join-Path ([System.IO.Path]::GetTempPath()) ("dotboxd-smoke-" + [Guid]::NewGuid().ToString("N") + ".out")
    $errorPath = Join-Path ([System.IO.Path]::GetTempPath()) ("dotboxd-smoke-" + [Guid]::NewGuid().ToString("N") + ".err")
    $parameters = @{
        FilePath = "dotnet"
        ArgumentList = $Arguments
        RedirectStandardOutput = $outputPath
        RedirectStandardError = $errorPath
        PassThru = $true
    }

    if ($IsWindows) {
        $parameters.WindowStyle = "Hidden"
    }

    $process = Start-Process @parameters
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            Stop-ProcessTree $process
            Write-CapturedOutput $Description $outputPath $errorPath
            throw "$Description timed out after $TimeoutSeconds seconds."
        }

        Write-CapturedOutput $Description $outputPath $errorPath
        if ($process.ExitCode -ne 0) {
            throw "$Description failed with exit code $($process.ExitCode)."
        }
    } finally {
        $process.Dispose()
        Remove-Item -LiteralPath $outputPath, $errorPath -Force -ErrorAction SilentlyContinue
    }
}

$commandDocuments = @(
    (Join-Path $root "README.md"),
    (Join-Path $root "docs/Specs/Addendum/Examples.md")
)
$specDocuments = @(Get-ChildItem -LiteralPath (Join-Path $root "docs/Specs") -Recurse -File -Filter "*.md" |
    Select-Object -ExpandProperty FullName)
foreach ($document in @($commandDocuments + $specDocuments | Select-Object -Unique)) {
    Test-DocumentCommands $document
}

Assert-DocsDoNotContain "Sandbox\.Parse" "JSON IR import is Sandbox.ImportJson"
Assert-DocsDoNotContain "tenant://123/config" "file grants use canonical filesystem roots"
Assert-DocsDoNotContain "Proposed Public C# API" "public API document is no longer proposed"
Assert-DocsDoNotContain "Proposed C# API surface" "public API index is no longer proposed"
Assert-DocsDoNotContain "Add compiler/cache after the core model is proven" "compiled mode is implemented"

Invoke-DotNetProject "Capabilities example smoke test" @("run", "--project", $capabilitiesExample, "--configuration", $Configuration, "--no-build")
Invoke-DotNetProject "Hosting example smoke test" @("run", "--project", $hostingExample, "--configuration", $Configuration, "--no-build")
Invoke-DotNetProject "Plugin authoring example smoke test" @("run", "--project", $pluginAuthoringExample, "--configuration", $Configuration, "--no-build")
Invoke-DotNetProject "HTTP transport example smoke test" @("run", "--project", $httpTransportExample, "--configuration", $Configuration, "--no-build")
Invoke-DotNetProject "Local plugin example smoke test" @("run", "--project", $localPluginExample, "--configuration", $Configuration, "--no-build")

if (-not $IsWindows) {
    Write-Host "Skipping named-pipe IPC smoke on non-Windows runners."
    Write-Host "Docs/example smoke checks passed."
    return
}

function Start-IpcServer([string] $Project, [string] $PipeName) {
    $outputPath = Join-Path ([System.IO.Path]::GetTempPath()) ("dotboxd-ipc-server-" + [Guid]::NewGuid().ToString("N") + ".out")
    $errorPath = Join-Path ([System.IO.Path]::GetTempPath()) ("dotboxd-ipc-server-" + [Guid]::NewGuid().ToString("N") + ".err")
    $arguments = @(
        "run", "--project", $Project,
        "--configuration", $Configuration,
        "--no-build", "--", $PipeName)
    $parameters = @{
        FilePath = "dotnet"
        ArgumentList = $arguments
        RedirectStandardOutput = $outputPath
        RedirectStandardError = $errorPath
        PassThru = $true
    }

    if ($IsWindows) {
        $parameters.WindowStyle = "Hidden"
    }

    $process = Start-Process @parameters
    [pscustomobject] @{
        Process = $process
        OutputPath = $outputPath
        ErrorPath = $errorPath
    }
}

function Invoke-IpcClient([string] $Project, [string] $PipeName) {
    $outputPath = Join-Path ([System.IO.Path]::GetTempPath()) ("dotboxd-ipc-client-" + [Guid]::NewGuid().ToString("N") + ".out")
    $errorPath = Join-Path ([System.IO.Path]::GetTempPath()) ("dotboxd-ipc-client-" + [Guid]::NewGuid().ToString("N") + ".err")
    $arguments = @(
        "run", "--project", $Project,
        "--configuration", $Configuration,
        "--no-build", "--", $PipeName)
    $parameters = @{
        FilePath = "dotnet"
        ArgumentList = $arguments
        RedirectStandardOutput = $outputPath
        RedirectStandardError = $errorPath
        PassThru = $true
    }

    if ($IsWindows) {
        $parameters.WindowStyle = "Hidden"
    }

    $process = Start-Process @parameters
    try {
        if (-not $process.WaitForExit(30000)) {
            Stop-ProcessTree $process
            Write-CapturedOutput "IPC client example smoke test" $outputPath $errorPath
            throw "IPC client example smoke test timed out after 30 seconds."
        }

        Write-CapturedOutput "IPC client example smoke test" $outputPath $errorPath
        if ($process.ExitCode -ne 0) {
            throw "IPC client example smoke test failed with exit code $($process.ExitCode)"
        }
    } finally {
        $process.Dispose()
        Remove-Item -LiteralPath $outputPath, $errorPath -Force -ErrorAction SilentlyContinue
    }
}

function Wait-IpcServer([object] $Server) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($Server.Process.HasExited) {
            throw "IPC server exited before listening with exit code $($Server.Process.ExitCode)."
        }

        if ((Test-Path -LiteralPath $Server.OutputPath) -and
            (Select-String -LiteralPath $Server.OutputPath -Pattern "listening" -Quiet)) {
            return
        }

        Start-Sleep -Milliseconds 100
    }

    throw "IPC server did not start listening within 30 seconds."
}

$pipeName = "sir-ipc-" + [Guid]::NewGuid().ToString("N")
$ipcServer = Start-IpcServer $ipcServerExample $pipeName
try {
    Wait-IpcServer $ipcServer

    Invoke-IpcClient $ipcClientExample $pipeName
} finally {
    if (-not $ipcServer.Process.HasExited) {
        Stop-ProcessTree $ipcServer.Process
    }

    $ipcServer.Process.Dispose()
    Remove-Item -LiteralPath $ipcServer.OutputPath, $ipcServer.ErrorPath -Force -ErrorAction SilentlyContinue
}

# Game server golden example: the server self-launches the plugin child process, so the smoke
# only needs to run the server once and assert exit 0. Point the server at the built plugin dll so it
# can launch it under --no-build.
function Invoke-GameServer([string] $ServerProject, [string] $HostDll) {
    $outputPath = Join-Path ([System.IO.Path]::GetTempPath()) ("dotboxd-game-" + [Guid]::NewGuid().ToString("N") + ".out")
    $errorPath = Join-Path ([System.IO.Path]::GetTempPath()) ("dotboxd-game-" + [Guid]::NewGuid().ToString("N") + ".err")
    $arguments = @(
        "run", "--project", $ServerProject,
        "--configuration", $Configuration,
        "--no-build")
    $parameters = @{
        FilePath = "dotnet"
        ArgumentList = $arguments
        RedirectStandardOutput = $outputPath
        RedirectStandardError = $errorPath
        PassThru = $true
    }

    if ($IsWindows) {
        $parameters.WindowStyle = "Hidden"
    }

    # Start-Process inherits the parent environment; set the host dll for the launched server.
    $previousHostDll = $env:SAFEIR_GAME_PLUGIN_DLL
    $env:SAFEIR_GAME_PLUGIN_DLL = $HostDll
    try {
        $process = Start-Process @parameters
    } finally {
        $env:SAFEIR_GAME_PLUGIN_DLL = $previousHostDll
    }

    try {
        if (-not $process.WaitForExit(60000)) {
            Stop-ProcessTree $process
            Write-CapturedOutput "Game server example smoke test" $outputPath $errorPath
            throw "Game server example smoke test timed out after 60 seconds."
        }

        Write-CapturedOutput "Game server example smoke test" $outputPath $errorPath
        if ($process.ExitCode -ne 0) {
            throw "Game server example smoke test failed with exit code $($process.ExitCode)."
        }
    } finally {
        $process.Dispose()
        Remove-Item -LiteralPath $outputPath, $errorPath -Force -ErrorAction SilentlyContinue
    }
}

$gamePluginDll = Join-Path $root "samples/Kernels/GameServer/DotBoxD.Kernels.Game.Plugin/bin/$Configuration/net10.0/DotBoxD.Kernels.Game.Plugin.dll"
if (-not (Test-Path -LiteralPath $gamePluginDll)) {
    throw "Game server smoke prerequisite missing: $gamePluginDll (build the solution first)."
}

Invoke-GameServer $gameServerExample $gamePluginDll

Write-Host "Docs/example smoke checks passed."
