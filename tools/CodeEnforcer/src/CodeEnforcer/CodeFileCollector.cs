using System.Diagnostics;

namespace CodeEnforcer;

internal static class CodeFileCollector
{
    public static IReadOnlyList<CodeFile> Collect(string root)
    {
        List<CodeFile> files = [];
        foreach (string path in ListTrackedCSharpFiles(root))
        {
            string normalizedPath = PathUtility.Normalize(path);
            if (ShouldSkip(normalizedPath))
            {
                continue;
            }

            string fullPath = Path.Combine(root, normalizedPath);
            files.Add(new CodeFile(normalizedPath, File.ReadLines(fullPath).Count()));
        }

        return files;
    }

    internal static bool ShouldSkip(string path) =>
        path.Contains("/bin/", StringComparison.Ordinal) ||
        path.Contains("/obj/", StringComparison.Ordinal) ||
        path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ListTrackedCSharpFiles(string root)
    {
        ProcessStartInfo startInfo = new("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(root);
        startInfo.ArgumentList.Add("ls-files");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("*.cs");

        using Process process = Process.Start(startInfo)
            ?? throw new CodeEnforcerException("Failed to start git.", ExitCodes.InternalError);
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new CodeEnforcerException("git ls-files failed: " + error.Trim(), ExitCodes.InputError);
        }

        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    }
}
