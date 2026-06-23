using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

/// <summary>The proxy decodes the kernel result into this; its <c>Sum</c> is recomputed from X and Y on decode.</summary>
public readonly record struct ConstructedLocation(int X, int Y)
{
    public int Sum => X + Y;
}

public interface ILocationMakeService
{
    ConstructedLocation Make(int x, int y);
}

public interface ILocationSumService
{
    int MakeAndReadSum(int x, int y);
}

/// <summary>
/// A kernel may construct a record whose wire shape carries a derived/get-only member (<c>Sum =&gt; X + Y</c>)
/// even though the constructor only takes X and Y. The analyzer fills the derived slot by lowering the member's
/// getter over the constructor arguments. This proves both halves end to end: the constructed record round-trips
/// with the right derived value, and an in-sandbox read of the derived member sees X+Y (so the slot holds the
/// lowered getter, not a placeholder default). Before the fix, constructing such a record was a DBXK100 error.
/// </summary>
public sealed class ServerExtensionComputedPropertyConstructionTests
{
    // One batch method per server extension, so the make/read cases are separate kernels over a shared record.
    private const string Source = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public readonly record struct Location(int X, int Y)
        {
            public int Sum => X + Y;
        }

        [ServerExtension("location-make")]
        public sealed partial class LocationMakeKernel
        {
            public Location Make(int x, int y, HookContext ctx)
            {
                return new Location(x, y);
            }
        }

        [ServerExtension("location-sum")]
        public sealed partial class LocationSumKernel
        {
            public int MakeAndReadSum(int x, int y, HookContext ctx)
            {
                var location = new Location(x, y);
                return location.Sum;
            }
        }
        """;

    [Fact]
    public async Task A_kernel_constructs_and_returns_a_record_with_a_derived_member()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(Source, "Sample.LocationMakePluginPackage");
        using var server = PluginServer.Create();
        var kernel = await server.InstallServerExtensionAsync(package);
        var service = ServerExtensionProxy.Create<ILocationMakeService>(kernel);

        var location = service.Make(3, 4);

        Assert.Equal(3, location.X);
        Assert.Equal(4, location.Y);
        Assert.Equal(7, location.Sum);
    }

    [Fact]
    public async Task A_kernel_reads_the_derived_member_off_a_record_it_just_constructed()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(Source, "Sample.LocationSumPluginPackage");
        using var server = PluginServer.Create();
        var kernel = await server.InstallServerExtensionAsync(package);
        var service = ServerExtensionProxy.Create<ILocationSumService>(kernel);

        // The derived slot must hold the lowered getter (X+Y), not a default — read it back from inside the sandbox.
        Assert.Equal(7, service.MakeAndReadSum(3, 4));
        Assert.Equal(11, service.MakeAndReadSum(5, 6));
    }
}
