using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;
using DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Defaults;

public sealed class AnalyzerDefaultValueEmitterMatrixTests
{
    public static TheoryData<DefaultCase> AnalyzerDefaultCases { get; } = new()
    {
        new DefaultCase(
            "primitive",
            "int amount = 7",
            "ReadAsync(int @amount = 7)",
            "int amount = 7",
            "int amount",
            "EchoAsync(int @amount = 7",
            "int amount = 7",
            "",
            "amount == 7",
            "I32(7)"),
        new DefaultCase(
            "nullable",
            "int? maybe = null",
            "ReadAsync(int? @maybe = null)",
            "int? maybe = null",
            "int? maybe",
            "EchoAsync(int? @maybe = null",
            "int? maybe = null",
            "",
            "true",
            KernelExpected: null),
        new DefaultCase(
            "enum",
            "Mode mode = Mode.Slow",
            "ReadAsync(global::Matrix.PluginServer.Game.Mode @mode = unchecked((global::Matrix.PluginServer.Game.Mode)2))",
            "Mode mode = Mode.Slow",
            "Mode mode",
            "EchoAsync(global::Matrix.Rpc.Mode @mode = unchecked((global::Matrix.Rpc.Mode)2)",
            "Mode mode = Mode.Slow",
            "",
            "mode == Mode.Slow",
            "I32(2)"),
        new DefaultCase(
            "enum-default-literal",
            "Mode mode = default",
            "ReadAsync(global::Matrix.PluginServer.Game.Mode @mode = unchecked((global::Matrix.PluginServer.Game.Mode)0))",
            "Mode mode = default",
            "Mode mode",
            "EchoAsync(global::Matrix.Rpc.Mode @mode = unchecked((global::Matrix.Rpc.Mode)0)",
            "Mode mode = default",
            "",
            "mode == default",
            "I32(0)"),
        new DefaultCase(
            "guid-default-literal",
            "Guid id = default",
            "ReadAsync(global::System.Guid @id = default)",
            "Guid id = default",
            "Guid id",
            "EchoAsync(global::System.Guid @id = default",
            "Guid id = default",
            "",
            "id == default",
            "FromGuid(global::System.Guid.Empty)"),
        new DefaultCase(
            "datetime-metadata",
            "[Optional, DateTimeConstant(0L)] DateTime when",
            "ReadAsync(global::System.DateTime @when = default)",
            "[Optional, DateTimeConstant(0L)] DateTime when",
            "DateTime when",
            "[global::System.Runtime.CompilerServices.DateTimeConstantAttribute(0L)] global::System.DateTime @when",
            "[Optional, DateTimeConstant(0L)] DateTime when",
            "",
            "when == default",
            "I64(0L)"),
        new DefaultCase(
            "decimal",
            "decimal price = 1.5m",
            "ReadAsync(decimal @price = 1.5m)",
            RpcServiceParameters: null,
            RpcKernelParameters: null,
            RpcExpected: null,
            KernelParameters: null,
            KernelInvocation: "",
            KernelPredicate: "true",
            KernelExpected: null),
        new DefaultCase(
            "optional-metadata-before-required",
            "[Optional] int optional, int required",
            "ReadAsync([global::System.Runtime.InteropServices.OptionalAttribute] int @optional, int @required)",
            "[Optional] int optional, int required",
            "int optional, int required",
            "EchoAsync([global::System.Runtime.InteropServices.OptionalAttribute] int @optional, int @required",
            "[Optional] int optional, int needed",
            "needed: 1",
            "optional == 0 && needed == 1",
            "I32(0)"),
        new DefaultCase(
            "default-attribute-before-required",
            "[Optional, DefaultParameterValue(42)] int optional, int required",
            "ReadAsync([global::System.Runtime.InteropServices.OptionalAttribute] [global::System.Runtime.InteropServices.DefaultParameterValueAttribute(42)] int @optional, int @required)",
            "[Optional, DefaultParameterValue(42)] int optional, int required",
            "int optional, int required",
            "EchoAsync([global::System.Runtime.InteropServices.OptionalAttribute] [global::System.Runtime.InteropServices.DefaultParameterValueAttribute(42)] int @optional, int @required",
            "[Optional, DefaultParameterValue(42)] int optional, int needed",
            "needed: 1",
            "optional == 42 && needed == 1",
            "I32(42)"),
    };

