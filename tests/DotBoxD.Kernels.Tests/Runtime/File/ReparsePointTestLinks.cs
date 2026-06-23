using System.Diagnostics;

namespace DotBoxD.Kernels.Tests.Runtime.File;

internal static class ReparsePointTestLinks
{
    public static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (IOException)
        {
            return TryCreateDirectoryJunction(link, target);
        }
        catch (UnauthorizedAccessException)
        {
            return TryCreateDirectoryJunction(link, target);
        }
        catch (PlatformNotSupportedException)
        {
            return TryCreateDirectoryJunction(link, target);
        }
    }

    public static bool TryCreateFileLink(string link, string target)
    {
        try
        {
            System.IO.File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }

    public static void TryDeleteDirectoryLink(string link)
    {
        try
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static void TryDeleteFileLink(string link)
    {
        try
        {
            if (System.IO.File.Exists(link))
            {
                System.IO.File.Delete(link);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool TryCreateDirectoryJunction(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var process = Process.Start(new ProcessStartInfo(
            "cmd.exe",
            $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        });
        process?.WaitForExit();
        return process?.ExitCode == 0 && Directory.Exists(link);
    }
}

internal sealed class TempDirectory : IDisposable
{
    private TempDirectory(string path) => Path = path;

    public string Path { get; }

    public static TempDirectory Create()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dotboxd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new TempDirectory(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
