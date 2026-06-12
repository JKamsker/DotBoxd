param(
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$addendumExample = Join-Path $root "examples/Addendum/SafeIR.AddendumExamples/SafeIR.AddendumExamples.csproj"
$localPluginExample = Join-Path $root "examples/LocalPlugin/SafeIR.PluginLocal/SafeIR.PluginLocal.csproj"
$ipcServerExample = Join-Path $root "examples/PluginIpc/SafeIR.PluginIpc.Server/SafeIR.PluginIpc.Server.csproj"
$ipcClientExample = Join-Path $root "examples/PluginIpc/SafeIR.PluginIpc.Client/SafeIR.PluginIpc.Client.csproj"

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
        $line = $lines[$i].Trim()
        if ($line -match '^dotnet\s+(restore|build|test|pack)\s+(?<target>\S+)') {
            Assert-ExistingPath $Path ($i + 1) $matches["target"]
            continue
        }

        if ($line -match '^dotnet\s+run\s+--project\s+(?<project>\S+)') {
            Assert-ExistingPath $Path ($i + 1) $matches["project"]
            continue
        }

        if ($line -match '^\.(?<script>\\scripts\\\S+\.ps1)') {
            Assert-ExistingPath $Path ($i + 1) ("." + $matches["script"])
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
    $outputPath = Join-Path ([System.IO.Path]::GetTempPath()) ("safe-ir-smoke-" + [Guid]::NewGuid().ToString("N") + ".out")
    $errorPath = Join-Path ([System.IO.Path]::GetTempPath()) ("safe-ir-smoke-" + [Guid]::NewGuid().ToString("N") + ".err")
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

Test-DocumentCommands (Join-Path $root "README.md")
Test-DocumentCommands (Join-Path $root "docs/Specs/Addendum/Examples.md")

Assert-DocsDoNotContain "Sandbox\.Parse" "JSON IR import is Sandbox.ImportJson"
Assert-DocsDoNotContain "tenant://123/config" "file grants use canonical filesystem roots"
Assert-DocsDoNotContain "Proposed Public C# API" "public API document is no longer proposed"
Assert-DocsDoNotContain "Proposed C# API surface" "public API index is no longer proposed"
Assert-DocsDoNotContain "Add compiler/cache after the core model is proven" "compiled mode is implemented"

Invoke-DotNetProject "Addendum example smoke test" @("run", "--project", $addendumExample, "--configuration", $Configuration, "--no-build")
Invoke-DotNetProject "Local plugin example smoke test" @("run", "--project", $localPluginExample, "--configuration", $Configuration, "--no-build")

if (-not $IsWindows) {
    Write-Host "Skipping named-pipe IPC smoke on non-Windows runners."
    Write-Host "Docs/example smoke checks passed."
    return
}

function Start-IpcServer([string] $Project, [string] $PipeName) {
    $outputPath = Join-Path ([System.IO.Path]::GetTempPath()) ("safe-ir-ipc-server-" + [Guid]::NewGuid().ToString("N") + ".out")
    $errorPath = Join-Path ([System.IO.Path]::GetTempPath()) ("safe-ir-ipc-server-" + [Guid]::NewGuid().ToString("N") + ".err")
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
    $outputPath = Join-Path ([System.IO.Path]::GetTempPath()) ("safe-ir-ipc-client-" + [Guid]::NewGuid().ToString("N") + ".out")
    $errorPath = Join-Path ([System.IO.Path]::GetTempPath()) ("safe-ir-ipc-client-" + [Guid]::NewGuid().ToString("N") + ".err")
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

$pipeName = "sir-ipc-" + [Guid]::NewGuid().ToString("N").Substring(0, 12)
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

Write-Host "Docs/example smoke checks passed."
