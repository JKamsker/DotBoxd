namespace DotBoxd.Kernels.Tests;

public sealed class PluginAdapterDocumentationTests
{
    [Fact]
    public void Flagship_plugin_docs_and_examples_register_the_damage_event_adapter()
    {
        var root = RepositoryRoot();
        Assert.Contains(
            "server.RegisterEventAdapter(DamageEventAdapter.Instance);",
            File.ReadAllText(Path.Combine(root, "docs", "Specs", "Addendum", "Addendum.md")));
        Assert.Contains(
            "server.RegisterEventAdapter(DamageEventAdapter.Instance);",
            File.ReadAllText(Path.Combine(root, "docs", "Specs", "Addendum", "Examples.md")));
        Assert.Contains(
            "server.RegisterEventAdapter(DamageEventAdapter.Instance);",
            File.ReadAllText(Path.Combine(root, "samples", "Kernels", "LocalPlugin", "DotBoxd.Kernels.PluginLocal", "Program.cs")));
        Assert.Contains(
            "server.RegisterEventAdapter(DamageEventAdapter.Instance);",
            File.ReadAllText(Path.Combine(
                root,
                "samples", "Pushdown", "PluginIpc",
                "DotBoxd.Kernels.PluginIpc.Server",
                "PluginControlService.cs")));
    }

    [Fact]
    public void Addendum_examples_call_out_convention_adapters_as_development_convenience()
    {
        var examples = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "docs",
            "Specs",
            "Addendum",
            "Examples.md"));

        Assert.Contains("Convention/discovery adapters are a development convenience", examples);
        Assert.Contains("explicit server-owned whitelist", examples);
    }

    [Fact]
    public void Message_writing_examples_use_explicit_message_policy()
    {
        var root = RepositoryRoot();
        Assert.Contains(
            "defaultPolicy: PluginMessagePolicy()",
            File.ReadAllText(Path.Combine(root, "docs", "Specs", "Addendum", "Examples.md")));
        Assert.Contains(
            "defaultPolicy: PluginPolicy()",
            File.ReadAllText(Path.Combine(root, "samples", "Kernels", "LocalPlugin", "DotBoxd.Kernels.PluginLocal", "Program.cs")));
        Assert.Contains(
            "defaultPolicy: PluginExamplePolicies.MessageWrite()",
            File.ReadAllText(Path.Combine(
                root,
                "samples", "Kernels",
                "PluginAuthoring",
                "DotBoxd.Kernels.Example.PluginAuthoring",
                "Examples",
                "KernelClassExample.cs")));
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
