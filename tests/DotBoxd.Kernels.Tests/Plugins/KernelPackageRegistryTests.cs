using DotBoxd.Kernels.PluginLocal;
using DotBoxd.Plugins;

namespace DotBoxd.Kernels.Tests;

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
    public void Resolve_throws_a_clear_error_when_no_package_exists()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => KernelPackageRegistry.Resolve<UngeneratedKernel>());

        Assert.Contains("No generated package", ex.Message, StringComparison.Ordinal);
    }

    private sealed class ExplicitlyRegisteredKernel;

    private sealed class UngeneratedKernel;
}
