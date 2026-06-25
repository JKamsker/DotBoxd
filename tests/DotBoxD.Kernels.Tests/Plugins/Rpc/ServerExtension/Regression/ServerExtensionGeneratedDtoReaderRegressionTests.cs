using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionGeneratedDtoReaderRegressionTests
{
    private const string ComputedDtoSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Services.Attributes;
        using DotBoxD.Abstractions;

        namespace Sample;

        [DotBoxDService]
        public interface IRemoteWorldControl
        {
        }

        public sealed class RemoteWorldControl : IRemoteWorldControl, IServerExtensionClientAccessor
        {
            public RemoteWorldControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                => ServerExtensions = serverExtensions;

            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        public readonly record struct Point(int X, int Y)
        {
            public int Sum => X + Y;
        }

        [ServerExtension(typeof(IRemoteWorldControl), "point")]
        public sealed partial class PointKernel
        {
            [ServerExtensionMethod(typeof(IRemoteWorldControl))]
            public Point Read(int x, HookContext ctx)
            {
                return new Point(x, 4);
            }
        }

        public static class Probe
        {
            public static Point Read(RemoteWorldControl control, int x) => control.Read(x);
        }
        """;

    private const string InheritedFieldDtoSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        public class BaseInfo
        {
            public int Id;
        }

        public sealed class DerivedInfo : BaseInfo
        {
            public int Extra;
        }

        [ServerExtension("inherited-field")]
        public sealed partial class InheritedFieldKernel
        {
            public int Use(DerivedInfo info, HookContext ctx)
            {
                return info.Extra;
            }
        }
        """;

    private const string ConstructorAndInitializerDtoSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Services.Attributes;
        using DotBoxD.Abstractions;

        namespace Sample;

        [DotBoxDService]
        public interface IRemoteWorldControl
        {
        }

        public sealed class RemoteWorldControl : IRemoteWorldControl, IServerExtensionClientAccessor
        {
            public RemoteWorldControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                => ServerExtensions = serverExtensions;

            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        public sealed class Profile
        {
            public Profile()
            {
            }

            public Profile(int Health) => this.Health = Health;

            public Profile(int Health, int Rank)
            {
                this.Health = Health;
                this.Rank = Rank;
            }

            public int Health { get; set; }
            public int Rank { get; set; }
            public string Name { get; set; } = "";
        }

        [ServerExtension(typeof(IRemoteWorldControl), "profile")]
        public sealed partial class ProfileKernel
        {
            [ServerExtensionMethod(typeof(IRemoteWorldControl))]
            public Profile Read(int x, HookContext ctx)
            {
                return new Profile { Health = x, Rank = 9, Name = "hero" };
            }
        }

        public static class Probe
        {
            public static Profile Read(RemoteWorldControl control, int x) => control.Read(x);
        }
        """;

    [Fact]
    public void Direct_extension_reconstructs_a_dto_with_a_computed_get_only_member()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(ComputedDtoSource);
        var control = CreateControl(
            assembly,
            KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record(
            [
                KernelRpcValue.Int32(3),
                KernelRpcValue.Int32(4),
                KernelRpcValue.Int32(7)
            ])));

        var point = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control, 3])!;

        var type = point.GetType();
        Assert.Equal(3, type.GetProperty("X")!.GetValue(point));
        Assert.Equal(4, type.GetProperty("Y")!.GetValue(point));
        Assert.Equal(7, type.GetProperty("Sum")!.GetValue(point));
    }

    [Fact]
    public void Direct_extension_reconstructs_a_dto_with_constructor_and_settable_members()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(ConstructorAndInitializerDtoSource);
        var control = CreateControl(
            assembly,
            KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record(
            [
                KernelRpcValue.Int32(3),
                KernelRpcValue.Int32(9),
                KernelRpcValue.String("hero")
            ])));

        var profile = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control, 3])!;

        var type = profile.GetType();
        Assert.Equal(3, type.GetProperty("Health")!.GetValue(profile));
        Assert.Equal(9, type.GetProperty("Rank")!.GetValue(profile));
        Assert.Equal("hero", type.GetProperty("Name")!.GetValue(profile));
    }

    [Fact]
    public void Server_extension_rejects_a_dto_that_inherits_public_fields()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(InheritedFieldDtoSource);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("inherit public fields", StringComparison.Ordinal));
    }

    private static object CreateControl(Assembly assembly, byte[] response)
    {
        var controlType = assembly.GetType("Sample.RemoteWorldControl", throwOnError: true)!;
        return Activator.CreateInstance(controlType, [new RecordingRegistry(response)])!;
    }

    private sealed class RecordingRegistry(byte[] response) : DotBoxD.Plugins.IServerExtensionClientRegistry
    {
        public string PluginId<TService>()
            where TService : class
            => "point";

        public ValueTask<byte[]> InvokeServerExtensionAsync(
            string pluginId,
            byte[] arguments,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(response);
        }
    }
}
