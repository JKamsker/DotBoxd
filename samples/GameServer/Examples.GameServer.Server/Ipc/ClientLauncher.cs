using System.Diagnostics;

namespace DotBoxD.Kernels.Game.Server.Ipc;

internal static class ClientLauncher
{
    private const string ClientDllEnvVar = "DOTBOXD_GAME_CLIENT_DLL";
    private const string ClientProjectDir = "Examples.GameServer.Client";
    private const string ServerProjectDir = "Examples.GameServer.Server";
    private const string ClientDllName = "Examples.GameServer.Client.dll";

    public static Process Launch(int port, string pluginsRoot)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(ResolveClientDll());
        startInfo.ArgumentList.Add("--connect");
        startInfo.ArgumentList.Add("127.0.0.1");
        startInfo.ArgumentList.Add(port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--plugins");
        startInfo.ArgumentList.Add(pluginsRoot);

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

    private static string ResolveClientDll()
    {
        var configured = Environment.GetEnvironmentVariable(ClientDllEnvVar);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return File.Exists(configured)
                ? configured
                : throw new FileNotFoundException($"{ClientDllEnvVar} points to a missing client dll: {configured}");
        }

        return SiblingClientPath(AppContext.BaseDirectory) is { } candidate && File.Exists(candidate)
            ? candidate
            : throw new FileNotFoundException($"Could not resolve {ClientDllName}. Build the solution first.");
    }

    private static string? SiblingClientPath(string serverBaseDirectory)
    {
        var trimmed = serverBaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var tfm = Path.GetFileName(trimmed);
        var configDir = Path.GetDirectoryName(trimmed);
        var binDir = configDir is null ? null : Path.GetDirectoryName(configDir);
        var serverProjectDir = binDir is null ? null : Path.GetDirectoryName(binDir);
        if (configDir is null ||
            serverProjectDir is null ||
            !string.Equals(Path.GetFileName(serverProjectDir), ServerProjectDir, StringComparison.Ordinal))
        {
            return null;
        }

        var root = Path.GetDirectoryName(serverProjectDir);
        return root is null
            ? null
            : Path.Combine(root, ClientProjectDir, "bin", Path.GetFileName(configDir), tfm, ClientDllName);
    }
}
