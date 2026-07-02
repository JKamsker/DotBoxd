using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod;

public sealed partial class PluginAnalyzerKernelMethodDescriptorTests
{
    [Fact]
    public void Prebuilt_sdk_descriptor_accepts_host_return_allocation_metadata()
    {
        var sdkReference = CompilePlainReference(
            ForgedAllocatingReturnDescriptorSdkSource(),
            "ForgedAllocatingKernelMethodDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(AllocatingReturnConsumerSource(), sdkReference);

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains(
                "has stale return metadata",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            diagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static string ForgedAllocatingReturnDescriptorSdkSource()
    {
        var payload = new KernelMethodDescriptorPayload(
            KernelMethodDescriptorPayload.CurrentVersion,
            "global::Sdk.GamePluginContext",
            "Name",
            "string Name(string)",
            DotBoxDGenerationNames.ManifestTypes.String,
            true,
            new EquatableArray<string>(["sample.read.name"]),
            new EquatableArray<string>(["Cpu", "Alloc", "HostStateRead"]),
            new EquatableArray<KernelMethodDescriptorParameter>(
            [
                new("__dotboxd_kernel_method_arg_0__", DotBoxDGenerationNames.ManifestTypes.String)
            ]),
            "new global::DotBoxD.Kernels.CallExpression(\"host.Sdk.IGameWorld.Name\", " +
                "[__dotboxd_kernel_method_arg_0__], null, Span)");
        var json = payload.ToJson();
        var hash = KernelMethodDescriptorPayload.Hash(json);
        return $$"""
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Services.Attributes;

            [assembly: global::DotBoxD.Abstractions.GeneratedKernelMethodDescriptorAttribute(
                {{KernelMethodDescriptorPayload.CurrentVersion}},
                typeof(global::Sdk.GamePluginContext),
                "Name",
                "string Name(string)",
                {{KernelMethodDescriptorPayload.JsonString(hash)}},
                {{KernelMethodDescriptorPayload.JsonString(json)}})]

            namespace Sdk
            {
                [RpcService]
                public interface IGameWorld
                {
                    [HostBinding("sample.read.name", SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.HostStateRead)]
                    string Name(string id) => throw new System.NotSupportedException("metadata-only world");
                }

                [GeneratePluginServer(Context = typeof(GamePluginContext))]
                public abstract class GamePluginServer : IGameWorld
                {
                }

                public sealed class GamePluginContext
                {
                    private readonly HookContext _raw;
                    public GamePluginContext(HookContext raw) => _raw = raw;
                    public HookContext Raw => _raw;
                    public IPluginMessageSink Messages => _raw.Messages;
                    public IGameWorld World => _raw.Host<IGameWorld>();
                    public static GamePluginContext FromHookContext(HookContext raw) => new(raw);

                    [KernelMethod]
                    public string Name(string id)
                        => throw new System.NotSupportedException("metadata-only descriptor");
                }
            }
            """;
    }

    private static string AllocatingReturnConsumerSource()
        => """
            using DotBoxD.Plugins.Runtime;
            using Sdk;

            namespace Consumer;

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod.KernelMethodAggroEvent, GamePluginContext>(GamePluginContext.FromHookContext)
                        .Where((e, ctx) => ctx.Name(e.MonsterId) == "monster-1")
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));
            }
            """;
}
