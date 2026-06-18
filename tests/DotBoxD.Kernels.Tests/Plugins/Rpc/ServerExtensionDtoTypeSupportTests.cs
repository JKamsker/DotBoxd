using System.Reflection;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public enum MarshalColor
{
    Red,
    Green,
    Blue
}

public enum MarshalBigFlag : long
{
    None = 0,
    High = 5_000_000_000
}

/// <summary>
/// Broader server-extension type support beyond the original record-DTO parameters (issue #41 follow-up):
/// enums marshal through their underlying integer, DTOs reconstructed via settable properties when no matching
/// constructor exists, and a DTO that inherits public properties is rejected with a direct diagnostic instead
/// of silently dropping the inherited fields.
/// </summary>
public sealed class ServerExtensionDtoTypeSupportTests
{
    private const string EnumEchoSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed class RemoteWorldControl : IServerExtensionClientAccessor
        {
            public RemoteWorldControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions) => ServerExtensions = serverExtensions;
            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        public enum Color { Red, Green, Blue }

        [ServerExtension(typeof(RemoteWorldControl), "color-echo")]
        public sealed partial class ColorEchoKernel
        {
            [ServerExtensionMethod(typeof(RemoteWorldControl))]
            public Color Echo(Color color, HookContext ctx)
            {
                return color;
            }
        }

        public static class Probe
        {
            public static Color Echo(RemoteWorldControl control, Color color) => control.Echo(color);
        }
        """;

    private const string InitOnlyDtoSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed class RemoteWorldControl : IServerExtensionClientAccessor
        {
            public RemoteWorldControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions) => ServerExtensions = serverExtensions;
            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        public sealed class Profile
        {
            public int Id { get; init; }
            public string Name { get; init; }
        }

        [ServerExtension(typeof(RemoteWorldControl), "profile")]
        public sealed partial class ProfileKernel
        {
            [ServerExtensionMethod(typeof(RemoteWorldControl))]
            public Profile Describe(int id, HookContext ctx)
            {
                return new Profile { Id = id, Name = "hero" };
            }
        }

        public static class Probe
        {
            public static Profile Describe(RemoteWorldControl control, int id) => control.Describe(id);
        }
        """;

    private const string InheritedDtoSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        public record BaseInfo(int Id);
        public record DerivedInfo(int Id, int Extra) : BaseInfo(Id);

        [ServerExtension("derived")]
        public sealed partial class DerivedKernel
        {
            public int Use(DerivedInfo info, HookContext ctx)
            {
                return info.Extra;
            }
        }
        """;

    [Fact]
    public void Marshaller_round_trips_enums_via_their_underlying_integer()
    {
        Assert.Equal(SandboxValue.FromInt32(1), KernelRpcMarshaller.ToSandboxValue(MarshalColor.Green, typeof(MarshalColor)));
        Assert.Equal(MarshalColor.Green, KernelRpcMarshaller.FromSandboxValue(SandboxValue.FromInt32(1), typeof(MarshalColor)));
        Assert.Equal(SandboxType.I32, KernelRpcMarshaller.SandboxTypeOf(typeof(MarshalColor)));

        Assert.Equal(SandboxValue.FromInt64(5_000_000_000L), KernelRpcMarshaller.ToSandboxValue(MarshalBigFlag.High, typeof(MarshalBigFlag)));
        Assert.Equal(MarshalBigFlag.High, KernelRpcMarshaller.FromSandboxValue(SandboxValue.FromInt64(5_000_000_000L), typeof(MarshalBigFlag)));
        Assert.Equal(SandboxType.I64, KernelRpcMarshaller.SandboxTypeOf(typeof(MarshalBigFlag)));
    }

    [Fact]
    public void Direct_extension_round_trips_an_enum_parameter_and_return()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(EnumEchoSource);
        var colorType = assembly.GetType("Sample.Color", throwOnError: true)!;
        var green = Enum.ToObject(colorType, 1);
        var control = CreateControl(assembly, KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Int32(1)), out var registry);

        var result = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Echo", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control, green]);

        // Written to the wire as the underlying I32.
        var arguments = KernelRpcBinaryCodec.DecodeArguments(registry.LastArguments);
        Assert.Equal(1, Assert.Single(arguments).Int32Value);
        // Read back from the wire as the enum value.
        Assert.Equal(green, result);
        Assert.Equal(colorType, result!.GetType());
    }

    [Fact]
    public void Direct_extension_reconstructs_a_dto_with_init_only_properties()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(InitOnlyDtoSource);
        var control = CreateControl(
            assembly,
            KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record(
            [
                KernelRpcValue.Int32(7),
                KernelRpcValue.String("hero")
            ])),
            out _);

        var profile = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Describe", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control, 7])!;

        var type = profile.GetType();
        Assert.Equal(7, type.GetProperty("Id")!.GetValue(profile));
        Assert.Equal("hero", type.GetProperty("Name")!.GetValue(profile));
    }

    [Fact]
    public void Server_extension_rejects_a_dto_that_inherits_public_properties()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(InheritedDtoSource);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" && d.GetMessage().Contains("inherit", StringComparison.Ordinal));
    }

    private static object CreateControl(Assembly assembly, byte[] response, out RecordingRegistry registry)
    {
        var controlType = assembly.GetType("Sample.RemoteWorldControl", throwOnError: true)!;
        registry = new RecordingRegistry(response);
        return Activator.CreateInstance(controlType, [registry])!;
    }

    private sealed class RecordingRegistry(byte[] response) : DotBoxD.Plugins.IServerExtensionClientRegistry
    {
        public byte[] LastArguments { get; private set; } = [];

        public string PluginId<TService>()
            where TService : class
            => "type-support";

        public ValueTask<byte[]> InvokeServerExtensionAsync(
            string pluginId,
            byte[] arguments,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastArguments = arguments;
            return ValueTask.FromResult(response);
        }
    }
}
