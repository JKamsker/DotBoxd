using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Kernels.Tests.Plugins;

/// <summary>
/// KernelPackageRegistry resolves a kernel CLR type to its analyzer-generated package — by an
/// explicit registration when present, otherwise by the generator's {Kernel - "Kernel"}PluginPackage
/// naming convention via reflection.
/// </summary>
public sealed class KernelPackageRegistryTests
{
    [Fact]
    public void Resolve_finds_generated_package_by_kernel_type_via_convention()
    {
        var package = KernelPackageRegistry.Resolve<FireDamageKernel>();

        Assert.Equal("fire-damage", package.Manifest.PluginId);
    }

    [Fact]
    public void Resolve_prefers_an_explicit_registration()
    {
        var sentinel = KernelPackageRegistry.Resolve<FireDamageKernel>();
        KernelPackageRegistry.Register(typeof(ExplicitlyRegisteredKernel), () => sentinel);

        var resolved = KernelPackageRegistry.Resolve<ExplicitlyRegisteredKernel>();

        Assert.Same(sentinel, resolved);
    }

    [Fact]
    public void Explicit_registration_overrides_cached_convention_factory()
    {
        var convention = KernelPackageRegistry.Resolve<CachedConventionKernel>();
        var cached = KernelPackageRegistry.Resolve<CachedConventionKernel>();
        var sentinel = KernelPackageRegistry.Resolve<FireDamageKernel>();

        KernelPackageRegistry.Register(typeof(CachedConventionKernel), () => sentinel);
        var resolved = KernelPackageRegistry.Resolve<CachedConventionKernel>();

        Assert.Equal("cached-convention", convention.Manifest.PluginId);
        Assert.Same(convention, cached);
        Assert.Same(sentinel, resolved);
    }

    [Fact]
    public void Resolve_throws_a_clear_error_when_no_package_exists()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => KernelPackageRegistry.Resolve<UngeneratedKernel>());

        Assert.Contains("No generated package", ex.Message, StringComparison.Ordinal);
    }

    private sealed class ExplicitlyRegisteredKernel;

    private sealed class UngeneratedKernel;
}

public sealed class CachedConventionKernel;

public static class CachedConventionPluginPackage
{
    private static readonly SourceSpan Span = new(1, 1);

    public static PluginPackage Create()
    {
        var function = new SandboxFunction(
            "Handle",
            IsEntrypoint: true,
            [],
            SandboxType.Unit,
            [new ReturnStatement(new LiteralExpression(SandboxValue.Unit, Span), Span)]);
        var module = new SandboxModule(
            "cached-convention",
            SemVersion.One,
            SemVersion.One,
            [],
            [function],
            new Dictionary<string, string> { ["pluginId"] = "cached-convention" });
        var manifest = new PluginManifest(
            "cached-convention",
            "CachedConvention",
            ExecutionMode.Auto,
            ["Cpu"],
            [],
            []);

        return PluginPackage.Create(manifest, module, new KernelEntrypoints("Handle", "Handle"));
    }
}
