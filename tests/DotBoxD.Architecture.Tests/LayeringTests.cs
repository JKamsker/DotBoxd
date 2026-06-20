namespace DotBoxD.Architecture.Tests;

/// <summary>
/// Enforces the layer dependency rules that the project graph is built on. These assert against the
/// compiled assembly references, so an accidental upward or sideways <c>ProjectReference</c> that
/// the type system would happily accept still fails the build.
/// </summary>
public sealed class LayeringTests
{
    private static void AssertNoDependency(string assemblyName, params string[] forbidden)
    {
        var assembly = ArchTestSupport.Load(assemblyName);
        var references = assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var violations = forbidden.Where(references.Contains).ToArray();

        Assert.True(
            violations.Length == 0,
            $"{assemblyName} must not depend on: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Kernels_core_does_not_depend_on_upper_layers()
        => AssertNoDependency(
            "DotBoxD.Kernels",
            "DotBoxD.Services",
            "DotBoxD.Hosting",
            "DotBoxD.Hosting.Http",
            "DotBoxD.Plugins",
            "DotBoxD.Pushdown.Services",
            "DotBoxD.Transports.Tcp",
            "DotBoxD.Transports.NamedPipes",
            "DotBoxD.Codecs.MessagePack");

    [Fact]
    public void Services_rpc_stack_is_independent_of_kernels_and_hosting()
        => AssertNoDependency(
            "DotBoxD.Services",
            "DotBoxD.Kernels",
            "DotBoxD.Hosting",
            "DotBoxD.Plugins");

    [Fact]
    public void Abstractions_sit_only_above_the_kernel_core()
        => AssertNoDependency(
            "DotBoxD.Abstractions",
            "DotBoxD.Hosting",
            "DotBoxD.Plugins",
            "DotBoxD.Services");

    [Fact]
    public void Kernel_runtime_does_not_depend_on_hosting()
        => AssertNoDependency(
            "DotBoxD.Kernels.Runtime",
            "DotBoxD.Hosting",
            "DotBoxD.Hosting.Http",
            "DotBoxD.Plugins");
}
