using System.Reflection;

namespace DotBoxD.Architecture.Tests;

/// <summary>
/// Shared helpers for the architecture guard tests: the shipping-assembly set, a by-name loader,
/// and a repository-root locator (walks up from the test output directory to the solution file).
/// </summary>
internal static class ArchTestSupport
{
    /// <summary>Simple names of every shipping (src/) assembly. Reflection guards run over this set.</summary>
    public static readonly string[] ShippingAssemblyNames =
    [
        "DotBoxD.Kernels",
        "DotBoxD.Kernels.Runtime",
        "DotBoxD.Kernels.Validation",
        "DotBoxD.Kernels.Interpreter",
        "DotBoxD.Kernels.Compiler",
        "DotBoxD.Kernels.Verifier",
        "DotBoxD.Kernels.Serialization.Json",
        "DotBoxD.Services",
        "DotBoxD.Abstractions",
        "DotBoxD.Hosting",
        "DotBoxD.Hosting.Http",
        "DotBoxD.Plugins",
        "DotBoxD.Pushdown.Services",
        "DotBoxD.Transports.Tcp",
        "DotBoxD.Transports.NamedPipes",
        "DotBoxD.Codecs.MessagePack",
    ];

    public static Assembly Load(string simpleName) => Assembly.Load(new AssemblyName(simpleName));

    public static IReadOnlyList<Assembly> ShippingAssemblies()
        => ShippingAssemblyNames.Select(Load).ToArray();

    public static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DotBoxD.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root (no DotBoxD.slnx found walking up from the test directory).");
    }
}
