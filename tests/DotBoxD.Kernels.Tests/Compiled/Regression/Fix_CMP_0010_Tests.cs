namespace DotBoxD.Kernels.Tests.Compiled.Regression;

/// <summary>
/// Regression coverage for CMP-0010: package metadata needed by an admin review surface must remain
/// available even though the old manifest-inspection sample is no longer maintained.
/// </summary>
public sealed class Fix_CMP_0010_Tests
{
    [Fact]
    public void Generated_package_exposes_required_capability_and_setting_range_for_review()
    {
        var package = FireDamagePluginPackage.Create();

        Assert.Contains(
            "host.message.write",
            package.Manifest.RequiredCapabilities);

        var minDamage = Assert.Single(
            package.Manifest.LiveSettings,
            setting => setting.Name == "MinDamage");
        Assert.NotNull(minDamage.Min);
        Assert.NotNull(minDamage.Max);
    }

    [Fact]
    public void Removed_manifest_inspection_sample_is_listed_as_an_example_coverage_gap()
    {
        var gaps = ReadRepositoryText("docs-site/src/content/docs/examples/coverage-gaps.md");

        Assert.Contains("Standalone package-manifest inspection examples", gaps);
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
