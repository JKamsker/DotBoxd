using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Core.Immutability;

public sealed class PluginPackageImmutabilityTests
{
    private static readonly SourceSpan Span = new(1, 1);

    [Fact]
    public void Plugin_package_snapshots_manifest_and_module_parts()
    {
        var subscription = new HookSubscriptionManifest("DamageEvent", "Kernel")
        {
            IndexedPredicates =
            [
                new IndexedPredicate("Damage", IndexPredicateOperator.GreaterThan, 5, "int")
            ]
        };
        var manifest = new PluginManifest(
            "plugin",
            "contract",
            ExecutionMode.Auto,
            ["Cpu"],
            [],
            [subscription])
        {
            RequiredCapabilities = ["host.message.send"]
        };
        var module = EmptyModule();
        var entrypoints = new KernelEntrypoints("ShouldHandle", "Handle");

        var package = PluginPackage.Create(manifest, module, entrypoints);

        Assert.NotSame(manifest, package.Manifest);
        Assert.NotSame(module, package.Module);
        Assert.NotSame(entrypoints, package.Entrypoints);
        Assert.NotSame(subscription, package.Manifest.Subscriptions[0]);
        Assert.NotSame(subscription.IndexedPredicates, package.Manifest.Subscriptions[0].IndexedPredicates);
        Assert.Equal("host.message.send", package.Manifest.RequiredCapabilities.Single());
    }

    private static SandboxModule EmptyModule()
        => new(
            "module",
            SemVersion.One,
            SemVersion.One,
            [],
            [
                new SandboxFunction(
                    "main",
                    true,
                    [],
                    SandboxType.I32,
                    [new ReturnStatement(new LiteralExpression(SandboxValue.FromInt32(1), Span), Span)])
            ],
            new Dictionary<string, string>());
}
