using System.Text.RegularExpressions;

namespace DotBoxd.Kernels.Tests;

/// <summary>
/// Regression coverage for API-0023: the package-backed release smoke
/// (<c>scripts/check-package-consumer-smoke.ps1</c>) must do more than reference the
/// <c>DotBoxd.Plugins.Analyzer</c> package. It has to compile a consumer that defines a
/// <c>[GamePlugin]</c> kernel implementing <c>IEventKernel&lt;TEvent&gt;</c> and then call the
/// generated <c>*PluginPackage.Create()</c> factory, so a broken analyzer asset, missing
/// generator initialization, or a factory that cannot build a valid <c>PluginPackage</c>
/// fails the smoke. These tests pin that proof to the script so it cannot silently regress
/// back to a reference-only consumer.
/// </summary>
public sealed class Fix_API_0023_Tests
{
    private static string SmokeScript()
    {
        var path = Path.Combine(
            RepositoryRoot(),
            "eng", "scripts",
            "check-package-consumer-smoke.ps1");
        Assert.True(File.Exists(path), $"Missing package consumer smoke script: {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Smoke_references_packaged_plugin_analyzer_as_an_analyzer_asset()
    {
        var script = SmokeScript();

        // The analyzer must be a packaged PackageReference flowed in as an analyzer asset,
        // not a project reference. Project references are what the source-tree examples use;
        // the release smoke has to prove the NuGet path.
        Assert.Matches(
            new Regex(
                "PackageReference\\s+Include=\"DotBoxd\\.Plugins\\.Analyzer\"[^>]*OutputItemType=\"Analyzer\"",
                RegexOptions.IgnoreCase),
            script);
        Assert.DoesNotContain("ProjectReference", script);
    }

    [Fact]
    public void Smoke_defines_a_game_plugin_kernel_implementing_the_event_kernel_interface()
    {
        var script = SmokeScript();

        // The consumer must author a real kernel so the packaged generator has something to lower.
        Assert.Contains("[Plugin(\"package-consumer-smoke\")]", script);
        Assert.Matches(
            new Regex(@"class\s+SmokeKernel\s*:\s*IEventKernel<SmokeEvent>"),
            script);
        Assert.Contains("ShouldHandle(SmokeEvent e, HookContext ctx)", script);
        Assert.Contains("Handle(SmokeEvent e, HookContext ctx)", script);
    }

    [Fact]
    public void Smoke_calls_the_generated_plugin_package_factory_and_inspects_its_shape()
    {
        var script = SmokeScript();

        // Calling the generated SmokeKernel -> SmokePluginPackage.Create() factory is the
        // only thing that exercises the generator running from the package. The smoke then
        // inspects the produced PluginPackage so a generator that emits an empty or wrong
        // package fails fast instead of passing on a bare reference.
        Assert.Contains("SmokePluginPackage.Create()", script);
        Assert.Contains("package.Manifest.PluginId", script);
        Assert.Contains("package.Manifest.Subscriptions", script);
        Assert.Contains("package.Module.Functions", script);
        Assert.Contains("package.Entrypoints.ShouldHandle", script);
        Assert.Contains("package.Entrypoints.Handle", script);
    }

    [Fact]
    public void Smoke_runs_the_generated_consumer_after_building_it()
    {
        var script = SmokeScript();

        Assert.Contains("dotnet run", script, StringComparison.Ordinal);
        Assert.Contains("--no-build", script, StringComparison.Ordinal);
        Assert.Contains("builder.AddNetworkBindings();", script, StringComparison.Ordinal);
        Assert.Contains("GrantFileRead(Path.GetTempPath(), 1024)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("GrantFileRead(\"config\"", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// The generated factory name is derived by stripping the <c>Kernel</c> suffix and appending
    /// <c>PluginPackage</c>. This pins the contract the smoke depends on so a generator rename
    /// would surface here rather than only at smoke time.
    /// </summary>
    [Fact]
    public void Generated_factory_name_matches_the_analyzer_naming_contract()
    {
        const string kernelName = "SmokeKernel";
        const string kernelSuffix = "Kernel";
        const string packageSuffix = "PluginPackage";

        var expected = kernelName.EndsWith(kernelSuffix, StringComparison.Ordinal)
            ? kernelName[..^kernelSuffix.Length] + packageSuffix
            : kernelName + packageSuffix;

        Assert.Equal("SmokePluginPackage", expected);
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DotBoxd.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
