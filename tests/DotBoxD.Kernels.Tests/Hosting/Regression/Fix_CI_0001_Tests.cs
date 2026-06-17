using System.Diagnostics;

namespace DotBoxD.Kernels.Tests.Hosting.Regression;

public sealed class Fix_CI_0001_Tests
{
    [Fact]
    public async Task Csharp_file_line_gate_scans_repo_sources_not_only_eng()
    {
        var probePath = Path.Combine(RepositoryRoot(), "src", "CodeEnforcerOverLimitProbe.cs");
        try
        {
            await File.WriteAllLinesAsync(
                probePath,
                Enumerable.Range(0, 351).Select(i => $"// probe {i}"));

            using var process = StartLineGuard();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var exitTask = process.WaitForExitAsync();
            if (await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(15))) != exitTask)
            {
                KillProcess(process);
                Assert.Fail("CodeEnforcer did not finish while scanning an over-limit probe file.");
            }

            await exitTask;
            var output = await outputTask;
            var error = await errorTask;

            Assert.NotEqual(
                0,
                process.ExitCode);
            Assert.Contains("CodeEnforcerOverLimitProbe.cs", output + error, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(probePath))
            {
                File.Delete(probePath);
            }
        }
    }

    private static Process StartLineGuard()
    {
        var startInfo = new ProcessStartInfo("pwsh")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = RepositoryRoot()
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(Path.Combine("eng", "scripts", "check-csharp-file-lines.ps1"));
        startInfo.ArgumentList.Add("-FailAt");
        startInfo.ArgumentList.Add("350");
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start PowerShell.");
    }

    private static void KillProcess(Process process)
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

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DotBoxD.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
