using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientDecodeSurpriseTests
{
    [Fact]
    public void Generated_client_rejects_finite_double_results_that_overflow_float()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(FloatSource);
        var control = CreateControl(assembly, "float", KernelRpcValue.Double(double.MaxValue));
        var probe = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("ReadFloat", BindingFlags.Public | BindingFlags.Static)!;

        var ex = Assert.Throws<TargetInvocationException>(() => probe.Invoke(null, [control]));

        Assert.IsType<NotSupportedException>(ex.InnerException);
    }

    [Fact]
    public void Generated_client_rejects_narrow_enum_results_outside_underlying_range()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(ByteEnumSource);
        var control = CreateControl(assembly, "byte-enum", KernelRpcValue.Int32(300));
        var probe = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("ReadByte", BindingFlags.Public | BindingFlags.Static)!;

        var ex = Assert.Throws<TargetInvocationException>(() => probe.Invoke(null, [control]));

        Assert.IsType<NotSupportedException>(ex.InnerException);
    }

    private static object CreateControl(Assembly assembly, string expectedPluginId, KernelRpcValue response)
    {
        var controlType = assembly.GetType("Sample.RemoteControl", throwOnError: true)!;
        return Activator.CreateInstance(
            controlType,
            [new RecordingRegistry(expectedPluginId, KernelRpcBinaryCodec.EncodeValue(response))])!;
    }

    private const string FloatSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Services.Attributes;
        using DotBoxD.Abstractions;

        namespace Sample;

        [DotBoxDService]
        public interface IRemoteControl
        {
        }

        public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
        {
            public RemoteControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                => ServerExtensions = serverExtensions;

            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        [ServerExtension(typeof(IRemoteControl), "float")]
        public sealed partial class FloatKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl))]
            public float ReadFloat(HookContext ctx) => 0f;
        }

        public static class Probe
        {
            public static float ReadFloat(RemoteControl control) => control.ReadFloat();
        }
        """;

    private const string ByteEnumSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Services.Attributes;
        using DotBoxD.Abstractions;

        namespace Sample;

        [DotBoxDService]
        public interface IRemoteControl
        {
        }

        public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
        {
            public RemoteControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                => ServerExtensions = serverExtensions;

            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        public enum Small : byte
        {
            Zero = 0,
            FortyFour = 44
        }

        [ServerExtension(typeof(IRemoteControl), "byte-enum")]
        public sealed partial class ByteKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl))]
            public Small ReadByte(HookContext ctx) => Small.Zero;
        }

        public static class Probe
        {
            public static Small ReadByte(RemoteControl control) => control.ReadByte();
        }
        """;

    private sealed class RecordingRegistry(string expectedPluginId, byte[] response)
        : DotBoxD.Plugins.IServerExtensionClientRegistry
    {
        public string PluginId<TService>()
            where TService : class
            => expectedPluginId;

        public ValueTask<byte[]> InvokeServerExtensionAsync(
            string pluginId,
            byte[] arguments,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(expectedPluginId, pluginId);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(response);
        }
    }
}
