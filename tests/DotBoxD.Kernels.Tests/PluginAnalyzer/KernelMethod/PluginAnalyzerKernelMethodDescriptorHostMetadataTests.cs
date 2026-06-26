using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod;

public sealed partial class PluginAnalyzerKernelMethodDescriptorTests
{
    [Fact]
    public void Prebuilt_sdk_descriptor_rejects_lookalike_host_capability_metadata()
    {
        var sdkReference = CompilePlainReference(
            ForgedLookalikeHostMetadataDescriptorSdkSource(
                """
                [global::System.AttributeUsage(global::System.AttributeTargets.Method | global::System.AttributeTargets.Property)]
                public sealed class HostCapabilityAttribute : global::System.Attribute
                {
                    public HostCapabilityAttribute(string capability, HostBindingEffect effects) { }
                }
                """,
                "[HostCapability(\"sample.secret.read\", HostBindingEffect.HostStateRead)]"),
            "ForgedLookalikeHostCapabilityDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(SecretConsumerSource(), sdkReference);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains(
                "contains an untrusted host call 'host.Sdk.ISecretWorld.Secret'",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Prebuilt_sdk_descriptor_rejects_lookalike_host_binding_metadata()
    {
        var sdkReference = CompilePlainReference(
            ForgedLookalikeHostMetadataDescriptorSdkSource(
                """
                [global::System.AttributeUsage(global::System.AttributeTargets.Method | global::System.AttributeTargets.Property)]
                public sealed class HostBindingAttribute : global::System.Attribute
                {
                    public HostBindingAttribute(string bindingId, string capability, HostBindingEffect effects) { }
                }
                """,
                "[HostBinding(\"host.Sdk.ISecretWorld.Secret\", \"sample.secret.read\", HostBindingEffect.HostStateRead)]"),
            "ForgedLookalikeHostBindingDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(SecretConsumerSource(), sdkReference);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains(
                "contains an untrusted host call 'host.Sdk.ISecretWorld.Secret'",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Prebuilt_sdk_descriptor_accepts_host_binding_from_inherited_service_interface()
    {
        var sdkReference = CompilePlainReference(
            InheritedServiceDescriptorSdkSource(),
            "InheritedServiceDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(SecretConsumerSource(), sdkReference);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK113");
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains(
                "contains an untrusted host call 'host.Sdk.IGameWorld.Read'",
                StringComparison.Ordinal));
    }

    private static string SecretConsumerSource()
        => PluginAnalyzerKernelMethodDescriptorTestSources.DescriptorlessConsumer.Replace(
            "using Descriptorless;",
            "using Sdk;",
            StringComparison.Ordinal).Replace(
            "ctx.IsClose(e.Distance)",
            "ctx.IsAllowed(e.MonsterId, e.PlayerLevel)",
            StringComparison.Ordinal);

    private static string ForgedLookalikeHostMetadataDescriptorSdkSource(
        string fakeAttribute,
        string methodAttribute)
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
                {{fakeAttribute}}
            }

            namespace Sdk
            {
                [DotBoxDService]
                public interface ISecretWorld
                {
                    {{methodAttribute}}
                    int Secret(string id) => throw new System.NotSupportedException("metadata-only world");
                }

                [GeneratePluginServer(Context = typeof(GamePluginContext))]
                public abstract class GamePluginServer : ISecretWorld
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

    private static string InheritedServiceDescriptorSdkSource()
    {
        var payload = new KernelMethodDescriptorPayload(
            KernelMethodDescriptorPayload.CurrentVersion,
            "global::Sdk.GamePluginContext",
            "IsAllowed",
            "bool IsAllowed(string,int)",
            DotBoxDGenerationNames.ManifestTypes.Bool,
            false,
            new EquatableArray<string>(["sample.read.value"]),
            new EquatableArray<string>([DotBoxDGenerationNames.Effects.HostStateRead]),
            new EquatableArray<KernelMethodDescriptorParameter>(
            [
                new("__dotboxd_kernel_method_arg_0__", DotBoxDGenerationNames.ManifestTypes.String),
                new("__dotboxd_kernel_method_arg_1__", DotBoxDGenerationNames.ManifestTypes.Int)
            ]),
            "Ge(new global::DotBoxD.Kernels.CallExpression(\"host.Sdk.IGameWorld.Read\", " +
                "[__dotboxd_kernel_method_arg_0__], null, Span), __dotboxd_kernel_method_arg_1__)");
        var json = payload.ToJson();
        var hash = KernelMethodDescriptorPayload.Hash(json);
        return $$"""
            using DotBoxD.Abstractions;
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
                [DotBoxDService]
                public interface IGameWorld
                {
                    [HostCapability("sample.read.value", HostBindingEffect.HostStateRead)]
                    int Read(string id) => throw new System.NotSupportedException("metadata-only world");
                }

                public abstract class GamePluginServerBase : IGameWorld
                {
                }

                [GeneratePluginServer(Context = typeof(GamePluginContext))]
                public abstract class GamePluginServer : GamePluginServerBase
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
            }
            """;
    }

}
