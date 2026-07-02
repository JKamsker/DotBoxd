using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Samples.GameServer;

public sealed class GameServerReadinessTests
{
    private const string ClientTimeoutMilliseconds = "1000";
    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task GameServer_exits_when_plugin_process_never_reaches_readiness()
    {
        var fakePlugin = BuildNonConnectingPlugin();
        using var process = StartGameServer(fakePlugin);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        try
        {
            var exit = process.WaitForExitAsync();
            if (await Task.WhenAny(exit, Task.Delay(ProcessExitTimeout)) != exit)
            {
                KillProcessTree(process);
                var output = await CapturedOutputAsync(stdout, stderr);
                Assert.Fail("Game server did not exit when the plugin stayed alive without connecting." + output);
            }

            Assert.Equal(1, process.ExitCode);
            Assert.Contains(
                "client did not connect",
                await CapturedOutputAsync(stdout, stderr),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            KillProcessTree(process);
        }
    }

    [Fact]
    public async Task GameServer_exits_when_ready_plugin_never_disconnects_after_shutdown()
    {
        var fakePlugin = BuildShutdownIgnoringPlugin();
        using var process = StartGameServer(fakePlugin);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        try
        {
            var exit = process.WaitForExitAsync();
            if (await Task.WhenAny(exit, Task.Delay(ProcessExitTimeout)) != exit)
            {
                KillProcessTree(process);
                var output = await CapturedOutputAsync(stdout, stderr);
                Assert.Fail("Game server did not exit when the ready plugin ignored shutdown." + output);
            }

            Assert.Equal(1, process.ExitCode);
            Assert.Contains(
                "client did not shut down",
                await CapturedOutputAsync(stdout, stderr),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            KillProcessTree(process);
        }
    }

    private static Process StartGameServer(string fakePlugin)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(GameServerAssemblyPath());
        startInfo.Environment["DOTBOXD_GAME_CLIENT_DLL"] = fakePlugin;
        startInfo.Environment["DOTBOXD_GAME_CLIENT_CONNECT_TIMEOUT_MS"] = ClientTimeoutMilliseconds;
        startInfo.Environment["DOTBOXD_GAME_CLIENT_SHUTDOWN_TIMEOUT_MS"] = ClientTimeoutMilliseconds;

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start GameServer sample process.");
    }

    private static string BuildNonConnectingPlugin()
        => BuildPlugin(
            "NonConnectingPlugin",
            """
            using System;
            using System.Threading.Tasks;

            public static class Program
            {
                public static async Task<int> Main(string[] args)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30));
                    return 0;
                }
            }
            """);

    private static string BuildShutdownIgnoringPlugin()
        => BuildPlugin(
            "ShutdownIgnoringPlugin",
            """
            using System;
            using System.Globalization;
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Kernels.Game.Server.Abstractions.Ipc;
            using DotBoxD.Pushdown.Services;
            using DotBoxD.Services.Server;
            using DotBoxD.Transports.Tcp;

            public static class Program
            {
                public static async Task<int> Main(string[] args)
                {
                    var host = args[1];
                    var port = int.Parse(args[2], CultureInfo.InvariantCulture);
                    await using var connection = await RpcMessagePackIpc.ConnectAsync(
                        new TcpTransport(host, port),
                        peer => peer.Provide((IServiceDispatcher)new PluginEventCallbackDispatcher(new Callback())));
                    var control = connection.Get<IGameClientControlService>();
                    await control.HoldUntilShutdownAsync();
                    await Task.Delay(TimeSpan.FromSeconds(30));
                    return 0;
                }

                private sealed class Callback : IPluginEventCallback
                {
                    public ValueTask OnEventAsync(
                        string subscriptionId,
                        ReadOnlyMemory<byte> projectedValue,
                        CancellationToken ct = default)
                        => ValueTask.CompletedTask;

                    public ValueTask<byte[]> OnResultAsync(
                        string subscriptionId,
                        ReadOnlyMemory<byte> contextValue,
                        CancellationToken ct = default)
                        => ValueTask.FromResult(Array.Empty<byte>());
                }
            }
            """);

    private static string BuildPlugin(string assemblyName, string source)
    {
        var directory = Path.Combine(Path.GetTempPath(), "dotboxd-" + assemblyName + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var outputPath = Path.Combine(directory, assemblyName + ".dll");
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = TrustedPlatformReferencePaths()
            .Concat(DotBoxDReferencePaths())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        var emit = compilation.Emit(outputPath);
        Assert.True(
            emit.Success,
            "Failed to compile fake plugin: " + string.Join(Environment.NewLine, emit.Diagnostics));
        WriteRuntimeConfig(outputPath);
        CopyRuntimeDependencies(directory, outputPath);
        return outputPath;
    }

    private static IEnumerable<string> TrustedPlatformReferencePaths()
        => ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator);

    private static IEnumerable<string> DotBoxDReferencePaths()
    {
        var directories = new[]
        {
            AppContext.BaseDirectory,
            Path.GetDirectoryName(GameServerAssemblyPath())!
        };

        return directories
            .SelectMany(directory => Directory.GetFiles(directory, "DotBoxD*.dll"))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static void CopyRuntimeDependencies(string directory, string outputPath)
    {
        foreach (var dependency in RuntimeDependencyPaths())
        {
            if (string.Equals(dependency, outputPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Copy(dependency, Path.Combine(directory, Path.GetFileName(dependency)), overwrite: true);
        }
    }

    private static IEnumerable<string> RuntimeDependencyPaths()
    {
        var directories = new[]
        {
            AppContext.BaseDirectory,
            Path.GetDirectoryName(GameServerAssemblyPath())!
        };

        return directories
            .SelectMany(directory => Directory.GetFiles(directory, "*.dll"))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static void WriteRuntimeConfig(string assemblyPath)
    {
        var version = Environment.Version;
        File.WriteAllText(
            Path.ChangeExtension(assemblyPath, ".runtimeconfig.json"),
            $$"""
            {
              "runtimeOptions": {
                "tfm": "net{{version.Major}}.0",
                "framework": {
                  "name": "Microsoft.NETCore.App",
                  "version": "{{version.Major}}.{{version.Minor}}.{{version.Build}}"
                }
              }
            }
            """);
    }

    private static async Task<string> CapturedOutputAsync(Task<string> stdout, Task<string> stderr)
        => Environment.NewLine +
           "--- stdout ---" + Environment.NewLine +
           await stdout.ConfigureAwait(false) +
           Environment.NewLine +
           "--- stderr ---" + Environment.NewLine +
           await stderr.ConfigureAwait(false);

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string GameServerAssemblyPath()
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        var configuration = output.Parent!.Name;
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "samples",
            "GameServer",
            "Examples.GameServer.Server",
            "bin",
            configuration,
            "net10.0",
            "Examples.GameServer.Server.dll"));
    }
}
