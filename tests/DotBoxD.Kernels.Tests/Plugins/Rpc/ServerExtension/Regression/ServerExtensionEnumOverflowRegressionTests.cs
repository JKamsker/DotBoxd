using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionEnumOverflowRegressionTests
{
    [Fact]
    public void Generated_client_enum_marshalling_uses_unchecked_conversions()
    {
        var generated = string.Join("\n", PluginAnalyzerGeneratedPackageFactory.GeneratedSources(HugeEnumSource));

        Assert.Contains("KernelRpcValue.Int64(unchecked((long)", generated, StringComparison.Ordinal);
        Assert.Contains("unchecked((global::Sample.Huge)", generated, StringComparison.Ordinal);
        Assert.Contains("__bits != 18446744073709551615UL", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_client_decodes_declared_high_bit_ulong_enum_result()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(HugeEnumSource);
        var hugeType = assembly.GetType("Sample.Huge", throwOnError: true)!;
        var top = Enum.Parse(hugeType, "Top");
        var control = Activator.CreateInstance(
            assembly.GetType("Sample.RemoteControl", throwOnError: true)!,
            [new RecordingRegistry("huge", KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Int64(-1)))])!;

        var result = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Echo", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control, top]);

        Assert.Equal("Top", result?.ToString());
    }

    private const string HugeEnumSource = """
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Services.Attributes;
            using DotBoxD.Abstractions;

            namespace Sample;

            [DotBoxDService]
            public interface IRemoteControl;

            public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
            {
                public RemoteControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                    => ServerExtensions = serverExtensions;

                public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
            }

            public enum Huge : ulong
            {
                Top = ulong.MaxValue
            }

            [ServerExtension(typeof(IRemoteControl), "huge")]
            public sealed partial class HugeKernel
            {
                [ServerExtensionMethod(typeof(IRemoteControl))]
                public Huge Echo(Huge value, HookContext ctx) => value;
            }

            public static class Probe
            {
                public static Huge Echo(RemoteControl control, Huge value) => control.Echo(value);
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
