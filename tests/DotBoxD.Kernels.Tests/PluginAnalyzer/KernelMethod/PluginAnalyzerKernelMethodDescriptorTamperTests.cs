using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod;

public sealed partial class PluginAnalyzerKernelMethodDescriptorTests
{
    [Fact]
    public void Prebuilt_sdk_descriptor_capabilities_are_recomputed_from_descriptor_source()
    {
        var sdkReference = CompilePlainReference(ForgedDescriptorSdkSource(), "ForgedKernelMethodDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(
            PluginAnalyzerKernelMethodDescriptorTestSources.DescriptorlessConsumer.Replace(
                "using Descriptorless;",
                "using Sdk;",
                StringComparison.Ordinal).Replace(
                "ctx.IsClose(e.Distance)",
                "ctx.IsAllowed(e.MonsterId, e.PlayerLevel)",
                StringComparison.Ordinal),
            sdkReference);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains(
                "does not match recomputed host requirements",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Prebuilt_sdk_descriptor_rejects_indirect_expression_source()
    {
        var sdkReference = CompilePlainReference(
            ForgedDescriptorSdkSource("global::Sdk.Evil.Build(__dotboxd_kernel_method_arg_0__)"),
            "ForgedKernelMethodDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(
            PluginAnalyzerKernelMethodDescriptorTestSources.DescriptorlessConsumer.Replace(
                "using Descriptorless;",
                "using Sdk;",
                StringComparison.Ordinal).Replace(
                "ctx.IsClose(e.Distance)",
                "ctx.IsAllowed(e.MonsterId, e.PlayerLevel)",
                StringComparison.Ordinal),
            sdkReference);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains(
                "contains unsupported expression source",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Prebuilt_sdk_descriptor_rejects_return_shape_mismatch()
    {
        var sdkReference = CompilePlainReference(
            ForgedDescriptorSdkSource("I32(1)"),
            "ForgedKernelMethodDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(
            PluginAnalyzerKernelMethodDescriptorTestSources.DescriptorlessConsumer.Replace(
                "using Descriptorless;",
                "using Sdk;",
                StringComparison.Ordinal).Replace(
                "ctx.IsClose(e.Distance)",
                "ctx.IsAllowed(e.MonsterId, e.PlayerLevel)",
                StringComparison.Ordinal),
            sdkReference);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains(
                "has stale return metadata",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Prebuilt_sdk_descriptor_rejects_foreign_call_expression_source()
    {
        var sdkReference = CompilePlainReference(
            ForgedDescriptorSdkSource(
                "new global::Sdk.CallExpression(\"host.Sdk.IGameWorld.Read\", " +
                "[__dotboxd_kernel_method_arg_0__], null, Span)"),
            "ForgedKernelMethodDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(
            PluginAnalyzerKernelMethodDescriptorTestSources.DescriptorlessConsumer.Replace(
                "using Descriptorless;",
                "using Sdk;",
                StringComparison.Ordinal).Replace(
                "ctx.IsClose(e.Distance)",
                "ctx.IsAllowed(e.MonsterId, e.PlayerLevel)",
                StringComparison.Ordinal),
            sdkReference);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains(
                "contains unsupported expression source",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Prebuilt_sdk_descriptor_rejects_call_expression_object_initializer()
    {
        var sdkReference = CompilePlainReference(
            ForgedDescriptorSdkSource(
                "new global::DotBoxD.Kernels.CallExpression(\"host.Sdk.IGameWorld.Read\", " +
                "[__dotboxd_kernel_method_arg_0__], null, Span) " +
                "{ Name = \"host.Sdk.IGameWorld.Name\", Arguments = [], GenericType = null }"),
            "ForgedInitializerKernelMethodDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(
            PluginAnalyzerKernelMethodDescriptorTestSources.DescriptorlessConsumer.Replace(
                "using Descriptorless;",
                "using Sdk;",
                StringComparison.Ordinal).Replace(
                "ctx.IsClose(e.Distance)",
                "ctx.IsAllowed(e.MonsterId, e.PlayerLevel)",
                StringComparison.Ordinal),
            sdkReference);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains(
                "contains unsupported expression source",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Prebuilt_sdk_descriptor_rejects_host_call_outside_context_world_selector()
    {
        var sdkReference = CompilePlainReference(
            ForgedDescriptorSdkSource(
                "new global::DotBoxD.Kernels.CallExpression(\"host.Sdk.ISecretWorld.Secret\", " +
                "[__dotboxd_kernel_method_arg_0__], null, Span)"),
            "ForgedSecretWorldKernelMethodDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(
            PluginAnalyzerKernelMethodDescriptorTestSources.DescriptorlessConsumer.Replace(
                "using Descriptorless;",
                "using Sdk;",
                StringComparison.Ordinal).Replace(
                "ctx.IsClose(e.Distance)",
                "ctx.IsAllowed(e.MonsterId, e.PlayerLevel)",
                StringComparison.Ordinal),
            sdkReference);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains(
                "contains an untrusted host call 'host.Sdk.ISecretWorld.Secret'",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Prebuilt_sdk_descriptor_rejects_duplicate_root_payload_member()
    {
        var sdkReference = CompilePlainReference(
            ForgedDescriptorSdkSource(
                mutatePayload: json => json.Insert(json.Length - 1, ",\"source\":\"I32(1)\"")),
            "DuplicateRootKernelMethodDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(
            PluginAnalyzerKernelMethodDescriptorTestSources.DescriptorlessConsumer.Replace(
                "using Descriptorless;",
                "using Sdk;",
                StringComparison.Ordinal).Replace(
                "ctx.IsClose(e.Distance)",
                "ctx.IsAllowed(e.MonsterId, e.PlayerLevel)",
                StringComparison.Ordinal),
            sdkReference);

        AssertMalformedDescriptor(diagnostics);
    }

    [Fact]
    public void Prebuilt_sdk_descriptor_rejects_extra_parameter_payload_member()
    {
        var sdkReference = CompilePlainReference(
            ForgedDescriptorSdkSource(
                mutatePayload: json => json.Replace(
                    "\"placeholder\":\"__dotboxd_kernel_method_arg_0__\"",
                    "\"placeholder\":\"__dotboxd_kernel_method_arg_0__\",\"extra\":\"ignored\"",
                    StringComparison.Ordinal)),
            "ExtraParameterKernelMethodDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(
            PluginAnalyzerKernelMethodDescriptorTestSources.DescriptorlessConsumer.Replace(
                "using Descriptorless;",
                "using Sdk;",
                StringComparison.Ordinal).Replace(
                "ctx.IsClose(e.Distance)",
                "ctx.IsAllowed(e.MonsterId, e.PlayerLevel)",
                StringComparison.Ordinal),
            sdkReference);

        AssertMalformedDescriptor(diagnostics);
    }

    private static void AssertMalformedDescriptor(IEnumerable<Microsoft.CodeAnalysis.Diagnostic> diagnostics)
        => Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains(
                "is malformed or has a stale hash",
                StringComparison.Ordinal));

    private static string ForgedDescriptorSdkSource(
        string? sourceOverride = null,
        string? extraSdkDeclarations = null,
        Func<string, string>? mutatePayload = null)
    {
        var payload = new KernelMethodDescriptorPayload(
            KernelMethodDescriptorPayload.CurrentVersion,
            "global::Sdk.GamePluginContext",
            "IsAllowed",
            "bool IsAllowed(string,int)",
            DotBoxDGenerationNames.ManifestTypes.Bool,
            false,
            new EquatableArray<string>([]),
            new EquatableArray<string>([]),
            new EquatableArray<KernelMethodDescriptorParameter>(
            [
                new("__dotboxd_kernel_method_arg_0__", DotBoxDGenerationNames.ManifestTypes.String),
                new("__dotboxd_kernel_method_arg_1__", DotBoxDGenerationNames.ManifestTypes.Int)
            ]),
            sourceOverride ??
            "new global::DotBoxD.Kernels.CallExpression(\"host.Sdk.IGameWorld.Read\", " +
                "[__dotboxd_kernel_method_arg_0__], null, Span)");
        var json = payload.ToJson();
        json = mutatePayload?.Invoke(json) ?? json;
        var hash = KernelMethodDescriptorPayload.Hash(json);
        return $$"""
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Services.Attributes;

            [assembly: global::DotBoxD.Abstractions.GeneratedKernelMethodDescriptorAttribute(
                {{KernelMethodDescriptorPayload.CurrentVersion}},
                typeof(global::Sdk.GamePluginContext),
                "IsAllowed",
                "bool IsAllowed(string,int)",
                {{KernelMethodDescriptorPayload.JsonString(hash)}},
                {{KernelMethodDescriptorPayload.JsonString(json)}})]

            namespace Sdk
            {
                [RpcService]
                public interface IGameWorld
                {
                    [HostBinding("sample.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                    int Read(string id) => throw new System.NotSupportedException("metadata-only world");
                }

                [RpcService]
                public interface ISecretWorld
                {
                    [HostBinding("sample.secret.read", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                    int Secret(string id) => throw new System.NotSupportedException("metadata-only world");
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
                    public bool IsAllowed(string id, int threshold)
                        => throw new System.NotSupportedException("metadata-only descriptor");
                }

                public static class Evil
                {
                    public static global::DotBoxD.Kernels.Expression Build(string id)
                        => throw new System.NotSupportedException("descriptor indirection");
                }

                {{extraSdkDeclarations}}
            }
            """;
    }

}
