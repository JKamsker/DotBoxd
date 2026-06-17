using System.Diagnostics;

namespace DotBoxD.Kernels.Tests.Tooling;

public sealed class ApiBaselineScriptTests
{
    [Fact]
    public async Task Normalize_api_line_preserves_double_slash_inside_string_literals()
    {
        var output = await RunProbeAsync(
            """
            $result = Normalize-ApiLine 'public const string Endpoint = "https://example.test/api";'
            [Console]::Out.Write($result)
            """);

        Assert.Equal("public const string Endpoint = \"https://example.test/api\"", output);
    }

    [Fact]
    public async Task Api_helpers_treat_internal_protected_as_public_surface()
    {
        var output = await RunProbeAsync(
            """
            $typeVisible = Test-TypeDeclarationPublic 'internal protected class HookBase'
            $member = Normalize-ApiLine 'internal protected virtual void OnHook();'
            [Console]::Out.Write("$typeVisible|$member")
            """);

        Assert.Equal("True|internal protected virtual void OnHook()", output);
    }

    private static async Task<string> RunProbeAsync(string body)
    {
        var scriptPath = Path.Combine(
            RepositoryRoot(),
            "eng",
            "scripts",
            "check-api-compat-baseline.ps1");
        var probe = Path.Combine(Path.GetTempPath(), "dotboxd-api-baseline-probe-" + Guid.NewGuid() + ".ps1");
        await File.WriteAllTextAsync(
            probe,
            $$"""
            $ErrorActionPreference = 'Stop'
            . '{{EscapePowerShellLiteral(scriptPath)}}'
            {{body}}
            """);

        try
        {
            return await RunPowerShellAsync(probe);
        }
        finally
        {
            File.Delete(probe);
        }
    }

    private static async Task<string> RunPowerShellAsync(string scriptPath)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "pwsh",
            ArgumentList = { "-NoProfile", "-NonInteractive", "-File", scriptPath },
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Failed to start pwsh.");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(process.ExitCode == 0, error);
        return output;
    }

    private static string EscapePowerShellLiteral(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

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
