using System.Text.RegularExpressions;

namespace DotBoxD.Kernels.Tests.Compiled.Regression;

public sealed class DocsSmokeCommandCoverageTests
{
    private static readonly Regex CommandPattern = new(
        @"dotnet\s+(?:restore|build|test|pack)\s+(?<target>[^`\s]+)|(?<script>(?:\./|\.\\)?(?:scripts|eng/scripts|eng\\scripts)[^`\s]*\.ps1)",
        RegexOptions.Compiled);

    [Fact]
    public void Spec_markdown_command_targets_resolve()
    {
        var root = RepositoryRoot();
        var specsRoot = Path.Combine(root, "docs", "Specs");
        var missing = new List<string>();

        foreach (var document in Directory.EnumerateFiles(specsRoot, "*.md", SearchOption.AllDirectories).Order())
        {
            var lines = File.ReadAllLines(document);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match match in CommandPattern.Matches(lines[i]))
                {
                    var relativePath = match.Groups["target"].Success
                        ? match.Groups["target"].Value
                        : match.Groups["script"].Value;
                    relativePath = NormalizeCommandPath(relativePath);
                    var fullPath = Path.Combine(root, relativePath);
                    if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                    {
                        missing.Add($"{Path.GetRelativePath(root, document)}:{i + 1} -> {relativePath}");
                    }
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            "Spec markdown command targets must resolve:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void Spec_manifest_update_instruction_uses_eng_scripts_path()
    {
        var script = ReadRepositoryText("eng/scripts/check-spec-manifest.ps1");

        Assert.Contains("./eng/scripts/check-spec-manifest.ps1 -Update", script, StringComparison.Ordinal);
        Assert.DoesNotContain("./scripts/check-spec-manifest.ps1 -Update", script, StringComparison.Ordinal);
    }

    private static string NormalizeCommandPath(string value)
    {
        var path = value.Trim().Trim('"', '\'');
        if (path.StartsWith("./", StringComparison.Ordinal) ||
            path.StartsWith(".\\", StringComparison.Ordinal))
        {
            path = path.Substring(2);
        }

        return path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    private static string ReadRepositoryText(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Missing repository file: {path}");
        return File.ReadAllText(path);
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
