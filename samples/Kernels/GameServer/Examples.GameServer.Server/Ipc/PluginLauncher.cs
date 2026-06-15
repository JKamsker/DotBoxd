using System.Diagnostics;

namespace DotBoxD.Kernels.Game.Server.Ipc;

/// <summary>
/// Launches the plugin child process and forwards its stdout/stderr so the demo shows the plugin's
/// ship logs inline. Resolves the plugin dll for both <c>dotnet run</c> (dev) and <c>--no-build</c>
/// smoke runs.
/// </summary>
internal static class PluginLauncher
{
    private const string PluginDllEnvVar = "SAFEIR_GAME_PLUGIN_DLL";
    private const string PluginProjectDir = "Examples.GameServer.Plugin";
    private const string ServerProjectDir = "Examples.GameServer.Server";
    private const string PluginDllName = "Examples.GameServer.Plugin.dll";

    public static Process Launch(string pipeName, bool useBuilder)
    {
        var pluginDll = ResolvePluginDll();
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(pluginDll);
        startInfo.ArgumentList.Add(pipeName);
        if (useBuilder)
        {
            startInfo.ArgumentList.Add("--use-builder");
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => ForwardLine(Console.Out, e.Data);
        process.ErrorDataReceived += (_, e) => ForwardLine(Console.Error, e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static void ForwardLine(TextWriter writer, string? line)
    {
        if (line is not null)
        {
            writer.WriteLine(line);
        }
    }

    private static string ResolvePluginDll()
    {
        var configured = Environment.GetEnvironmentVariable(PluginDllEnvVar);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!File.Exists(configured))
            {
                throw new FileNotFoundException(
                    $"{PluginDllEnvVar} points to a missing plugin dll: {configured}");
            }

            return configured;
        }

        // Server base dir: .../samples/Kernels/GameServer/Examples.GameServer.Server/bin/<Config>/net10.0/
        // Sibling plugin:  .../samples/Kernels/GameServer/Examples.GameServer.Plugin/bin/<Config>/net10.0/<dll>
        var serverBase = AppContext.BaseDirectory;
        var candidate = SiblingPluginPath(serverBase);
        if (candidate is not null && File.Exists(candidate))
        {
            return candidate;
        }

        throw new FileNotFoundException(
            "Could not resolve the plugin dll. Build the solution or set " +
            $"{PluginDllEnvVar} to {PluginDllName}. Looked beside: {serverBase}");
    }

    private static string? SiblingPluginPath(string serverBaseDirectory)
    {
        var trimmed = serverBaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        // trimmed -> .../Examples.GameServer.Server/bin/<Config>/net10.0
        var tfm = Path.GetFileName(trimmed);
        var configDir = Path.GetDirectoryName(trimmed);
        var binDir = Path.GetDirectoryName(configDir);
        var serverProjectDir = Path.GetDirectoryName(binDir);
        if (configDir is null || binDir is null || serverProjectDir is null)
        {
            return null;
        }

        if (!string.Equals(Path.GetFileName(serverProjectDir), ServerProjectDir, StringComparison.Ordinal))
        {
            return null;
        }

        var config = Path.GetFileName(configDir);
        var gameServerRoot = Path.GetDirectoryName(serverProjectDir);
        if (gameServerRoot is null)
        {
            return null;
        }

        return Path.Combine(gameServerRoot, PluginProjectDir, "bin", config, tfm, PluginDllName);
    }
}
