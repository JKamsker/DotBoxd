using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod;

public sealed partial class PluginAnalyzerKernelMethodDescriptorTests
{
    [Fact]
    public void Prebuilt_sdk_descriptor_rejects_foreign_real_generate_plugin_server_selector()
    {
        var sdkReference = CompilePlainReference(
            ForgedForeignSelectorDescriptorSdkSource(),
            "ForgedForeignSelectorKernelMethodDescriptorSdk");
        var foreignCompilation = CreateCompilation(
            ForeignRealSelectorSource(),
            "ForeignRealSelector",
            sdkReference);
        Assert.Empty(foreignCompilation.GetDiagnostics().Where(
            static diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var foreignReference = EmitReference(foreignCompilation);
        var diagnostics = GeneratorDiagnostics(
            PluginAnalyzerKernelMethodDescriptorTestSources.DescriptorlessConsumer.Replace(
                "using Descriptorless;",
                "using Sdk;",
                StringComparison.Ordinal).Replace(
                "ctx.IsClose(e.Distance)",
                "ctx.IsAllowed(e.MonsterId, e.PlayerLevel)",
                StringComparison.Ordinal),
            sdkReference,
            foreignReference);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains(
                "contains an untrusted host call 'host.Foreign.ISecretWorld.Secret'",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Prebuilt_sdk_descriptor_rejects_duplicate_context_selector_facade()
    {
        var sdkReference = CompilePlainReference(
            ForgedDescriptorSdkSource(
                "new global::DotBoxD.Kernels.CallExpression(\"host.Sdk.ISecretWorld.Secret\", " +
                "[__dotboxd_kernel_method_arg_0__], null, Span)",
                """
                [GeneratePluginServer(Context = typeof(GamePluginContext))]
                public abstract class FakeSecretServer : ISecretWorld
                {
                }
                """),
            "ForgedDuplicateSelectorKernelMethodDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(SecretConsumerSource(), sdkReference);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains(
                "contains an untrusted host call 'host.Sdk.ISecretWorld.Secret'",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Prebuilt_sdk_descriptor_rejects_lookalike_generate_plugin_server_selector()
    {
        var sdkReference = CompilePlainReference(
            ForgedLookalikeSelectorDescriptorSdkSource(),
            "ForgedLookalikeSelectorKernelMethodDescriptorSdk");
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

    private static string ForgedLookalikeSelectorDescriptorSdkSource()
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
            "new global::DotBoxD.Kernels.CallExpression(\"host.Sdk.ISecretWorld.Secret\", " +
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
                "IsAllowed",
                "bool IsAllowed(string,int)",
                {{KernelMethodDescriptorPayload.JsonString(hash)}},
                {{KernelMethodDescriptorPayload.JsonString(json)}})]

            namespace DotBoxD.Abstractions
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Class)]
                public sealed class GeneratePluginServerAttribute : global::System.Attribute
                {
                    public global::System.Type? Context { get; set; }
                }
            }

            namespace Sdk
            {
                [RpcService]
                public interface ISecretWorld
                {
                    [HostBinding("sample.secret.read", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                    int Secret(string id) => throw new System.NotSupportedException("metadata-only world");
                }

                [GeneratePluginServer(Context = typeof(GamePluginContext))]
                public abstract class FakeSecretServer : ISecretWorld
                {
                }

                public sealed class GamePluginContext
                {
                    private readonly HookContext _raw;
                    public GamePluginContext(HookContext raw) => _raw = raw;
                    public HookContext Raw => _raw;
                    public IPluginMessageSink Messages => _raw.Messages;
                    public static GamePluginContext FromHookContext(HookContext raw) => new(raw);

                    [KernelMethod]
                    public bool IsAllowed(string id, int threshold)
                        => throw new System.NotSupportedException("metadata-only descriptor");
                }
            }
            """;
    }

    private static string ForgedForeignSelectorDescriptorSdkSource()
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
            "new global::DotBoxD.Kernels.CallExpression(\"host.Foreign.ISecretWorld.Secret\", " +
                "[__dotboxd_kernel_method_arg_0__], null, Span)");
        var json = payload.ToJson();
        var hash = KernelMethodDescriptorPayload.Hash(json);
        return $$"""
            using DotBoxD.Abstractions;

            [assembly: global::DotBoxD.Abstractions.GeneratedKernelMethodDescriptorAttribute(
                {{KernelMethodDescriptorPayload.CurrentVersion}},
                typeof(global::Sdk.GamePluginContext),
                "IsAllowed",
                "bool IsAllowed(string,int)",
                {{KernelMethodDescriptorPayload.JsonString(hash)}},
                {{KernelMethodDescriptorPayload.JsonString(json)}})]

            namespace Sdk
            {
                public sealed class GamePluginContext
                {
                    private readonly HookContext _raw;
                    public GamePluginContext(HookContext raw) => _raw = raw;
                    public HookContext Raw => _raw;
                    public IPluginMessageSink Messages => _raw.Messages;
                    public static GamePluginContext FromHookContext(HookContext raw) => new(raw);

                    [KernelMethod]
                    public bool IsAllowed(string id, int threshold)
                        => throw new System.NotSupportedException("metadata-only descriptor");
                }
            }
            """;
    }

    private static string ForeignRealSelectorSource()
        => """
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Services.Attributes;
            using Sdk;

            namespace Foreign;

            [RpcService]
            public interface ISecretWorld
            {
                [HostBinding("sample.secret.read", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                int Secret(string id) => throw new System.NotSupportedException("metadata-only world");
            }

            [GeneratePluginServer(Context = typeof(GamePluginContext))]
            public abstract class FakeSecretServer : ISecretWorld
            {
            }
            """;
}
