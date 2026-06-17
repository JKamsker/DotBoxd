namespace DotBoxD.Kernels.Tests.Hosting.Regression;

public sealed class Fix_API_0023_Tests
{
    [Fact]
    public void Abstractions_baseline_records_multiline_generic_method_constraints()
    {
        var baseline = BaselineEntries("DotBoxD.Abstractions");

        Assert.Contains(
            "public THost Host<THost>() where THost : class",
            baseline);
    }

    [Fact]
    public void Plugins_baseline_records_all_multiline_generic_method_constraints()
    {
        var baseline = BaselineEntries("DotBoxD.Plugins");

        Assert.Contains(
            baseline,
            entry => entry.Contains("RegisterRpcServiceAsync<TService, TKernel>", StringComparison.Ordinal) &&
                     entry.Contains("where TService : class", StringComparison.Ordinal) &&
                     entry.Contains("where TKernel : class", StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> BaselineEntries(string packageId)
    {
        var path = Path.Combine(RepositoryRoot(), "docs", "api-baselines", $"{packageId}.txt");
        Assert.True(File.Exists(path), $"Missing regenerated baseline: {path}");

        return File.ReadAllLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line) &&
                           !line.TrimStart().StartsWith('#'))
            .ToArray();
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
