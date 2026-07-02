param(
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$gameServerExample = Join-Path $root "samples/GameServer/Examples.GameServer.Server/Examples.GameServer.Server.csproj"
$pluginsRoot = Join-Path $root "samples/GameServer/plugins"

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

        if ($line -match '^dotnet\s+run\b.*\s--project\s+(?<project>\S+)') {
            Assert-ExistingPath $Path ($i + 1) $matches["project"]
            continue
        }

        if ($line -match '^\.(?<script>\\scripts\\\S+\.ps1)') {
            Assert-ExistingPath $Path ($i + 1) ("." + $matches["script"])
        }
    }
}

function Assert-DocsDoNotContain([string] $Pattern, [string] $Description) {
    Assert-DocumentsDoNotContain (Get-ChildItem -LiteralPath (Join-Path $root "docs/Specs") -Recurse -File -Filter "*.md") $Pattern $Description
}

function Assert-DocumentsDoNotContain([System.IO.FileInfo[]] $Documents, [string] $Pattern, [string] $Description) {
    $documents = @($Documents | Where-Object { $_ -ne $null })
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

function Assert-ExportedGameServerBundles {
    $expected = @(
        "guardian/server/hooks/guardian.json",
        "guardian/server/subscriptions/retaliation.json",
        "bounty-hunter/server/extensions/bounty-payout.json",
        "bounty-hunter/client/extensions/bounty-claim.json",
        "bounty-hunter/client/hooks/monster-death-fx.json",
        "bounty-hunter/client/subscriptions/gold-hud.json",
        "bounty-hunter/client/assets/skull.anim.txt",
        "gold-cheat/server/extensions/gold-cheat.json",
        "gold-cheat/client/extensions/gold-cheat.json"
    )

    foreach ($relative in $expected) {
        $path = Join-Path $pluginsRoot ($relative.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
        if (-not (Test-Path -LiteralPath $path)) {
            throw "GameServer exported bundle missing: $relative (build the solution first)."
        }
    }
}

function Assert-OutputContains([string] $Output, [string] $Marker) {
    if (-not $Output.Contains($Marker, [System.StringComparison]::Ordinal)) {
        throw "GameServer example smoke output did not contain marker: $Marker"
    }
}

function Invoke-GameServer([string] $ServerProject) {
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

    $process = Start-Process @parameters

    try {
        if (-not $process.WaitForExit(60000)) {
            Stop-ProcessTree $process
            Write-CapturedOutput "GameServer example smoke test" $outputPath $errorPath
            throw "GameServer example smoke test timed out after 60 seconds."
        }

        Write-CapturedOutput "GameServer example smoke test" $outputPath $errorPath
        if ($process.ExitCode -ne 0) {
            throw "GameServer example smoke test failed with exit code $($process.ExitCode)."
        }

        $output = Read-TextIfExists $outputPath
        Assert-OutputContains $output "listening on"
        Assert-OutputContains $output "client connected"
        Assert-OutputContains $output "bounty: paid"
        Assert-OutputContains $output "=== SUMMARY ==="
    } finally {
        $process.Dispose()
        Remove-Item -LiteralPath $outputPath, $errorPath -Force -ErrorAction SilentlyContinue
    }
}

Test-DocumentCommands (Join-Path $root "README.md")
Test-DocumentCommands (Join-Path $root "CONTRIBUTING.md")
Test-DocumentCommands (Join-Path $root "docs-site/src/content/docs/getting-started.md")
Test-DocumentCommands (Join-Path $root "docs/Specs/Addendum/Examples.md")

Assert-DocsDoNotContain "Sandbox\.Parse" "JSON IR import is Sandbox.ImportJson"
Assert-DocsDoNotContain "tenant://123/config" "file grants use canonical filesystem roots"
Assert-DocsDoNotContain "Proposed Public C# API" "public API document is no longer proposed"
Assert-DocsDoNotContain "Proposed C# API surface" "public API index is no longer proposed"
Assert-DocsDoNotContain "Add compiler/cache after the core model is proven" "compiled mode is implemented"

$pluginFluentDocs = @(
    "docs/design/plugin-fluent-hooks-api/server-walkthrough.md",
    "docs/design/plugin-fluent-hooks-api/plugin-walkthrough.md",
    "docs/design/plugin-fluent-hooks-api/kernel-binding-model.md",
    "docs/design/remote-plugin-server-builder/interface-driven-plugin-server.md"
) | ForEach-Object { Get-Item -LiteralPath (Resolve-RepoPath $_) }

$currentServerExtensionDocs = @(
    "README.md",
    "docs-site/src/content/docs/overview.md",
    "docs-site/src/content/docs/getting-started.md",
    "docs-site/src/content/docs/concepts/pushdown.md",
    "docs/Specs/Addendum/Examples.md",
    "docs/design/plugin-fluent-hooks-api/followups.md",
    "docs/design/remote-plugin-server-builder/invoke-async.md"
) | ForEach-Object { Get-Item -LiteralPath (Resolve-RepoPath $_) }

foreach ($document in $pluginFluentDocs) {
    Test-DocumentCommands $document.FullName
}

Assert-DocumentsDoNotContain $pluginFluentDocs "GameWorld\.CreateDefault\(server\.Hooks\)" "GameWorld.CreateDefault now receives hooks and subscriptions"
Assert-DocumentsDoNotContain $pluginFluentDocs "DBXK110.*DBXK114" "unsupported hook-chain diagnostics are DBXK111-DBXK116"
Assert-DocumentsDoNotContain $pluginFluentDocs "server\.Events\.On" "server hook surface is server.Hooks"
Assert-DocumentsDoNotContain $pluginFluentDocs "server\.Kernels\.(Register|Get)" "generated server surface no longer uses server.Kernels"
Assert-DocumentsDoNotContain $pluginFluentDocs "InvokeKernel|InvokeLocal" "old kernel invocation terminology is stale"
Assert-DocumentsDoNotContain $pluginFluentDocs "SetValuesAsync" "live-settings API uses generated update flow"

Assert-DocumentsDoNotContain $currentServerExtensionDocs "KernelRpcService" "server extensions use ServerExtensionAttribute"
Assert-DocumentsDoNotContain $currentServerExtensionDocs "KernelRpcClientMethod" "server-extension clients use ServerExtensionMethodAttribute"
Assert-DocumentsDoNotContain $currentServerExtensionDocs "RegisterKernelRpcService" "server extensions register through RegisterServerExtensionAsync"
Assert-DocumentsDoNotContain $currentServerExtensionDocs "RegisterRpcServiceAsync" "server extensions register through RegisterServerExtensionAsync"
Assert-DocumentsDoNotContain $currentServerExtensionDocs "RpcService<" "server extensions use ServerExtension<T>"
Assert-DocumentsDoNotContain $currentServerExtensionDocs "server\.KernelRpc" "generated server surface no longer uses server.KernelRpc"
Assert-DocumentsDoNotContain $currentServerExtensionDocs "SetupKernelRpc" "builder docs use SetupServerExtensions"
Assert-DocumentsDoNotContain $currentServerExtensionDocs "RemoteKernelRpcControl" "builder docs use RemoteServerExtensionControl"
Assert-DocumentsDoNotContain $currentServerExtensionDocs "KernelRpcRegistrationAccumulator" "builder docs use ServerExtensionRegistrationAccumulator"
Assert-DocumentsDoNotContain $currentServerExtensionDocs "kernel RPC" "current docs call this server extensions"
Assert-DocumentsDoNotContain $currentServerExtensionDocs "KernelRpcMarshaller" "current server-extension docs avoid legacy KernelRpcMarshaller terminology"
Assert-DocumentsDoNotContain $currentServerExtensionDocs "KernelRpcValue" "current server-extension docs avoid legacy KernelRpcValue terminology"
Assert-DocumentsDoNotContain $currentServerExtensionDocs "KernelRpcBinaryCodec" "current server-extension docs avoid legacy KernelRpcBinaryCodec terminology"

Assert-ExportedGameServerBundles

if (-not $IsWindows) {
    Write-Host "Skipping GameServer runtime smoke on non-Windows runners."
    Write-Host "Docs/static smoke checks passed; GameServer runtime smoke was skipped."
    return
}

Invoke-GameServer $gameServerExample

Write-Host "Docs/example smoke checks passed."
