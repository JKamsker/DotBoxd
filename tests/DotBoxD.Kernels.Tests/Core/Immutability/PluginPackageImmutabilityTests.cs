using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Core.Immutability;

public sealed class PluginPackageImmutabilityTests
{
    private static readonly SourceSpan Span = new(1, 1);

    [Theory]
    [InlineData("Effects")]
    [InlineData("LiveSettings")]
    [InlineData("Subscriptions")]
    public void Plugin_manifest_null_collection_inputs_report_public_parameter_name(string parameterName)
    {
        var exception = Assert.Throws<ArgumentNullException>(() => _ = CreateManifestWithNull(parameterName));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Fact]
    public void Plugin_package_rejects_null_subscription_elements_explicitly()
    {
        var manifest = new PluginManifest(
            "plugin",
            "contract",
            ExecutionMode.Auto,
            ["Cpu"],
            [],
            [null!]);

        var exception = Assert.Throws<ArgumentException>(
            () => PluginPackage.Create(manifest, EmptyModule(), new KernelEntrypoints("ShouldHandle", "Handle")));

        Assert.Equal("Subscriptions", exception.ParamName);
    }

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

    private static PluginManifest CreateManifestWithNull(string parameterName)
        => parameterName switch
        {
            "Effects" => new PluginManifest(
                "plugin",
                "contract",
                ExecutionMode.Auto,
                null!,
                [],
                []),
            "LiveSettings" => new PluginManifest(
                "plugin",
                "contract",
                ExecutionMode.Auto,
                ["Cpu"],
                null!,
                []),
            "Subscriptions" => new PluginManifest(
                "plugin",
                "contract",
                ExecutionMode.Auto,
                ["Cpu"],
                [],
                null!),
            _ => throw new ArgumentOutOfRangeException(nameof(parameterName))
        };

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
