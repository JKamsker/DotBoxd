param(
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$addendumExample = Join-Path $root "examples/Addendum/SafeIR.AddendumExamples/SafeIR.AddendumExamples.csproj"
$localPluginExample = Join-Path $root "examples/LocalPlugin/SafeIR.PluginLocal/SafeIR.PluginLocal.csproj"
$ipcServerExample = Join-Path $root "examples/PluginIpc/SafeIR.PluginIpc.Server/SafeIR.PluginIpc.Server.csproj"
$ipcClientExample = Join-Path $root "examples/PluginIpc/SafeIR.PluginIpc.Client/SafeIR.PluginIpc.Client.csproj"

& dotnet run --project $addendumExample --configuration $Configuration --no-build
if ($LASTEXITCODE -ne 0) {
    throw "Addendum example smoke test failed with exit code $LASTEXITCODE"
}

& dotnet run --project $localPluginExample --configuration $Configuration --no-build
if ($LASTEXITCODE -ne 0) {
    throw "Local plugin example smoke test failed with exit code $LASTEXITCODE"
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

$pipeName = "safe-ir-plugin-ipc-smoke-" + [Guid]::NewGuid().ToString("N")
$ipcServer = Start-IpcServer $ipcServerExample $pipeName
try {
    Wait-IpcServer $ipcServer

    & dotnet run --project $ipcClientExample --configuration $Configuration --no-build -- $pipeName
    if ($LASTEXITCODE -ne 0) {
        throw "IPC client example smoke test failed with exit code $LASTEXITCODE"
    }
} finally {
    if (-not $ipcServer.Process.HasExited) {
        $ipcServer.Process.Kill($true)
        $ipcServer.Process.WaitForExit()
    }

    $ipcServer.Process.Dispose()
    Remove-Item -LiteralPath $ipcServer.OutputPath, $ipcServer.ErrorPath -Force -ErrorAction SilentlyContinue
}

Write-Host "Docs/example smoke checks passed."
