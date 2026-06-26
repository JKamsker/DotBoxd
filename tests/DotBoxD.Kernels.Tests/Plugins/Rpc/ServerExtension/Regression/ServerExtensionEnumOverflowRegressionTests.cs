using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionEnumOverflowRegressionTests
{
    [Fact]
    public void Generated_client_enum_marshalling_uses_unchecked_conversions()
    {
        var generated = string.Join("\n", PluginAnalyzerGeneratedPackageFactory.GeneratedSources("""
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
            """));

        Assert.Contains("KernelRpcValue.Int64(unchecked((long)", generated, StringComparison.Ordinal);
        Assert.Contains("unchecked((global::Sample.Huge)", generated, StringComparison.Ordinal);
    }
}
