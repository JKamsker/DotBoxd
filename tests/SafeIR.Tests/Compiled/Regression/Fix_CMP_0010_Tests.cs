using SafeIR;
using SafeIR.PluginLocal;
using SafeIR.Plugins;

namespace SafeIR.Tests;

/// <summary>
/// Regression coverage for CMP-0010: the runnable manifest/admin inspection example
/// (<c>examples/PluginAuthoring/SafeIR.Example.PluginAuthoring/Examples/ManifestInspectionExample.cs</c>) is the
/// operator's pre-install review surface, yet it prints only live-setting name/type/default, effects,
/// and subscriptions. The docs say server owners must also review requested capabilities and live
/// setting numeric range bounds before enabling a plugin.
///
/// These tests prove (a) the omitted review data is already reachable through public package APIs and
/// (b) the maintained example source still omits both, so they stay red until the example renders the
/// full review surface (the <c>host.message.write</c> capability request and the <c>MinDamage</c>
/// range bounds).
/// </summary>
public sealed class Fix_CMP_0010_Tests
{
    private const string ExampleRelativePath =
        "examples/PluginAuthoring/SafeIR.Example.PluginAuthoring/Examples/ManifestInspectionExample.cs";

    [Fact]
    public void Generated_package_exposes_capability_request_and_setting_range_for_review()
    {
        // The data the admin review surface must show is already available on the public package.
        var package = FireDamagePluginPackage.Create();

        // Capability requests live on the module and include host.message.write.
        Assert.Contains(
            package.Module.CapabilityRequests,
            request => request.Id == "host.message.write");

        // The MinDamage live setting declares numeric range bounds that the runtime enforces.
        var minDamage = Assert.Single(
            package.Manifest.LiveSettings,
            setting => setting.Name == "MinDamage");
        Assert.NotNull(minDamage.Min);
        Assert.NotNull(minDamage.Max);
    }

    [Fact]
    public void Manifest_inspection_example_surfaces_requested_capabilities()
    {
        var example = ReadExampleSource();

        // The pre-install review surface must enumerate the package's requested capabilities so an
        // admin sees the host.message.write permission before approving the plugin.
        Assert.Contains("CapabilityRequests", example);
    }

    [Fact]
    public void Manifest_inspection_example_surfaces_live_setting_range_bounds()
    {
        var example = ReadExampleSource();

        // The review surface must print the numeric range bounds (Min/Max) for live settings such as
        // MinDamage, not just name/type/default.
        var surfacesBounds =
            example.Contains(".Min", StringComparison.Ordinal) &&
            example.Contains(".Max", StringComparison.Ordinal);

        Assert.True(
            surfacesBounds,
            "ManifestInspectionExample must print live setting Min/Max range bounds for the admin review surface.");
    }

    private static string ReadExampleSource()
        => File.ReadAllText(Path.Combine(RepositoryRoot(), ExampleRelativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SafeIR.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
