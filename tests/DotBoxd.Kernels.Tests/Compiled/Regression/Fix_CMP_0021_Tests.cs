using System.Collections;
using System.Reflection;
using DotBoxd.Kernels.PluginLocal;
using DotBoxd.Plugins;

namespace DotBoxd.Kernels.Tests;

/// <summary>
/// Regression coverage for CMP-0021: <see cref="DotBoxd.Plugins.KernelRegistry"/> exposes only the
/// throwing per-id lookups <c>Get(string)</c> and <c>Get&lt;TState&gt;(string)</c>. There is no public
/// way to enumerate installed kernels, take a stable inventory snapshot, or non-throwingly probe
/// whether a plugin id is currently installed. A real admin/host UI therefore cannot discover which
/// plugins are installed without mirroring server state in a side dictionary.
///
/// These tests pin the expected public discoverability surface using reflection so the file compiles
/// against the current public API (it references no member the fix will add by name). They are RED
/// today because no public enumeration or non-throwing lookup member exists, and turn GREEN once an
/// inventory surface (enumeration + try-get style lookup) is added to the registry.
/// </summary>
public sealed class Fix_CMP_0021_Tests
{
    private const string InstalledPluginId = "fire-damage";
    private const string MissingPluginId = "never-installed-plugin";

    [Fact]
    public async Task Registry_exposes_a_public_enumeration_of_installed_kernels()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        var installed = await server.InstallAsync(FireDamagePluginPackage.Create());

        var inventory = EnumerateInstalledKernels(server.Kernels);

        Assert.NotNull(inventory);
        Assert.Contains(installed, inventory!);
        Assert.Contains(inventory!, k => k.Manifest.PluginId == InstalledPluginId);
        Assert.DoesNotContain(inventory!, k => k.Manifest.PluginId == MissingPluginId);
    }

    [Fact]
    public async Task Inventory_reflects_uninstall_without_a_consumer_side_dictionary()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        await server.InstallAsync(FireDamagePluginPackage.Create());

        var beforeUninstall = EnumerateInstalledKernels(server.Kernels);
        Assert.NotNull(beforeUninstall);
        Assert.Contains(beforeUninstall!, k => k.Manifest.PluginId == InstalledPluginId);

        Assert.True(server.Uninstall(InstalledPluginId));

        var afterUninstall = EnumerateInstalledKernels(server.Kernels);
        Assert.NotNull(afterUninstall);
        Assert.DoesNotContain(afterUninstall!, k => k.Manifest.PluginId == InstalledPluginId);
    }

    [Fact]
    public async Task Registry_exposes_a_public_non_throwing_lookup_for_installed_kernels()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        await server.InstallAsync(FireDamagePluginPackage.Create());

        var tryGet = FindNonThrowingLookup(typeof(KernelRegistry));

        Assert.True(
            tryGet is not null,
            "KernelRegistry must expose a public non-throwing lookup (e.g. bool TryGet(string, out InstalledKernel)).");

        Assert.True(InvokeTryGet(tryGet!, server.Kernels, InstalledPluginId, out var foundKernel));
        Assert.NotNull(foundKernel);
        Assert.Equal(InstalledPluginId, foundKernel!.Manifest.PluginId);

        Assert.False(InvokeTryGet(tryGet!, server.Kernels, MissingPluginId, out var missingKernel));
        Assert.Null(missingKernel);
    }

    /// <summary>
    /// Finds a public instance member on <see cref="KernelRegistry"/> that yields the currently
    /// installed kernels (a property or parameterless method returning an enumerable of
    /// <see cref="InstalledKernel"/>, or the registry itself being enumerable), invokes it, and
    /// returns the snapshot. Returns <c>null</c> when no such public enumeration surface exists,
    /// which is the bug this finding describes.
    /// </summary>
    private static IReadOnlyList<InstalledKernel>? EnumerateInstalledKernels(KernelRegistry registry)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

        foreach (var property in typeof(KernelRegistry).GetProperties(flags))
        {
            if (property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            if (TryMaterialize(property.GetValue(registry), out var fromProperty))
            {
                return fromProperty;
            }
        }

        foreach (var method in typeof(KernelRegistry).GetMethods(flags))
        {
            if (method.IsSpecialName || method.GetParameters().Length != 0 || method.ReturnType == typeof(void))
            {
                continue;
            }

            if (TryMaterialize(method.Invoke(registry, null), out var fromMethod))
            {
                return fromMethod;
            }
        }

        if (registry is IEnumerable registryEnumerable && TryMaterialize(registryEnumerable, out var direct))
        {
            return direct;
        }

        return null;
    }

    private static bool TryMaterialize(object? value, out IReadOnlyList<InstalledKernel> kernels)
    {
        if (value is IEnumerable<InstalledKernel> typed)
        {
            kernels = typed.ToList();
            return true;
        }

        if (value is IEnumerable sequence and not string)
        {
            var collected = new List<InstalledKernel>();
            foreach (var item in sequence)
            {
                if (item is not InstalledKernel kernel)
                {
                    kernels = Array.Empty<InstalledKernel>();
                    return false;
                }

                collected.Add(kernel);
            }

            kernels = collected;
            return true;
        }

        kernels = Array.Empty<InstalledKernel>();
        return false;
    }

    /// <summary>
    /// Finds a public instance method shaped like <c>bool TryGet(string, out InstalledKernel)</c>,
    /// which lets a host probe installation state without catching <see cref="KeyNotFoundException"/>.
    /// </summary>
    private static MethodInfo? FindNonThrowingLookup(Type registryType)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

        foreach (var method in registryType.GetMethods(flags))
        {
            if (method.ReturnType != typeof(bool))
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 2)
            {
                continue;
            }

            var keyIsString = parameters[0].ParameterType == typeof(string);
            var outIsKernel = parameters[1].IsOut &&
                parameters[1].ParameterType == typeof(InstalledKernel).MakeByRefType();

            if (keyIsString && outIsKernel)
            {
                return method;
            }
        }

        return null;
    }

    private static bool InvokeTryGet(MethodInfo method, KernelRegistry registry, string pluginId, out InstalledKernel? kernel)
    {
        var args = new object?[] { pluginId, null };
        var result = (bool)method.Invoke(registry, args)!;
        kernel = args[1] as InstalledKernel;
        return result;
    }
}