    [Theory]
    [MemberData(nameof(AnalyzerDefaultCases))]
    public void Rpc_client_and_plugin_server_facade_delegate_to_shared_default_emitter(DefaultCase testCase)
    {
        var (pluginServer, pluginServerCompilation) =
            PluginServerGenerationTestDriver.Run(PluginServerSource(testCase.PluginServerParameters));

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(pluginServerCompilation);
        Assert.Contains(testCase.PluginServerExpected, pluginServer, StringComparison.Ordinal);

        if (testCase.RpcServiceParameters is null ||
            testCase.RpcKernelParameters is null ||
            testCase.RpcExpected is null)
        {
            return;
        }

        var rpcSources = string.Join(
            "\n",
            PluginAnalyzerGeneratedPackageFactory.GeneratedSources(
                RpcClientSource(testCase.RpcServiceParameters, testCase.RpcKernelParameters)));
        Assert.Contains(testCase.RpcExpected, rpcSources, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AnalyzerDefaultCases))]
    public void Kernel_method_default_arguments_delegate_to_shared_default_emitter(DefaultCase testCase)
    {
        if (testCase.KernelParameters is null)
        {
            return;
        }

        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator(
            KernelMethodSource(testCase.KernelParameters, testCase.KernelInvocation, testCase.KernelPredicate),
            typeof(KernelMethodAggroEvent));
        var generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Contains("UseGeneratedChain", generated, StringComparison.Ordinal);
        if (testCase.KernelExpected is not null)
        {
            Assert.Contains(testCase.KernelExpected, generated, StringComparison.Ordinal);
        }
    }

    private static string RpcClientSource(string serviceParameters, string kernelParameters)
        => $$"""
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;

            namespace Matrix.Rpc;

            public enum Mode
            {
                Fast = 1,
                Slow = 2
            }

            public interface IEchoService
            {
                ValueTask<int> EchoAsync({{serviceParameters}});
            }

            [ServerExtension("echo", typeof(IEchoService))]
            public sealed partial class EchoKernel
            {
                public int Echo({{kernelParameters}}, HookContext ctx) => 0;
            }
            """;

    private static string KernelMethodSource(string parameters, string invocation, string predicate)
        => $$"""
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels;
            using DotBoxD.Plugins.Runtime;

            namespace Matrix.Kernel;

            public enum Mode
            {
                Fast = 1,
                Slow = 2
            }

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                    => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod.KernelMethodAggroEvent>()
                        .Where(e => Matches({{invocation}}))
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "default"));

                [KernelMethod]
                public static bool Matches({{parameters}}) => {{predicate}};
            }
            """;

    private static string PluginServerSource(string worldParameters)
        => $$"""
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Matrix.PluginServer.Game
            {
                public enum Mode
                {
                    Fast = 1,
                    Slow = 2
                }

                [RpcService]
                public interface IGameWorldAccess
                {
                    ValueTask<int> ReadAsync({{worldParameters}});
                }
            }

            namespace Matrix.PluginServer.Game.Ipc
            {
                public readonly record struct LiveSettingUpdate(string Name, string Value);

                public interface IGamePluginControlService : DotBoxD.Plugins.IServerExtensionWireClient
                {
                    ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);
                    ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default);
                    ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default);
                    ValueTask UpdateSettingsAsync(string pluginId, LiveSettingUpdate[] updates, bool atomic = false, CancellationToken ct = default);
                    ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);
                }
            }

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGeneratedExtensions
                {
                    public static Matrix.PluginServer.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");
                }
            }

            namespace Matrix.PluginServer.Plugin
            {
                using DotBoxD.Abstractions;
                using Matrix.PluginServer.Game;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """;

    public sealed record DefaultCase(
        string Name,
        string PluginServerParameters,
        string PluginServerExpected,
        string? RpcServiceParameters,
        string? RpcKernelParameters,
        string? RpcExpected,
        string? KernelParameters,
        string KernelInvocation,
        string KernelPredicate,
        string? KernelExpected)
    {
        public override string ToString() => Name;
    }
}
